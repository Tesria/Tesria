using System.Net;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Tesria.Api.Infrastructure.Email;

public record EmailMessage(string To, string Subject, string Text);
public record EmailResult(bool Sent, string? Error = null);

/// <summary>Sends one email. Returns rather than throws: callers decide what a failure means.</summary>
public interface IEmailSender
{
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Outbound mail over SMTP (dev-plan 4.1), configured at runtime from the
/// site settings an administrator fills in — no environment variables, no
/// restart. Plain text is the message; a minimal HTML twin is generated
/// from it so clients that prefer HTML get readable line breaks and a link
/// that is clickable. No template engine: every email this app sends is a
/// few sentences and one link.
///
/// With <c>EmailEnabled</c> off, or the settings incomplete, nothing is
/// attempted and the result says so — the "null sender" the plan describes
/// is this same class declining, so there is one code path to reason about.
/// A delivery failure is audited as <c>email.failed</c> (recipient and
/// subject, never the body) and logged.
/// </summary>
public sealed class SmtpEmailSender(
    ISiteSettingsService settings, IAuditLogger audit, AppDbContext db, ILogger<SmtpEmailSender> logger)
    : IEmailSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (!s.EmailEnabled) return new EmailResult(false, "Email is turned off in Settings.");
        if (string.IsNullOrWhiteSpace(s.SmtpHost)) return new EmailResult(false, "No SMTP host is configured.");
        if (string.IsNullOrWhiteSpace(s.SmtpFromAddress)) return new EmailResult(false, "No From address is configured.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(s.InstanceName, s.SmtpFromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            TextBody = message.Text,
            HtmlBody = EmailTemplates.Html(s.InstanceName, message.Text),
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
            var security = s.SmtpTls switch
            {
                Domain.SmtpTlsMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
                Domain.SmtpTlsMode.StartTls => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(s.SmtpHost, s.SmtpPort, security, ct);
            if (!string.IsNullOrEmpty(s.SmtpUsername))
                await client.AuthenticateAsync(new NetworkCredential(s.SmtpUsername, settings.Unprotect(s.SmtpPasswordProtected) ?? ""), ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Email sent to {To}: {Subject}", message.To, message.Subject);
            return new EmailResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Email to {To} failed: {Subject}", message.To, message.Subject);
            audit.RecordAs(null, "email.failed", "instance", null,
                new { message.To, message.Subject, Error = ex.Message });
            try { await db.SaveChangesAsync(ct); } catch (Exception) { /* the audit row is best-effort here */ }
            return new EmailResult(false, ex.Message);
        }
    }
}

/// <summary>The one shape every email takes: the instance name, the text, a footer.</summary>
public static class EmailTemplates
{
    public static string Html(string instanceName, string text)
    {
        var body = WebUtility.HtmlEncode(text).Replace("\n", "<br>\n");
        // Bare URLs become links; nothing else is interpreted.
        body = System.Text.RegularExpressions.Regex.Replace(body,
            @"(https?://[^\s<]+)", "<a href=\"$1\">$1</a>");
        return $"""
            <div style="font-family:-apple-system,Segoe UI,Roboto,sans-serif;font-size:15px;line-height:1.5;color:#172b4d;max-width:36em">
              <p style="font-weight:600;font-size:13px;color:#6b778c;margin:0 0 1em">{WebUtility.HtmlEncode(instanceName)}</p>
              <p>{body}</p>
            </div>
            """;
    }
}

/// <summary>Where this instance lives, for links in email (dev-plan 4.1).</summary>
public static class SiteUrl
{
    /// <summary>The setting wins over the deploy-time value; both trimmed of a trailing slash.</summary>
    public static string Resolve(Domain.SiteSettings settings, IConfiguration config) =>
        (string.IsNullOrWhiteSpace(settings.BaseUrl) ? config["Site:BaseUrl"] : settings.BaseUrl)?.TrimEnd('/')
        ?? "https://localhost";
}
