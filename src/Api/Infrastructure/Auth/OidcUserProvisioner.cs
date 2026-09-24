using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Refused to link an OIDC identity to an existing local account because the
/// provider did not assert the email as verified.
/// </summary>
public sealed class OidcEmailNotVerifiedException(string email)
    : Exception($"An account already exists for {email}, and the identity provider did not confirm " +
                "this email is verified, so it cannot be linked automatically. Sign in locally, or verify " +
                "the email with your identity provider, first.");

/// <summary>
/// Refused to create an account through SSO because registration is by
/// invitation. Without this check, "Invite only" held for the sign-up form but
/// not for SSO: with a public provider such as Google, anyone with an account
/// there got one here (found writing the support site, 2026-09-23).
/// </summary>
public sealed class OidcRegistrationClosedException(string email)
    : Exception($"There is no account for {email}, and this instance is invite only. Ask an administrator " +
                "for an invite, create your account from it, then sign in with SSO.");

/// <summary>
/// Resolves an external OIDC identity (the "sub" claim, plus email/name) to a
/// local <see cref="User"/> row: signing in a returning user, linking to an
/// existing local account, or provisioning a brand-new one (PLAN §1: local
/// accounts now, "architected for OIDC/SSO later"). Deliberately independent of
/// ASP.NET Core's OIDC event plumbing so this security-sensitive matching logic
/// is directly unit-testable.
/// </summary>
public interface IOidcUserProvisioner
{
    Task<User> ResolveOrProvisionAsync(string subject, string? email, bool emailVerified, string? displayName);
}

public sealed class OidcUserProvisioner(AppDbContext db, ISiteSettingsService settings, Audit.IAuditLogger audit) : IOidcUserProvisioner
{
    public async Task<User> ResolveOrProvisionAsync(
        string subject, string? email, bool emailVerified, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("The identity provider did not return a subject id.", nameof(subject));

        // Returning user: already linked to this exact external identity.
        var existingBySubject = await db.Users.FirstOrDefaultAsync(u => u.OidcSubject == subject);
        if (existingBySubject is not null) return existingBySubject;

        var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
        if (normalizedEmail.Length == 0)
            throw new InvalidOperationException("The identity provider did not return an email address.");

        var existingByEmail = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existingByEmail is not null)
        {
            // Auto-linking an unverified email would let anyone claiming that
            // address at the IdP take over an existing local account.
            if (!emailVerified) throw new OidcEmailNotVerifiedException(normalizedEmail);

            existingByEmail.OidcSubject = subject;
            await db.SaveChangesAsync();
            return existingByEmail;
        }

        // Same rule as local registration: the first account on an empty instance
        // administers it, however it arrived.
        var isFirstAccount = !await db.Users.AnyAsync();
        // And the same gate: a closed instance signs in the accounts it has
        // and makes no new ones, whichever way the person arrived.
        if (!isFirstAccount && !(await settings.GetAsync()).AllowPublicRegistration)
            throw new OidcRegistrationClosedException(normalizedEmail);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail : displayName.Trim(),
            PasswordHash = null, // OIDC-only account: no local password.
            OidcSubject = subject,
            Status = UserStatus.Active,
            Role = isFirstAccount ? UserRole.Owner : UserRole.Member,
            RoleId = await Permissions.RoleSeed.BuiltInIdAsync(
                db, isFirstAccount ? UserRole.Owner : UserRole.Member),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        // As registration does (dev-plan 14.3): every account's creation is audited.
        audit.RecordAs(user.Id, "user.registered", "user", user.Id, new
        {
            user.Email,
            How = isFirstAccount ? "first account, single sign-on" : "single sign-on",
        });
        await db.SaveChangesAsync();
        return user;
    }
}
