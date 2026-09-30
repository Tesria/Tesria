using System.Security.Cryptography;
using System.Text;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Auth;

/// <summary>
/// Changing the sign-in email (t2-009, the owner's choice of 2026-09-29).
///
/// The new address is confirmed before it counts: the change is kept as
/// pending, a link goes to the new address, and a notice to the old one.
/// Only opening the link makes it the sign-in address. It used to take
/// effect at once, with no message to anyone, so a typo locked the person
/// out of Forgot Your Password?, and someone with a borrowed session and the
/// password could move the account with no trace to its owner.
///
/// An instance that sends no email cannot send a link, so there the change
/// still takes effect at once (the profile says so beforehand).
/// </summary>
public static partial class AuthEndpoints
{
    public record ConfirmEmailRequest(string Token);
    public record EmailConfirmedResponse(string Email);

    /// <summary>How long a confirmation link works. Longer than a reset link: it goes to an inbox that may not be the one open now.</summary>
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromHours(24);

    private static async Task<IResult> ChangeEmail(
        ChangeEmailRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current,
        IAuditLogger audit, HttpContext http, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        IConfiguration config, IEmailQueue emailQueue,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.PasswordHash is null)
            return Results.ValidationProblem(Error("email",
                "This account signs in through your identity provider, which owns its email address."));

        if (!hasher.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return Results.ValidationProblem(Error("currentPassword", "Current password is incorrect."));

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
            return Results.ValidationProblem(Error("email", "A valid email address is required."));
        if (email == user.Email)
            return Results.ValidationProblem(Error("email", "That is already your email address."));

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new { message = "An account with this email already exists." });

        var s = await siteSettings.GetAsync();
        if (!s.EmailEnabled)
        {
            // No link can be sent, so there is nothing to wait for. The
            // profile warns before the change is made.
            var previous = user.Email;
            user.Email = email;
            ClearPendingEmail(user);
            // The address is an identity, so the change is worth a record: the
            // old value included, since "who used to be this address" is the
            // question an operator will actually be asking.
            audit.Record("user.email_changed", "user", user.Id, new { From = previous, To = email, How = "at once, email is off" });
            await db.SaveChangesAsync();
            await SignIn(http, db, user, AuthTimeOf(http), SessionIdOf(http.User));
            return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
        }

        // Asking again replaces an earlier request: its link stops working.
        user.PendingEmail = email;
        var token = IssueEmailChangeToken(user);
        audit.Record("user.email_change_requested", "user", user.Id, new { From = user.Email, To = email, Ip = ClientIp(http) });
        await db.SaveChangesAsync();

        SendEmailChangeLink(emailQueue, s, config, user, token);
        SendEmailChangeNotice(emailQueue, s, config, user);
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    /// <summary>A new link to the pending address, the old one no longer working. Only the new address is written to.</summary>
    private static async Task<IResult> ResendEmailChange(
        AppDbContext db, CurrentUser current, IAuditLogger audit, IAccountRecoveryService recovery,
        ISiteSettingsService siteSettings, IConfiguration config, IEmailQueue emailQueue, RecoveryAttemptLimiter limiter,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.PendingEmail is null)
            return Results.ValidationProblem(Error("email", "There is no email change waiting to be confirmed."));
        var s = await siteSettings.GetAsync();
        if (!s.EmailEnabled)
            return Results.ValidationProblem(Error("email", "This instance does not send email at the moment, so the link cannot be sent again."));
        if (!limiter.TryAttempt($"email-change:{user.Id}"))
            return Results.Problem("Too many attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);
        if (await db.Users.AnyAsync(u => u.Email == user.PendingEmail))
            return Results.Conflict(new { message = "An account with this email already exists." });

        var token = IssueEmailChangeToken(user);
        audit.Record("user.email_change_resent", "user", user.Id, new { To = user.PendingEmail });
        await db.SaveChangesAsync();
        SendEmailChangeLink(emailQueue, s, config, user, token);
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> CancelEmailChange(
        AppDbContext db, CurrentUser current, IAuditLogger audit, IAccountRecoveryService recovery,
        ISiteSettingsService siteSettings, Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        CancelPendingEmail(user, audit, "canceled on the profile");
        await db.SaveChangesAsync();
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    /// <summary>
    /// Opens the link: the pending address becomes the sign-in address.
    /// Anonymous, as a reset link is: it may be opened on a device that is
    /// not signed in. The token is the proof; the address is checked again,
    /// since another account may have taken it since the link was sent.
    /// </summary>
    private static async Task<IResult> ConfirmEmailChange(
        ConfirmEmailRequest req, AppDbContext db, IAuditLogger audit, RecoveryAttemptLimiter limiter, HttpContext http)
    {
        var token = (req.Token ?? "").Trim();
        if (!limiter.TryAttempt($"email-token:{token}"))
            return Results.Problem("Too many attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);

        var hash = HashToken(token);
        var now = DateTimeOffset.UtcNow;
        // Only the rows with a change waiting, compared in memory: fixed-time,
        // and SQLite (the tests) cannot compare a DateTimeOffset. The same
        // approach as reset tokens; there are only ever a few pending.
        var candidates = await db.Users.Where(u => u.PendingEmailTokenHash != null).ToListAsync();
        var user = candidates.FirstOrDefault(u =>
            u.PendingEmailExpiresAt > now && FixedTimeEqualsHex(u.PendingEmailTokenHash!, hash));
        if (user is null || user.Status != UserStatus.Active || user.PendingEmail is null)
        {
            audit.RecordAs(null, "user.email_change_failed", "instance", null, new { Ip = ClientIp(http) });
            await db.SaveChangesAsync();
            return Results.ValidationProblem(Error("token", "That link is not valid or has expired. Ask for a new one from your profile."));
        }

        var email = user.PendingEmail;
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id))
        {
            CancelPendingEmail(user, audit, "the address was taken");
            await db.SaveChangesAsync();
            return Results.Conflict(new { message = $"Another account now uses {email}, so your email address was not changed." });
        }

        var previous = user.Email;
        user.Email = email;
        ClearPendingEmail(user);
        audit.RecordAs(user.Id, "user.email_changed", "user", user.Id, new { From = previous, To = email, How = "confirmed by link", Ip = ClientIp(http) });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Taken between the check and the write: the unique index says so.
            return Results.Conflict(new { message = $"Another account now uses {email}, so your email address was not changed." });
        }
        limiter.Reset($"email-token:{token}");
        return Results.Ok(new EmailConfirmedResponse(email));
    }

