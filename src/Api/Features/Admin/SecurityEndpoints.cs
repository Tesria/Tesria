using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Admin → Security: events, alerts, the blocklist (dev-plan 3.3). The
/// mitigations that act on a user or a setting are the existing endpoints;
/// this file adds only what did not exist.
/// </summary>
public static class SecurityEndpoints
{
    public record EventRow(
        Guid Id, string Kind, SecuritySeverity Severity, string Key, string? Ip, Guid? ActorId,
        string? ActorName, string? TargetType, Guid? TargetId, string? MetadataJson, DateTimeOffset CreatedAt);

    public record AlertRow(
        Guid Id, Guid EventId, string Kind, SecuritySeverity Severity, string Key, string? Ip, Guid? ActorId,
        string? ActorName, SecurityAlertStatus Status, DateTimeOffset CreatedAt,
        DateTimeOffset? AcknowledgedAt, string? AcknowledgedByName,
        DateTimeOffset? ResolvedAt, string? ResolvedByName, string? Note, string? MetadataJson);

    public record BlockRow(Guid Id, string Cidr, string? Reason, string? CreatedByName, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
    public record BlockRequest(string Cidr, string? Reason, int? ExpiresInHours);
    public record NoteRequest(string? Note);

    public record Overview(
        int OpenAlerts, int CriticalOpen, int EventsLast24h, int BlockedNetworks, long BlockedHits,
        bool AllowPublicSpaces, bool AllowPublicRegistration, bool RequireTotpForAdmins);

    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/security").WithTags("Admin").RequireAuthorization();

        group.MapGet("/overview", GetOverview).RequirePermission(InstancePermissions.SecurityView);
        group.MapGet("/events", ListEvents).RequirePermission(InstancePermissions.SecurityView);
        group.MapGet("/alerts", ListAlerts).RequirePermission(InstancePermissions.SecurityView);
        group.MapPost("/alerts/{id:guid}/acknowledge", Acknowledge).RequirePermission(InstancePermissions.SecurityRespond);
        group.MapPost("/alerts/{id:guid}/resolve", Resolve).RequirePermission(InstancePermissions.SecurityRespond);
        group.MapGet("/blocks", ListBlocks).RequirePermission(InstancePermissions.SecurityView);
        group.MapPost("/blocks", AddBlock).RequirePermission(InstancePermissions.SecurityRespond);
        group.MapDelete("/blocks/{id:guid}", RemoveBlock).RequirePermission(InstancePermissions.SecurityRespond);
        return routes;
    }

    private static async Task<IResult> GetOverview(
        AppDbContext db, BlocklistCache blocklist, Infrastructure.Settings.ISiteSettingsService settings)
    {
        var s = await settings.GetAsync();
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        // Postgres compares timestamps in SQL; the SQLite test provider can
        // neither compare nor order a DateTimeOffset, so there the (small)
        // set is counted in memory.
        var recent = db.Database.IsNpgsql()
            ? await db.SecurityEvents.CountAsync(e => e.CreatedAt >= since)
            : (await db.SecurityEvents.AsNoTracking().Select(e => e.CreatedAt).ToListAsync()).Count(t => t >= since);

        return Results.Ok(new Overview(
            OpenAlerts: await db.SecurityAlerts.CountAsync(a => a.Status != SecurityAlertStatus.Resolved),
            CriticalOpen: await db.SecurityAlerts.CountAsync(a => a.Status != SecurityAlertStatus.Resolved && a.Severity == SecuritySeverity.Critical),
            EventsLast24h: recent,
            BlockedNetworks: await db.BlockedNetworks.CountAsync(),
            BlockedHits: blocklist.BlockedHits,
            AllowPublicSpaces: s.AllowPublicSpaces,
            AllowPublicRegistration: s.AllowPublicRegistration,
            RequireTotpForAdmins: s.RequireTotpForAdmins));
    }

    private static async Task<IResult> ListEvents(
        AppDbContext db, string? kind, SecuritySeverity? severity, int? take)
    {
        var limit = Math.Clamp(take ?? 100, 1, 500);
        IQueryable<SecurityEvent> query = db.SecurityEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(e => e.Kind == kind);
        if (severity is { } sev) query = query.Where(e => e.Severity == sev);

        List<SecurityEvent> rows;
        if (db.Database.IsNpgsql())
            rows = await query.OrderByDescending(e => e.CreatedAt).Take(limit).ToListAsync();
        else
            rows = (await query.ToListAsync()).OrderByDescending(e => e.CreatedAt).Take(limit).ToList();

        var names = await NamesAsync(db, rows.Select(r => r.ActorId));
        return Results.Ok(rows.Select(e => new EventRow(
            e.Id, e.Kind, e.Severity, e.Key, e.Ip, e.ActorId, Name(names, e.ActorId),
            e.TargetType, e.TargetId, e.MetadataJson, e.CreatedAt)));
    }

    private static async Task<IResult> ListAlerts(AppDbContext db, string? status)
    {
        IQueryable<SecurityAlert> query = db.SecurityAlerts.AsNoTracking().Include(a => a.Event);
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.Status != SecurityAlertStatus.Resolved);

