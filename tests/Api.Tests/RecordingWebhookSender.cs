using System.Collections.Concurrent;
using Tesria.Api.Infrastructure.Webhooks;

namespace Tesria.Api.Tests;

/// <summary>
/// Test double for <see cref="IWebhookSender"/>: records deliveries instead of
/// making real HTTP calls, so webhook *matching* logic (which webhooks, for
/// which events) can be tested without any network I/O. Real delivery
/// (signing + POSTing) is a separate concern, verified manually against a live
/// listener rather than in this in-process suite.
/// </summary>
public sealed class RecordingWebhookSender : IWebhookSender
{
    private readonly ConcurrentBag<WebhookDelivery> _deliveries = [];

    public IReadOnlyCollection<WebhookDelivery> Deliveries => _deliveries;

    public void Enqueue(WebhookDelivery delivery) => _deliveries.Add(delivery);
}
