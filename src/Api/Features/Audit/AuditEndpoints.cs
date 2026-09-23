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
    private static async Task<IResult> List(
        AppDbContext db, IPermissionService perms, string? targetType, Guid? targetId, int? take)
    {
        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(a => a.TargetType == targetType);
        if (targetId is { } id)
            query = query.Where(a => a.TargetId == id);

        // Newest first. Postgres can sort DateTimeOffset in SQL; the SQLite test
        // provider cannot, so there we sort in memory over a bounded window.
        List<AuditLog> rows;
        if (db.Database.IsNpgsql())
        {
            rows = await query.OrderByDescending(a => a.CreatedAt).Take(limit).ToListAsync();
        }
        else
        {
            var all = await query.ToListAsync();
            rows = all.OrderByDescending(a => a.CreatedAt).Take(limit).ToList();
        }

        // Entry metadata embeds page titles and space keys, so drop anything
        // whose target the caller cannot see. A page target that no longer
        // exists (purged) can't be authorized, so it is hidden too.
        var visible = new List<AuditLog>();
        foreach (var a in rows)
        {
            var allowed = a.TargetType switch
            {
                "space" => a.TargetId is { } sid && await perms.CanViewSpaceAsync(sid),
                "page" => a.TargetId is { } pid && await perms.CanViewPageAsync(pid),
                _ => true, // groups and other non-content targets aren't sensitive
            };
            if (allowed) visible.Add(a);
        }
        rows = visible;

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
