using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Audit;

public static class AuditEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public record AuditEntryResponse(
        Guid Id, string Action, string TargetType, Guid? TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder routes)
    {
        // Administrators only. They still do not bypass space permissions, so
        // the visibility filter below applies to them as to anyone.
        routes.MapGet("/audit", List).WithTags("Audit")
            .RequireAuthorization()
            .RequirePermission(Infrastructure.Permissions.InstancePermissions.AuditView);
        return routes;
    }

    /// <summary>Most recent audit entries, optionally filtered to one target.</summary>
    /// <summary>How many entries one request may look through for ones the caller can see.</summary>
    private const int MaxScanned = 5000;

    private static async Task<IResult> List(
        AppDbContext db, IPermissionService perms, string? targetType, Guid? targetId, int? take)
    {
        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(a => a.TargetType == targetType);
        if (targetId is { } id)
            query = query.Where(a => a.TargetId == id);

        // Newest first, by the chain's sequence (it only ever grows, and both
        // databases sort it in SQL). Filtered for what the caller may see
        // batch by batch until the page is full (dev-plan 14.1): before, the
        // limit was applied first and the filter after, so hidden rows used
        // up the page and someone could be shown a handful, or none, of the
        // entries they were entitled to. Entry metadata embeds page titles and
        // space keys, so anything whose target the caller cannot see is
        // dropped; a page that no longer exists (purged) cannot be
        // authorized, so it is hidden too. Scanning stops at a bound so a
        // caller who can see almost nothing does not read the whole log.
        var rows = new List<AuditLog>();
        long? before = null;
        var scanned = 0;
        while (rows.Count < limit && scanned < MaxScanned)
        {
            // Rows the chain has not numbered yet (only before its backfill
            // has run) cannot be paged by number, and are left out.
            var batchQuery = before is { } b ? query.Where(a => a.Sequence != null && a.Sequence < b) : query.Where(a => a.Sequence != null);
            var batch = await batchQuery.OrderByDescending(a => a.Sequence).Take(limit * 2).ToListAsync();
            if (batch.Count == 0) break;
            scanned += batch.Count;
            before = batch[^1].Sequence!.Value;
            foreach (var a in batch)
            {
                var allowed = a.TargetType switch
                {
                    "space" => a.TargetId is { } sid && await perms.CanViewSpaceAsync(sid),
                    "page" => a.TargetId is { } pid && await perms.CanViewPageAsync(pid),
                    _ => true, // groups and other non-content targets aren't sensitive
                };
                if (allowed) rows.Add(a);
                if (rows.Count == limit) break;
            }
        }

        // Resolve actor names in one round trip.
        var actorIds = rows.Where(r => r.ActorId is not null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return Results.Ok(rows.Select(a => new AuditEntryResponse(
            a.Id, a.Action, a.TargetType, a.TargetId, a.ActorId,
            a.ActorId is { } aid && names.TryGetValue(aid, out var n) ? n : null,
            a.MetadataJson, a.CreatedAt)));
    }
}
