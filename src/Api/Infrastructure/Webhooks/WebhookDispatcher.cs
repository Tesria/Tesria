using System.Text.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Webhooks;

/// <summary>
/// Decides which webhooks should hear about an event and hands each one to
/// <see cref="IWebhookSender"/> to actually deliver. Kept separate from
/// delivery so this matching logic (which webhooks, for which events) is
/// testable without any network I/O.
/// </summary>
public interface IWebhookDispatcher
{
    Task DispatchAsync(Guid spaceId, string action, string targetType, Guid targetId, object? metadata = null);
}

public sealed class WebhookDispatcher(AppDbContext db, IWebhookSender sender) : IWebhookDispatcher
{
    public async Task DispatchAsync(
        Guid spaceId, string action, string targetType, Guid targetId, object? metadata = null)
    {
        var webhooks = await db.Webhooks.AsNoTracking()
            .Where(w => w.SpaceId == spaceId && w.Enabled)
            .ToListAsync();

        var matching = webhooks.Where(w => Matches(w.Events, action)).ToList();
        if (matching.Count == 0) return;

        var payload = JsonSerializer.Serialize(new
        {
            @event = action,
            targetType,
            targetId,
            metadata,
            timestamp = DateTimeOffset.UtcNow,
        });

        foreach (var webhook in matching)
            sender.Enqueue(new WebhookDelivery(webhook.Id, webhook.Url, webhook.Secret, payload));
    }

    private static bool Matches(string subscribedEvents, string action) =>
        subscribedEvents == "*" ||
        subscribedEvents.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(action, StringComparer.OrdinalIgnoreCase);
}
