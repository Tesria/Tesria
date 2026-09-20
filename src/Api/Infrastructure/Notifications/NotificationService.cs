using System.Text.Json;
using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Notifications;

/// <summary>Fans a page or space event out to its watchers as notifications.</summary>
public interface INotificationService
{
    /// <summary>
    /// Queues one notification per watcher of the page (and, if
    /// <paramref name="spaceId"/> is given, of its space too), excluding
    /// <paramref name="actorId"/>. Queued on the current unit of work, so
    /// notifications commit together with the change that triggered them (or
    /// not at all) — the same pattern as <c>IAuditLogger</c>.
    /// </summary>
    Task NotifyPageWatchersAsync(
        Guid pageId, Guid spaceId, string action, Guid actorId, object? metadata = null);

    /// <summary>Queues one notification per watcher of the space, excluding <paramref name="actorId"/>.</summary>
    Task NotifySpaceWatchersAsync(Guid spaceId, string action, Guid actorId, object? metadata = null);

    /// <summary>
    /// Queues a "page.created" notification, for each watcher of the space, that
    /// points at the new page (not the space) — a brand-new page has no
    /// watchers of its own yet, but the notification should still let its
    /// recipient click straight through to it.
    /// </summary>
    Task NotifyOfNewPageAsync(Guid pageId, Guid spaceId, Guid actorId, object? metadata = null);

    /// <summary>
    /// Queues a notification for one named person, regardless of whether they
    /// watch anything — used for a direct mention (dev-plan Phase 7 Wave C).
    /// The caller is responsible for checking that the recipient may see the
    /// target; this does not.
    /// </summary>
    Task NotifyUserAsync(Guid userId, string action, string targetType, Guid targetId, Guid actorId, object? metadata = null);

    /// <summary>
    /// Queues one notification per active administrator (dev-plan 3.3). No
    /// actor: security alerts come from the system, and an admin whose own
    /// action tripped a detector should still hear about it.
    /// </summary>
    Task NotifyAdminsAsync(string action, Guid targetId, object? metadata = null);
}

public sealed class NotificationService(AppDbContext db) : INotificationService
{
    public async Task NotifyPageWatchersAsync(
        Guid pageId, Guid spaceId, string action, Guid actorId, object? metadata = null)
    {
        var recipients = await db.Watches.AsNoTracking()
            .Where(w =>
                (w.TargetType == "page" && w.TargetId == pageId) ||
                (w.TargetType == "space" && w.TargetId == spaceId))
            .Select(w => w.UserId)
            .Distinct()
            .ToListAsync();

        Enqueue(recipients, "page", pageId, action, actorId, metadata);
    }

    public async Task NotifySpaceWatchersAsync(
        Guid spaceId, string action, Guid actorId, object? metadata = null)
    {
        var recipients = await SpaceWatcherIdsAsync(spaceId);
        Enqueue(recipients, "space", spaceId, action, actorId, metadata);
    }

    public async Task NotifyOfNewPageAsync(
        Guid pageId, Guid spaceId, Guid actorId, object? metadata = null)
    {
        var recipients = await SpaceWatcherIdsAsync(spaceId);
        Enqueue(recipients, "page", pageId, "page.created", actorId, metadata);
    }

    public Task NotifyUserAsync(
        Guid userId, string action, string targetType, Guid targetId, Guid actorId, object? metadata = null)
    {
        Enqueue([userId], targetType, targetId, action, actorId, metadata);
        return Task.CompletedTask;
    }

    public async Task NotifyAdminsAsync(string action, Guid targetId, object? metadata = null)
    {
        var admins = await db.Users.AsNoTracking()
            .Where(u => u.Role >= UserRole.Admin && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync();

        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);
        var now = DateTimeOffset.UtcNow;
        foreach (var userId in admins)
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                TargetType = "security",
                TargetId = targetId,
                ActorId = null,
                MetadataJson = metadataJson,
                CreatedAt = now,
            });
    }

    private Task<List<Guid>> SpaceWatcherIdsAsync(Guid spaceId) =>
        db.Watches.AsNoTracking()
            .Where(w => w.TargetType == "space" && w.TargetId == spaceId)
            .Select(w => w.UserId)
            .Distinct()
            .ToListAsync();

    private void Enqueue(
        List<Guid> recipients, string targetType, Guid targetId,
        string action, Guid actorId, object? metadata)
    {
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);
        var now = DateTimeOffset.UtcNow;

        foreach (var userId in recipients)
        {
            // Never notify people about their own action.
            if (userId == actorId) continue;
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                ActorId = actorId,
                MetadataJson = metadataJson,
                CreatedAt = now,
            });
        }
    }
}
