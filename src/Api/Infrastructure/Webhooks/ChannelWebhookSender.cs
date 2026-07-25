using System.Threading.Channels;

namespace ConfluenceClone.Api.Infrastructure.Webhooks;

/// <summary>
/// Queues deliveries onto an in-process channel for <see cref="WebhookDeliveryBackgroundService"/>
/// to send. Keeps outbound HTTP calls (which can be slow or hang) off the
/// request thread — a request that triggers webhooks returns as soon as the
/// delivery is queued, not once it's actually been sent.
/// </summary>
public sealed class ChannelWebhookSender : IWebhookSender
{
    // Bounded so a burst of events can't grow memory unboundedly; a full
    // channel drops the oldest queued delivery rather than blocking the caller.
    private readonly Channel<WebhookDelivery> _channel =
        Channel.CreateBounded<WebhookDelivery>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public ChannelReader<WebhookDelivery> Reader => _channel.Reader;

    public void Enqueue(WebhookDelivery delivery) => _channel.Writer.TryWrite(delivery);
}
