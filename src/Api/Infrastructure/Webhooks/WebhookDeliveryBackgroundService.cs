using System.Security.Cryptography;
using System.Text;

namespace Tesria.Api.Infrastructure.Webhooks;

/// <summary>
/// Drains queued webhook deliveries and POSTs each one, signing the body with
/// HMAC-SHA256 (over the secret configured when the webhook was created) so
/// receivers can verify the request actually came from this server. Retries a
/// failed delivery a few times with backoff before giving up on it.
///
/// **Nothing a receiver does can stop this service, or the app.** A receiver
/// that does not answer within <see cref="Security.EgressGuard.Timeout"/>
/// makes HttpClient throw a <see cref="TaskCanceledException"/>, which is an
/// <see cref="OperationCanceledException"/> although nothing asked this
/// service to stop. That used to escape the loop and, with the host's default
/// exception behavior, shut the whole app down (0.8.1 QA, T5-027). Now only
/// the host's own stopping token ends the loop; every other failure is a
/// failed attempt, logged like the rest.
/// </summary>
public sealed class WebhookDeliveryBackgroundService(
    ChannelWebhookSender sender, IHttpClientFactory httpClientFactory, ILogger<WebhookDeliveryBackgroundService> logger,
    Security.EgressGuard egress)
    : BackgroundService
{
    private const int MaxAttempts = 3;

    /// <summary>The wait before the second attempt; it doubles before each later one (2 s, then 4 s).</summary>
    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a webhook's lane waits for another event before it closes.</summary>
    public TimeSpan LaneIdle { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// One lane per webhook, each sending its own events in order (t5-R09, the
    /// 0.8.3 retest). A single queue meant one slow or failing receiver, with
    /// its 5-second timeouts and retries, held up every other space's
    /// webhooks: 21 seconds an event, 80 with a backlog. Now it holds up only
    /// its own. A lane closes once idle, and the lock makes opening,
    /// writing to and closing a lane one step each, so nothing is written to
    /// a lane that has just closed.
    /// </summary>
    private readonly Dictionary<Guid, System.Threading.Channels.Channel<WebhookDelivery>> _lanes = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var delivery in sender.Reader.ReadAllAsync(stoppingToken))
        {
            lock (_lanes)
            {
                if (!_lanes.TryGetValue(delivery.WebhookId, out var lane))
                {
                    lane = System.Threading.Channels.Channel.CreateUnbounded<WebhookDelivery>(new() { SingleReader = true });
                    _lanes[delivery.WebhookId] = lane;
                    _ = RunLaneAsync(delivery.WebhookId, lane, stoppingToken);
                }
                lane.Writer.TryWrite(delivery);
            }
        }
    }

    private async Task RunLaneAsync(Guid webhookId, System.Threading.Channels.Channel<WebhookDelivery> lane, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (lane.Reader.TryRead(out var delivery))
            {
                try
                {
                    await SendWithRetryAsync(delivery, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // SendWithRetryAsync handles its own failures; this is a
                    // safety net, so one bad delivery never ends the lane.
                    logger.LogError(ex, "Webhook {WebhookId} delivery to {Url} failed unexpectedly; giving up on it",
                        delivery.WebhookId, delivery.Url);
                }
            }
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idle.CancelAfter(LaneIdle);
            try
            {
                if (await lane.Reader.WaitToReadAsync(idle.Token)) continue;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Idle: close, unless an event arrived in the meantime.
            }
            catch (OperationCanceledException)
            {
                return;
            }
            lock (_lanes)
            {
                if (lane.Reader.Count > 0) continue;
                _lanes.Remove(webhookId);
                lane.Writer.TryComplete();
                return;
            }
        }
    }

    private async Task SendWithRetryAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(WebhookDeliveryBackgroundService));
        var bodyBytes = Encoding.UTF8.GetBytes(delivery.PayloadJson);
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(delivery.Secret), bodyBytes)).ToLowerInvariant();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // Validated per hop and re-checked at connect time: a hostname
                // that has come to resolve privately since the webhook was
                // saved is refused here, not fetched.
                using var response = await egress.SendAsync(client, target =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, target)
                    {
                        Content = new ByteArrayContent(bodyBytes),
                    };
                    request.Content.Headers.ContentType = new("application/json");
                    request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
                    return request;
                }, new Uri(delivery.Url), ct);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "Webhook {WebhookId} delivered to {Url} (attempt {Attempt})",
                        delivery.WebhookId, delivery.Url, attempt);
                    return;
                }
                logger.LogWarning(
                    "Webhook {WebhookId} delivery to {Url} returned {Status} (attempt {Attempt}/{Max})",
                    delivery.WebhookId, delivery.Url, (int)response.StatusCode, attempt, MaxAttempts);
            }
            catch (Exception ex) when (ex is Security.EgressBlockedException
                                       || ex.InnerException is Security.EgressBlockedException)
            {
                // Not a transient failure: retrying would only re-refuse it.
                // (The handler wraps a connect-time refusal in HttpRequestException.)
                var reason = (ex as Security.EgressBlockedException ?? ex.InnerException)!.Message;
                logger.LogWarning("Webhook {WebhookId} delivery refused: {Reason}", delivery.WebhookId, reason);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Includes a receiver that did not answer in time: HttpClient
                // reports its own timeout as a TaskCanceledException.
                var reason = ex is OperationCanceledException
                    ? $"no answer within {Security.EgressGuard.Timeout.TotalSeconds:0} seconds"
                    : ex.Message;
                logger.LogWarning(
                    "Webhook {WebhookId} delivery to {Url} failed: {Reason} (attempt {Attempt}/{Max})",
                    delivery.WebhookId, delivery.Url, reason, attempt, MaxAttempts);
            }

            if (attempt < MaxAttempts)
                await Task.Delay(FirstRetryDelay * Math.Pow(2, attempt - 1), ct);
        }
        logger.LogWarning(
            "Webhook {WebhookId} delivery to {Url} failed after {Max} attempts; giving up on this event",
            delivery.WebhookId, delivery.Url, MaxAttempts);
    }
}
