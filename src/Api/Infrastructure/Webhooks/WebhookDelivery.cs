namespace ConfluenceClone.Api.Infrastructure.Webhooks;

/// <summary>One outbound webhook call to make: where to, what to sign with, and what to send.</summary>
public record WebhookDelivery(Guid WebhookId, string Url, string Secret, string PayloadJson);

/// <summary>
/// Accepts webhook deliveries for later sending. Split from
/// <c>IWebhookDispatcher</c> (which decides *who* should receive an event) so
/// the actual network I/O can be swapped out in tests without touching the
/// matching logic.
/// </summary>
public interface IWebhookSender
{
    void Enqueue(WebhookDelivery delivery);
}
