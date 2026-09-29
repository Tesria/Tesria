using System.Collections.Concurrent;
using Tesria.Api.Infrastructure.Email;

namespace Tesria.Api.Tests;

/// <summary>Captures outbound email instead of sending it, so tests can assert on it.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>Flip to simulate a mail server that refuses.</summary>
    public bool Fail { get; set; }

    /// <summary>Flip to simulate a mail server that refuses the recipient for good (a 5xx).</summary>
    public bool FailPermanently { get; set; }

    /// <summary>Every attempt, sent or not.</summary>
    public ConcurrentQueue<EmailMessage> Attempts { get; } = new();

    public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Attempts.Enqueue(message);
        if (FailPermanently) return Task.FromResult(new EmailResult(false, "simulated refusal", Permanent: true));
        if (Fail) return Task.FromResult(new EmailResult(false, "simulated failure"));
        Sent.Enqueue(message);
        return Task.FromResult(new EmailResult(true));
    }
}
