using Microsoft.EntityFrameworkCore;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Every API token on the instance, for administrators (the owner,
/// 2026-09-24): "I have no idea who has created tokens and how often they are
/// in use." A token is a way in that lives outside any browser, so an
/// administrator can see whose it is, how much it is used and from where,
/// and revoke one without breaking that person's other scripts.
///
/// <para>Listing needs <c>users.view</c>, revoking <c>users.manage</c>, as
/// the Users tab's own "Revoke tokens" does, with the same protection: the
/// owner's tokens are the owner's to revoke, and another administrator's need
/// the right to manage administrators' accounts. The token itself is never
/// shown, only the prefix its owner also sees.</para>
/// </summary>
public static class AdminTokenEndpoints
{
    public record TokenOwner(Guid Id, string DisplayName, string Email, bool Suspended);

    /// <summary>Requests through the REST API (reads, changes) and tool calls through MCP (reads, changes).</summary>
    public record UsageCounts(int Reads, int Writes, int McpReads, int McpWrites);

    public record AdminTokenResponse(
        Guid Id, string Name, string Prefix, bool ReadOnly, DateTimeOffset CreatedAt,
        DateTimeOffset? LastUsedAt, string? LastUsedFrom, long UseCount, DateTimeOffset? ExpiresAt, bool Expired,
        TokenOwner Owner, UsageCounts Last7Days);

    /// <summary>The tab's header: how many tokens, how many in use, and use per day.</summary>
    public record TokenSummary(
        int Tokens, int ActiveLast7Days, int NeverUsed, int ExpiringWithinWeek, UsageCounts Last7Days,
        IReadOnlyList<DashboardEndpoints.DailyPoint> ApiPerDay, IReadOnlyList<DashboardEndpoints.DailyPoint> McpPerDay);

    public record ActivityPage(Guid Id, string? Title, string? SpaceKey, bool Hidden);
    public record ActivityResponse(
        Guid Id, DateTimeOffset At, string Tool, bool Write, bool Ok, string? Error,
        Guid TokenId, string TokenName, string TokenPrefix, Guid UserId, string? UserName,
        ActivityPage? Page, string? SpaceKey);

    public static IEndpointRouteBuilder MapAdminTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/api-tokens").WithTags("Admin").RequireAuthorization();
        group.MapGet("/", List).RequirePermission(InstancePermissions.UsersView);
        group.MapGet("/summary", Summary).RequirePermission(InstancePermissions.UsersView);
        group.MapGet("/activity", Activity).RequirePermission(InstancePermissions.UsersView);
        group.MapDelete("/{id:guid}", Revoke).RequirePermission(InstancePermissions.UsersManage);
        return routes;
    }

    private static async Task<IResult> List(AppDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.ApiTokens.AsNoTracking()
            .Join(db.Users.AsNoTracking(), t => t.UserId, u => u.Id, (t, u) => new { t, u })
            .ToListAsync(ct);
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-6);
        var week = (await db.ApiTokenDays.AsNoTracking().Where(d => d.Day >= since).ToListAsync(ct))
            .GroupBy(d => d.TokenId)
            .ToDictionary(g => g.Key, g => new UsageCounts(
                g.Sum(d => d.Reads), g.Sum(d => d.Writes), g.Sum(d => d.McpReads), g.Sum(d => d.McpWrites)));
        var none = new UsageCounts(0, 0, 0, 0);
        // Ordered in memory, as the other admin lists are: SQLite (the test
        // provider) cannot sort a DateTimeOffset. Most recently used first,
        // then never-used tokens by age, so the live ones lead.
        return Results.Ok(rows
            .Select(r => new AdminTokenResponse(
                r.t.Id, r.t.Name, r.t.Prefix, r.t.ReadOnly, r.t.CreatedAt,
                r.t.LastUsedAt, r.t.LastUsedFrom, r.t.UseCount, r.t.ExpiresAt,
                r.t.ExpiresAt is { } e && e <= now,
                new TokenOwner(r.u.Id, r.u.DisplayName, r.u.Email, r.u.Status == Domain.UserStatus.Suspended),
                week.GetValueOrDefault(r.t.Id, none)))
            .OrderByDescending(t => t.LastUsedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(t => t.CreatedAt)
            .ToList());
    }

    private static async Task<IResult> Summary(AppDbContext db, int? days, CancellationToken ct)
    {
        var range = Math.Clamp(days ?? 30, 7, 90);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(1 - range);
        var weekFrom = today.AddDays(-6);
        var tokens = await db.ApiTokens.AsNoTracking().Select(t => new { t.Id, t.LastUsedAt, t.ExpiresAt }).ToListAsync(ct);
        var dayRows = await db.ApiTokenDays.AsNoTracking().Where(d => d.Day >= from).ToListAsync(ct);
        var week = dayRows.Where(d => d.Day >= weekFrom).ToList();

        // Every day in the range, zeros included, so the chart has no gaps.
        var byDay = dayRows.GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.ToList());
        var dates = Enumerable.Range(0, range).Select(i => from.AddDays(i)).ToList();
        List<DashboardEndpoints.DailyPoint> Series(Func<ApiTokenDayRow, int> pick) =>
            [.. dates.Select(d => new DashboardEndpoints.DailyPoint(d,
                byDay.TryGetValue(d, out var rows) ? rows.Sum(r => pick(new ApiTokenDayRow(r.Reads, r.Writes, r.McpReads, r.McpWrites))) : 0))];

        return Results.Ok(new TokenSummary(
            tokens.Count,
            week.Select(d => d.TokenId).Distinct().Count(),
            tokens.Count(t => t.LastUsedAt is null),
            tokens.Count(t => t.ExpiresAt is { } e && e > now && e <= now.AddDays(7)),
            new UsageCounts(week.Sum(d => d.Reads), week.Sum(d => d.Writes), week.Sum(d => d.McpReads), week.Sum(d => d.McpWrites)),
            Series(r => r.Reads + r.Writes),
            Series(r => r.McpReads + r.McpWrites)));
    }

    private readonly record struct ApiTokenDayRow(int Reads, int Writes, int McpReads, int McpWrites);

    /// <summary>
    /// The most recent MCP tool calls, all tokens or one. A page is named only
    /// if the administrator looking may read it: seeing tokens is not a way
    /// around a space's permissions.
    /// </summary>
    private static async Task<IResult> Activity(
        AppDbContext db, IPermissionService perms, Guid? tokenId, int? take, CancellationToken ct)
    {
        var limit = Math.Clamp(take ?? 50, 1, 200);
        // Ordered in memory (SQLite cannot sort a DateTimeOffset); the log is
        // bounded to 90 days, and one token's is small.
        var calls = (await db.McpToolCalls.AsNoTracking()
                .Where(c => tokenId == null || c.TokenId == tokenId)
                .ToListAsync(ct))
            .OrderByDescending(c => c.At).Take(limit).ToList();

        var userIds = calls.Select(c => c.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var pageIds = calls.Where(c => c.PageId != null).Select(c => c.PageId!.Value).Distinct().ToList();
        var pages = await db.Pages.AsNoTracking().IgnoreQueryFilters().Where(p => pageIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, SpaceKey = p.Space!.Key }).ToDictionaryAsync(p => p.Id, ct);
        var visible = new Dictionary<Guid, bool>();
        foreach (var id in pageIds)
            visible[id] = pages.ContainsKey(id) && await perms.CanViewPageAsync(id);

        return Results.Ok(calls.Select(c =>
        {
            ActivityPage? page = null;
            if (c.PageId is { } pid)
                page = visible.GetValueOrDefault(pid) && pages.TryGetValue(pid, out var p)
                    ? new ActivityPage(pid, p.Title, p.SpaceKey, false)
                    : new ActivityPage(pid, null, null, true);
            // A tool's error can quote the page (get_page lists its section
            // headings), so a page the viewer may not read keeps its error
            // to itself as well as its title (the 14.1 review).
            var error = page is { Hidden: true } ? (c.Error is null ? null : "Failed.") : c.Error;
            return new ActivityResponse(c.Id, c.At, c.Tool, c.Write, c.Ok, error,
                c.TokenId, c.TokenName, c.TokenPrefix, c.UserId, names.GetValueOrDefault(c.UserId), page, c.SpaceKey);
        }).ToList());
    }

    private static async Task<IResult> Revoke(
        Guid id, AppDbContext db, CurrentUser current, IInstancePermissions rights,
        IAuditLogger audit, INotificationService notifications, CancellationToken ct)
    {
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null) return Results.NotFound();
        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (owner is null) return Results.NotFound();
        if (await AdminEndpoints.RefuseIfProtectedAccountAsync(owner, current, rights) is { } refused) return refused;

        db.ApiTokens.Remove(token);
        audit.Record("token.revoked_by_admin", "user", owner.Id,
            new { owner.Email, token.Name, token.Prefix, token.UseCount, token.LastUsedFrom });
        // Its owner hears about it, unless they revoked their own: a script
        // that stops working for no visible reason is the worst way to find out.
        if (owner.Id != current.Id)
            await notifications.NotifySystemAsync(owner.Id, "token.revoked", "token", token.Id,
                new { token.Name, token.Prefix });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
