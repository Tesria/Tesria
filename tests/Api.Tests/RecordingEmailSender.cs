using System.Collections.Concurrent;
using Tesria.Api.Infrastructure.Email;

namespace Tesria.Api.Tests;

/// <summary>Captures outbound email instead of sending it, so tests can assert on it.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>Flip to simulate a mail server that refuses.</summary>
    public bool Fail { get; set; }

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (Fail) return Task.FromResult(new EmailResult(false, "simulated failure"));
        Sent.Enqueue(message);
        return Task.FromResult(new EmailResult(true));
    }
}