    /// <summary>
    /// Drops a waiting change, with a record of why, when there is one. A
    /// password change and a password recovery call it: the notice to the old
    /// address tells its owner that changing the password stops the change.
    /// </summary>
    private static void CancelPendingEmail(User user, IAuditLogger audit, string reason)
    {
        if (user.PendingEmail is null) return;
        audit.RecordAs(user.Id, "user.email_change_canceled", "user", user.Id, new { To = user.PendingEmail, Reason = reason });
        ClearPendingEmail(user);
    }

    private static void ClearPendingEmail(User user)
    {
        user.PendingEmail = null;
        user.PendingEmailTokenHash = null;
        user.PendingEmailExpiresAt = null;
    }

    private static string IssueEmailChangeToken(User user)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        user.PendingEmailTokenHash = HashToken(token);
        user.PendingEmailExpiresAt = DateTimeOffset.UtcNow.Add(EmailChangeLifetime);
        return token;
    }

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEqualsHex(string a, string b) =>
        a.Length == b.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>
    /// The link, to the new address. It does not name the old address: a
    /// mistyped new one is a stranger's inbox.
    /// </summary>
    private static void SendEmailChangeLink(IEmailQueue queue, SiteSettings s, IConfiguration config, User user, string token)
    {
        var path = $"/confirm-email?token={token}";
        var link = SiteUrl.Resolve(s, config) + path;
        // Both addresses when Tesria is also on a tailnet, as reset links have.
        var tailnet = Admin.TailscaleEndpoints.AddressOf(config) is { } tailnetAddress ? tailnetAddress + path : null;
        var alsoTailnet = tailnet is not null && tailnet != link
            ? $"If you reach {s.InstanceName} through Tailscale, use this address instead:\n{tailnet}\n\n"
            : "";
        queue.Enqueue(new EmailMessage(
            user.PendingEmail!,
            $"[{s.InstanceName}] Confirm your new email address",
            $"Someone, probably you, asked to sign in to {s.InstanceName} with this address ({user.PendingEmail}) from now on.\n\n" +
            $"Confirm it here (the link works once and expires in 24 hours):\n{link}\n\n" +
            alsoTailnet +
            "Until it is opened, nothing changes. If you did not ask for this, ignore this message."));
    }

    /// <summary>The notice to the address that still signs in, and how to stop the change if it was not them.</summary>
    private static void SendEmailChangeNotice(IEmailQueue queue, SiteSettings s, IConfiguration config, User user)
    {
        var profile = SiteUrl.Resolve(s, config) + "/profile";
        queue.Enqueue(new EmailMessage(
            user.Email,
            $"[{s.InstanceName}] Your sign-in email is about to change",
            $"Someone signed in to your account on {s.InstanceName} asked to change its sign-in email from {user.Email} to {user.PendingEmail}.\n\n" +
            "Nothing changes until the link sent to the new address is opened (it expires in 24 hours). Until then you still sign in with this address.\n\n" +
            "If that was you, there is nothing to do.\n\n" +
            "If it was not you, someone may know your password. Sign in and, on your profile:\n" +
            "- change your password: that stops the email change and signs out every other device;\n" +
            "- or choose Cancel Change on the Email Address card, then look at Sessions and sign out any you do not recognize.\n\n" +
            profile));
    }
}
