using System.Security.Cryptography;
using System.Text;

namespace Tesria.Api.Infrastructure.Webhooks;

/// <summary>
/// Drains queued webhook deliveries and POSTs each one, signing the body with
/// HMAC-SHA256 (over the secret configured when the webhook was created) so
/// receivers can verify the request actually came from this server. Retries a
/// failed delivery a few times with backoff before giving up on it.
/// </summary>
public sealed class WebhookDeliveryBackgroundService(
    ChannelWebhookSender sender, IHttpClientFactory httpClientFactory, ILogger<WebhookDeliveryBackgroundService> logger,
    Security.EgressGuard egress)
    : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var delivery in sender.Reader.ReadAllAsync(stoppingToken))
        {
            await SendWithRetryAsync(delivery, stoppingToken);
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
                // Validated per hop and re-checked at connect time — a hostname
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Webhook {WebhookId} delivery to {Url} failed (attempt {Attempt}/{Max})",
                    delivery.WebhookId, delivery.Url, attempt, MaxAttempts);
            }

            if (attempt < MaxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
    }
}
