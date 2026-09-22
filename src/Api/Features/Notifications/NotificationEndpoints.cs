using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Notifications;

public static class NotificationEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public record NotificationResponse(
        Guid Id, string Action, string TargetType, Guid TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson,
        DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
    public record UnreadCountResponse(int Count);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();
        group.MapGet("/", List);
        group.MapGet("/unread-count", UnreadCount);
        group.MapPost("/{id:guid}/read", MarkRead);
        group.MapPost("/read-all", MarkAllRead);
        return routes;
    }

    private static async Task<IResult> List(
        AppDbContext db, IPermissionService perms, CurrentUser current, bool? unreadOnly, int? take)
    {
        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        var userId = current.RequireId();

        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly == true) query = query.Where(n => n.ReadAt == null);

        // Newest first, bounded. Postgres can order DateTimeOffset in SQL; the
        // SQLite test provider cannot, so there we sort in memory.
        List<Notification> rows;
        if (db.Database.IsNpgsql())
        {
            rows = await query.OrderByDescending(n => n.CreatedAt).Take(limit).ToListAsync();
        }
        else
        {
            var all = await query.ToListAsync();
            rows = all.OrderByDescending(n => n.CreatedAt).Take(limit).ToList();
        }

        var visible = await FilterViewableAsync(rows, perms);
        return Results.Ok(await ToResponsesAsync(visible, db));
    }

    private static async Task<IResult> UnreadCount(AppDbContext db, IPermissionService perms, CurrentUser current)
    {
        var userId = current.RequireId();
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ToListAsync();
        var visible = await FilterViewableAsync(rows, perms);
        return Results.Ok(new UnreadCountResponse(visible.Count));
    }

    private static async Task<IResult> MarkRead(Guid id, AppDbContext db, CurrentUser current)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == current.RequireId());
        if (n is null) return Results.NotFound();
        n.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllRead(AppDbContext db, CurrentUser current)
    {
        var userId = current.RequireId();
        var now = DateTimeOffset.UtcNow;
        await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
        return Results.NoContent();
    }

    /// <summary>
    /// Drops entries whose target the caller can no longer view. A watch grants
    /// no standing access of its own, so if permissions changed after a
    /// notification was generated (e.g. the space went private), it must not
    /// keep surfacing metadata the caller can no longer see: the same leak
    /// this pattern already closed for the audit log.
    /// </summary>
    private static async Task<List<Notification>> FilterViewableAsync(
        List<Notification> rows, IPermissionService perms)
    {
        var visible = new List<Notification>();
        foreach (var n in rows)
        {
            var allowed = n.TargetType switch
            {
                "space" => await perms.CanViewSpaceAsync(n.TargetId),
                "page" => await perms.CanViewPageAsync(n.TargetId),
                _ => true,
            };
            if (allowed) visible.Add(n);
        }
        return visible;
    }

    private static async Task<IEnumerable<NotificationResponse>> ToResponsesAsync(
        List<Notification> rows, AppDbContext db)
    {
        var actorIds = rows.Where(r => r.ActorId is not null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return rows.Select(n => new NotificationResponse(
            n.Id, n.Action, n.TargetType, n.TargetId, n.ActorId,
            n.ActorId is { } aid && names.TryGetValue(aid, out var name) ? name : null,
            n.MetadataJson, n.CreatedAt, n.ReadAt));
    }
}
