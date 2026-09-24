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
/// </summary>
public sealed class EmailQueue(IServiceScopeFactory scopes, ILogger<EmailQueue> logger) : BackgroundService, IEmailQueue
{
    private readonly Channel<EmailMessage> _pending = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(EmailMessage message)
    {
        if (!_pending.Writer.TryWrite(message))
            logger.LogWarning("Email to {To} dropped: the queue is closed", message.To);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _pending.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The sender reports its own failures; this is anything else.
                logger.LogWarning(ex, "Email to {To} could not be sent", message.To);
            }
        }
    }
}
