using Tesria.Api.Domain;

namespace Tesria.Api.Infrastructure.Email;

/// <summary>
/// One mail provider's settings, as the email settings and the setup wizard
/// offer them (dev-plan 18.1). Choosing one fills the host, port and
/// encryption and says what goes in the username and password; the fields
/// stay editable.
/// </summary>
/// <param name="Id">Stable, stored in <see cref="SiteSettings.SmtpProvider"/>.</param>
/// <param name="Group">How the list is grouped: <c>mail</c> for an email account, <c>service</c> for a sending service.</param>
/// <param name="Username">What goes in the username, in words.</param>
/// <param name="Password">What goes in the password, in words: usually not the account's own password.</param>
/// <param name="SupportPage">The Support site page that walks through it, by title.</param>
/// <param name="SignIn">The provider's own sign-in, when Tesria offers one instead of a password (18.2, 18.3).</param>
/// <param name="Note">Anything the host, port or account needs said, such as a regional address.</param>
public sealed record MailProvider(
    string Id, string Name, string Group,
    string Host, int Port, SmtpTlsMode Tls,
    string Username, string Password, string SupportPage,
    MailSignIn? SignIn = null, string? Note = null);

/// <summary>
/// The presets, in the order they are offered. Every value was checked
/// against the provider's own help pages on 2026-09-24 (the Support pages'
/// source comments name them); the Support site's settings tables are built
/// from this list, so the two cannot disagree.
/// </summary>
public static class MailProviders
{
    public static readonly IReadOnlyList<MailProvider> All =
    [
        new("gmail", "Gmail or Google Workspace", "mail",
            "smtp.gmail.com", 587, SmtpTlsMode.StartTls,
            "Your full Gmail address",
            "An app password, not your Gmail password",
            "Sending with Gmail", MailSignIn.Google),
        new("microsoft", "Outlook or Microsoft 365", "mail",
            "smtp.office365.com", 587, SmtpTlsMode.StartTls,
            "Your full email address",
            "None: sign in with Microsoft instead. A password works only for Microsoft 365, while its administrator allows it",
            "Sending with Outlook or Microsoft 365", MailSignIn.Microsoft,
            Note: "Microsoft 365’s server. A personal Outlook.com account uses smtp-mail.outlook.com, which Tesria picks when one signs in. Microsoft 365 also needs Authenticated SMTP allowed for the mailbox by its administrator."),
        new("icloud", "Apple iCloud Mail", "mail",
            "smtp.mail.me.com", 587, SmtpTlsMode.StartTls,
            "Your full iCloud address, such as name@icloud.com",
            "An app-specific password, not your Apple Account password",
            "Sending with Apple iCloud Mail"),
        new("zoho", "Zoho Mail", "mail",
            "smtppro.zoho.com", 465, SmtpTlsMode.SslOnConnect,
            "Your full Zoho email address",
            "An application-specific password if you use two-factor sign-in; otherwise your Zoho password",
            "Sending with Zoho Mail",
            Note: "For an organization on its own domain. A personal account uses smtp.zoho.com, and each region has its own address (.eu, .in and so on): Zoho Mail’s settings show yours."),
        new("fastmail", "Fastmail", "mail",
            "smtp.fastmail.com", 465, SmtpTlsMode.SslOnConnect,
            "Your full Fastmail address",
            "An app password, not your Fastmail password",
            "Sending with Fastmail",
            Note: "Needs a plan above Basic."),
        new("proton", "Proton Mail", "mail",
            "smtp.protonmail.ch", 587, SmtpTlsMode.StartTls,
            "An address on your own domain",
            "An SMTP token, not your Proton password",
            "Sending with Proton Mail",
            Note: "Paid plans only, and only for an address on your own domain."),

        // Sending services (checked 2026-09-24 against each one's own
        // documentation): made for programs that send email. Every one wants
        // the From address, or its domain, verified with it first.
        new("ses", "Amazon SES", "service",
            "email-smtp.us-east-1.amazonaws.com", 587, SmtpTlsMode.StartTls,
            "The SMTP username from SES’s SMTP settings (not your AWS access key)",
            "The SMTP password shown with it, once",
            "Sending services",
            Note: "Each AWS region has its own address: replace us-east-1 with yours. SMTP credentials also belong to one region."),
        new("postmark", "Postmark", "service",
            "smtp.postmarkapp.com", 587, SmtpTlsMode.StartTls,
            "The server’s API token (or an SMTP token’s access key)",
            "The same API token (or the SMTP token’s secret key)",
            "Sending services",
            Note: "Postmark does not offer port 465; use 587."),
        new("mailgun", "Mailgun", "service",
            "smtp.mailgun.org", 587, SmtpTlsMode.StartTls,
            "The full SMTP login for your domain, such as name@mg.example.com",
            "That login’s SMTP password, from the domain’s SMTP credentials",
            "Sending services",
            Note: "A domain in Mailgun’s EU region uses smtp.eu.mailgun.org."),
        new("sendgrid", "SendGrid", "service",
            "smtp.sendgrid.net", 587, SmtpTlsMode.StartTls,
            "The word apikey, exactly",
            "An API key with Mail Send permission",
            "Sending services"),
        new("brevo", "Brevo", "service",
            "smtp-relay.brevo.com", 587, SmtpTlsMode.StartTls,
            "Your SMTP login, from Brevo’s SMTP settings",
            "An SMTP key (not an API key)",
            "Sending services"),
        new("resend", "Resend", "service",
            "smtp.resend.com", 587, SmtpTlsMode.StartTls,
            "The word resend, exactly",
            "An API key",
            "Sending services"),
        new("smtp2go", "SMTP2GO", "service",
            "mail.smtp2go.com", 587, SmtpTlsMode.StartTls,
            "An SMTP user’s username, from Sending, SMTP Users",
            "That SMTP user’s password",
            "Sending services"),
    ];

    public static MailProvider? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);
}
