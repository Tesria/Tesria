using System.Collections.Concurrent;
using System.Net;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The delivery side of webhooks: the background service that sends what
/// <see cref="WebhookDispatcher"/> queued. The rest of <see cref="WebhookTests"/>
/// records deliveries instead of sending them.
/// </summary>
public class WebhookDeliveryTests
{
    /// <summary>Answers each URL as told, and counts the attempts.</summary>
    private sealed class Receiver(Func<Uri, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public ConcurrentDictionary<string, int> Attempts { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Attempts.AddOrUpdate(request.RequestUri!.AbsolutePath, 1, (_, n) => n + 1);
            return answer(request.RequestUri!, ct);
        }
    }

    private sealed class Clients(HttpMessageHandler handler, TimeSpan timeout) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = timeout };
    }

    private static WebhookDelivery Delivery(string path) =>
        new(Guid.NewGuid(), $"http://127.0.0.1:9{path}", "secret", "{}");

    [Fact]
    public async Task A_receiver_that_does_not_answer_in_time_is_retried_and_given_up_on_without_stopping_the_service()
    {
        // 0.8.1 QA, T5-027: HttpClient's timeout is a TaskCanceledException,
        // which escaped the loop and, with the host's default behavior,
        // stopped the whole app; every delivery still queued was lost.
        var receiver = new Receiver(async (uri, ct) =>
        {
            if (uri.AbsolutePath == "/slow") await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var queue = new ChannelWebhookSender();
        var egress = new EgressGuard(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Egress:AllowedNetworks"] = "127.0.0.1/32" })
            .Build());
        using var service = new WebhookDeliveryBackgroundService(
            queue, new Clients(receiver, TimeSpan.FromMilliseconds(200)),
            NullLogger<WebhookDeliveryBackgroundService>.Instance, egress)
        {
            FirstRetryDelay = TimeSpan.FromMilliseconds(10),
        };

        await service.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(Delivery("/slow"));
            queue.Enqueue(Delivery("/fine"));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!receiver.Attempts.ContainsKey("/fine") && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(50);

            // Three attempts, as documented, then on to the next delivery.
            Assert.Equal(3, receiver.Attempts.GetValueOrDefault("/slow"));
            Assert.Equal(1, receiver.Attempts.GetValueOrDefault("/fine"));
            Assert.False(service.ExecuteTask!.IsCompleted, "the delivery service stopped");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_receiver_that_throws_anything_is_a_failed_attempt_not_a_stopped_service()
    {
        var receiver = new Receiver((uri, _) => uri.AbsolutePath == "/broken"
            ? throw new InvalidOperationException("something unexpected")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var queue = new ChannelWebhookSender();
        var egress = new EgressGuard(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Egress:AllowedNetworks"] = "127.0.0.1/32" })
            .Build());
        using var service = new WebhookDeliveryBackgroundService(
            queue, new Clients(receiver, TimeSpan.FromSeconds(5)),
            NullLogger<WebhookDeliveryBackgroundService>.Instance, egress)
        {
            FirstRetryDelay = TimeSpan.FromMilliseconds(10),
        };

        await service.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(Delivery("/broken"));
            queue.Enqueue(Delivery("/fine"));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (!receiver.Attempts.ContainsKey("/fine") && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(50);

            Assert.Equal(3, receiver.Attempts.GetValueOrDefault("/broken"));
            Assert.Equal(1, receiver.Attempts.GetValueOrDefault("/fine"));
            Assert.False(service.ExecuteTask!.IsCompleted, "the delivery service stopped");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void A_background_job_that_fails_does_not_stop_the_app()
    {
        // The backstop behind every service's own loop: .NET's default is
        // StopHost, which is how one slow webhook receiver took the site down.
        using var factory = new TestAppFactory();
        var options = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;
        Assert.Equal(BackgroundServiceExceptionBehavior.Ignore, options.BackgroundServiceExceptionBehavior);
    }
}
