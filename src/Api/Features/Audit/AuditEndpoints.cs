using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Audit;

public static class AuditEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    /// <summary>How many entries one request may look through for ones the caller can see.</summary>
    private const int MaxScanned = 5000;

    /// <summary>
    /// Set when there may be older entries: the <c>before</c> to ask for next
    /// (T7-019). Absent once the log, as filtered, has been read to its start.
    /// </summary>
    public const string NextBeforeHeader = "X-Audit-Next-Before";

    public record AuditEntryResponse(
        Guid Id, string Action, string TargetType, Guid? TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson, DateTimeOffset CreatedAt,
        long Sequence);

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder routes)
    {
        // Administrators only. They still do not bypass space permissions, so
        // the visibility filter below applies to them as to anyone.
        routes.MapGet("/audit", List).WithTags("Audit")
            .WithSummary("Audit entries, newest first")
            .WithDescription(
                "Up to `take` entries (50 by default, at most 200), newest first, leaving out any about a space or page " +
                "the caller cannot see. Filters: `action` (an exact name such as `user.role_changed`, or a prefix ending " +
                "in a dot, such as `user.`), `actorId`, `targetType` and `targetId`, and `from` (inclusive) and `to` " +
                "(exclusive) as ISO 8601 times. For older entries, send the `" + NextBeforeHeader + "` response header's " +
                "value as `before` (a sequence number: only entries numbered below it) with the same filters; the " +
                "header is absent when there are no more.")
            .RequireAuthorization()
            .RequirePermission(Infrastructure.Permissions.InstancePermissions.AuditView);
        return routes;
    }

    /// <summary>Most recent audit entries, optionally filtered, one page at a time.</summary>
    private static async Task<IResult> List(
        HttpContext http, AppDbContext db, IPermissionService perms,
        string? targetType, Guid? targetId, int? take,
        long? before, string? action, Guid? actorId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        action = action?.Trim();
        if (action is { Length: > 100 })
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["An action name is at most 100 characters."] });

        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(a => a.TargetType == targetType);
        if (targetId is { } id)
            query = query.Where(a => a.TargetId == id);
        if (!string.IsNullOrEmpty(action))
        {
            // "user." is every user.* action; anything else is one action.
            query = action.EndsWith('.')
                ? query.Where(a => a.Action.StartsWith(action))
                : query.Where(a => a.Action == action);
        }
        if (actorId is { } who)
            query = query.Where(a => a.ActorId == who);

        // Postgres compares timestamps in SQL. The SQLite provider the tests
        // use can neither compare nor order a DateTimeOffset, so there the
        // range is applied to each row as it is read, below.
        var datesInSql = db.Database.IsNpgsql();
        if (datesInSql)
        {
            if (from is { } f) query = query.Where(a => a.CreatedAt >= f);
            if (to is { } t) query = query.Where(a => a.CreatedAt < t);
        }
        bool InRange(AuditLog a) =>
            datesInSql || ((from is not { } f || a.CreatedAt >= f) && (to is not { } t || a.CreatedAt < t));

        // Newest first, by the chain's sequence (it only ever grows, and both
        // databases sort it in SQL). Filtered for what the caller may see
        // batch by batch until the page is full (dev-plan 14.1): before, the
        // limit was applied first and the filter after, so hidden rows used
        // up the page and someone could be shown a handful, or none, of the
        // entries they were entitled to. Entry metadata embeds page titles and
        // space keys, so anything whose target the caller cannot see is
        // dropped; a page that no longer exists (purged) cannot be
        // authorized, so it is hidden too. Scanning stops at a bound so a
        // caller who can see almost nothing does not read the whole log; the
        // next-page header then says where it stopped, so a long stretch of
        // hidden entries does not look like the start of the log (T7-019).
        var rows = new List<AuditLog>();
        var cursor = before;
        var scanned = 0;
        var exhausted = false;
        var batchSize = limit * 2;
        while (rows.Count < limit && scanned < MaxScanned)
        {
            // Rows the chain has not numbered yet (only before its backfill
            // has run) cannot be paged by number, and are left out.
            var batchQuery = cursor is { } b ? query.Where(a => a.Sequence != null && a.Sequence < b) : query.Where(a => a.Sequence != null);
            var batch = await batchQuery.OrderByDescending(a => a.Sequence).Take(batchSize).ToListAsync();
            var readAll = true;
            for (var i = 0; i < batch.Count; i++)
            {
                var a = batch[i];
                scanned++;
                cursor = a.Sequence!.Value;
                var allowed = InRange(a) && a.TargetType switch
                {
                    "space" => a.TargetId is { } sid && await perms.CanViewSpaceAsync(sid),
                    "page" => a.TargetId is { } pid && await perms.CanViewPageAsync(pid),
                    _ => true, // groups and other non-content targets aren't sensitive
                };
                if (allowed) rows.Add(a);
                if (rows.Count == limit)
                {
                    readAll = i == batch.Count - 1;
                    break;
                }
            }
            if (batch.Count < batchSize && readAll)
            {
                exhausted = true;
                break;
            }
        }
        if (!exhausted && cursor is { } next)
            http.Response.Headers[NextBeforeHeader] = next.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Resolve actor names in one round trip.
        var actorIds = rows.Where(r => r.ActorId is not null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return Results.Ok(rows.Select(a => new AuditEntryResponse(
            a.Id, a.Action, a.TargetType, a.TargetId, a.ActorId,
            a.ActorId is { } aid && names.TryGetValue(aid, out var n) ? n : null,
            a.MetadataJson, a.CreatedAt, a.Sequence!.Value)));
    }
}
