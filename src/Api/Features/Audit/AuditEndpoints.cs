using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Audit;

public static class AuditEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public record AuditEntryResponse(
        Guid Id, string Action, string TargetType, Guid? TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/audit", List).WithTags("Audit").RequireAuthorization();
        return routes;
    }

    /// <summary>Most recent audit entries, optionally filtered to one target.</summary>
    private static async Task<IResult> List(
        AppDbContext db, string? targetType, Guid? targetId, int? take)
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