        var rows = (await query.Take(500).ToListAsync()).OrderByDescending(a => a.CreatedAt).ToList();
        var names = await NamesAsync(db, rows.SelectMany(r => new[] { r.ActorId, r.AcknowledgedById, r.ResolvedById }));
        return Results.Ok(rows.Select(a => ToRow(a, names)));
    }

    private static async Task<IResult> Acknowledge(Guid id, NoteRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var alert = await db.SecurityAlerts.Include(a => a.Event).FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return Results.NotFound();
        if (alert.Status == SecurityAlertStatus.Open)
        {
            alert.Status = SecurityAlertStatus.Acknowledged;
            alert.AcknowledgedAt = DateTimeOffset.UtcNow;
            alert.AcknowledgedById = current.RequireId();
        }
        if (!string.IsNullOrWhiteSpace(req.Note)) alert.Note = req.Note.Trim();
        audit.Record("security.alert_acknowledged", "security", alert.Id, new { alert.Kind });
        await db.SaveChangesAsync();
        return Results.Ok(ToRow(alert, await NamesAsync(db, [alert.ActorId, alert.AcknowledgedById, alert.ResolvedById])));
    }

    private static async Task<IResult> Resolve(Guid id, NoteRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var alert = await db.SecurityAlerts.Include(a => a.Event).FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return Results.NotFound();
        alert.Status = SecurityAlertStatus.Resolved;
        alert.ResolvedAt = DateTimeOffset.UtcNow;
        alert.ResolvedById = current.RequireId();
        alert.AcknowledgedAt ??= alert.ResolvedAt;
        alert.AcknowledgedById ??= alert.ResolvedById;
        if (!string.IsNullOrWhiteSpace(req.Note)) alert.Note = req.Note.Trim();
        audit.Record("security.alert_resolved", "security", alert.Id, new { alert.Kind, alert.Note });
        await db.SaveChangesAsync();
        return Results.Ok(ToRow(alert, await NamesAsync(db, [alert.ActorId, alert.AcknowledgedById, alert.ResolvedById])));
    }

    private static async Task<IResult> ListBlocks(AppDbContext db)
    {
        var rows = (await db.BlockedNetworks.AsNoTracking().ToListAsync()).OrderByDescending(b => b.CreatedAt).ToList();
        var names = await NamesAsync(db, rows.Select(r => r.CreatedById));
        return Results.Ok(rows.Select(b => new BlockRow(b.Id, b.Cidr, b.Reason, Name(names, b.CreatedById), b.CreatedAt, b.ExpiresAt)));
    }

    private static async Task<IResult> AddBlock(
        BlockRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit, BlocklistCache blocklist, HttpContext http)
    {
        if (!BlocklistCache.TryParseCidr(req.Cidr, out var network))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["cidr"] = ["Enter an IP address or CIDR range."] });

        // Locking yourself out is the one mistake this page must not allow.
        var self = http.Connection.RemoteIpAddress;
        if (self is not null && network.Contains(self.IsIPv4MappedToIPv6 ? self.MapToIPv4() : self))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["cidr"] = ["That range includes your own address."] });

        if (req.ExpiresInHours is { } hours && (hours < 1 || hours > 24 * 365))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["expiresInHours"] = ["Between 1 hour and 1 year."] });

        var canonical = $"{network.BaseAddress}/{network.PrefixLength}";
        if (await db.BlockedNetworks.AnyAsync(b => b.Cidr == canonical))
            return Results.Conflict(new { title = "Already blocked." });

        var block = new BlockedNetwork
        {
            Id = Guid.NewGuid(),
            Cidr = canonical,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
            CreatedById = current.RequireId(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = req.ExpiresInHours is { } h ? DateTimeOffset.UtcNow.AddHours(h) : null,
        };
        db.BlockedNetworks.Add(block);
        audit.Record("security.network_blocked", "security", block.Id, new { block.Cidr, block.Reason, block.ExpiresAt });
        await db.SaveChangesAsync();
        blocklist.Reload();

        return Results.Created($"/api/admin/security/blocks/{block.Id}",
            new BlockRow(block.Id, block.Cidr, block.Reason, null, block.CreatedAt, block.ExpiresAt));
    }

    private static async Task<IResult> RemoveBlock(
        Guid id, AppDbContext db, IAuditLogger audit, BlocklistCache blocklist, HttpContext http, IConfiguration config)
    {
        var block = await db.BlockedNetworks.FirstOrDefaultAsync(b => b.Id == id);
        if (block is null) return Results.NotFound();
        // Undoing a mitigation is sudo territory (dev-plan 3.5).
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        db.BlockedNetworks.Remove(block);
        audit.Record("security.network_unblocked", "security", block.Id, new { block.Cidr });
        await db.SaveChangesAsync();
        blocklist.Reload();
        return Results.NoContent();
    }

    private static AlertRow ToRow(SecurityAlert a, Dictionary<Guid, string> names) => new(
        a.Id, a.EventId, a.Kind, a.Severity, a.Key, a.Ip, a.ActorId, Name(names, a.ActorId), a.Status, a.CreatedAt,
        a.AcknowledgedAt, Name(names, a.AcknowledgedById), a.ResolvedAt, Name(names, a.ResolvedById), a.Note,
        a.Event?.MetadataJson);

    private static async Task<Dictionary<Guid, string>> NamesAsync(AppDbContext db, IEnumerable<Guid?> ids)
    {
        var wanted = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (wanted.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? id) =>
        id is { } i && names.TryGetValue(i, out var n) ? n : null;
}
