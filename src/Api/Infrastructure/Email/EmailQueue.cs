using System.Threading.Channels;

namespace Tesria.Api.Infrastructure.Email;

/// <summary>
/// Email sent after the request that asked for it has been answered
/// (dev-plan 14.3). For a message whose sending time must not show in the
/// answer: "reset my password" answers the same way for an address with an
/// account and one without, and sending inline made the first measurably
/// slower. Most email is still sent inline, where the caller wants to know
/// whether it went (invites, the test email).
/// </summary>
public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
}

/// <summary>
/// In memory, like the other queues here (a single app instance). A restart
/// loses what is waiting, which for a reset link means asking again; the
/// queue is bounded so a flood of requests cannot use unbounded memory.
///
/// Nobody is watching these messages go, so a mail server that does not
/// answer must not lose them quietly (t2-013): a failed message is tried
/// again after 1, 2, 4, 8 and 15 minutes, then given up, about half an hour
/// after it was queued (a reset link lasts an hour). The first failure also raises
/// the <c>mail.send_failed</c> alert, so an administrator learns that email
/// is not getting out. A message the server refused for good is not retried.
/// </summary>
public sealed class EmailQueue(IServiceScopeFactory scopes, ILogger<EmailQueue> logger) : BackgroundService, IEmailQueue
{
    private const int MaxWaiting = 1000;

    private sealed record Item(EmailMessage Message, DateTimeOffset QueuedAt, int Attempts);

    private readonly Channel<Item> _pending = Channel.CreateBounded<Item>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private int _waiting;

    /// <summary>The first wait before a retry; later ones double, to at most 15 of these. A setting for tests.</summary>
    public TimeSpan RetryUnit { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long after queuing a message is still tried: 40 retry units, room
    /// for the five retries (30 units of waiting) plus the time each try took.
    /// </summary>
    public TimeSpan RetryFor => RetryUnit * 40;

    public void Enqueue(EmailMessage message)
    {
        if (!_pending.Writer.TryWrite(new Item(message, DateTimeOffset.UtcNow, 0)))
            logger.LogWarning("Email to {To} dropped: the queue is closed", message.To);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _pending.Reader.ReadAllAsync(stoppingToken))
        {
            EmailResult result;
            using var scope = scopes.CreateScope();
            try
            {
                result = await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(item.Message, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The sender reports its own failures; this is anything else.
                logger.LogWarning(ex, "Email to {To} could not be sent", item.Message.To);
                result = new EmailResult(false, ex.Message);
            }
            if (result.Sent || result.Permanent) continue;

            if (item.Attempts == 0) await AlertAsync(scope, result.Error, stoppingToken);

            var attempts = item.Attempts + 1;
            var delay = RetryUnit * Math.Min(Math.Pow(2, attempts - 1), 15);
            if (DateTimeOffset.UtcNow + delay - item.QueuedAt > RetryFor)
            {
                logger.LogWarning("Email to {To} given up after {Attempts} tries: {Subject}", item.Message.To, attempts, item.Message.Subject);
                continue;
            }
            if (Interlocked.Increment(ref _waiting) > MaxWaiting)
            {
                Interlocked.Decrement(ref _waiting);
                logger.LogWarning("Email to {To} dropped: too many messages are waiting to be retried", item.Message.To);
                continue;
            }
            _ = RetryLaterAsync(item with { Attempts = attempts }, delay, stoppingToken);
        }
    }

    private async Task RetryLaterAsync(Item item, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            _pending.Writer.TryWrite(item);
        }
        catch (OperationCanceledException) { /* shutting down: lost, as a restart loses the queue */ }
        finally { Interlocked.Decrement(ref _waiting); }
    }

    private async Task AlertAsync(IServiceScope scope, string? reason, CancellationToken ct)
    {
        try
        {
            await scope.ServiceProvider.GetRequiredService<Security.ISecurityDetector>()
                .MailSendFailedAsync(reason ?? "The mail server did not accept the message.");
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not raise the email alert");
        }
    }
}
