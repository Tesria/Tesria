using System.Data;
using System.Security.Claims;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Auth;

public static class AuthEndpoints
{
    /// <summary>
    /// Claim carrying <see cref="User.SecurityStamp"/>. Shared with
    /// Program.cs's cookie validation, which is where it is checked.
    /// </summary>
    public const string SecurityStampClaim = "tesria:security_stamp";

    /// <summary>
    /// When this session last actually proved who it is: a sign-in, or a
    /// password change. NOT refreshed by re-issuing the cookie for an unrelated
    /// reason (a display-name edit), or renaming yourself would silently extend
    /// the window below.
    /// </summary>
    public const string AuthTimeClaim = "tesria:auth_time";

    /// <summary>
    /// How long after authenticating a session may perform a sensitive account
    /// action without re-entering its password.
    ///
    /// The fresh login *is* the re-authentication: asking for the same password
    /// again seconds after typing it proves nothing and mostly teaches people to
    /// type passwords into prompts. Short enough that a session left open on a
    /// shared machine is not still privileged an hour later.
    /// </summary>
    /// <remarks>
    /// Overridable through <c>Auth:FreshLoginMinutes</c>. That exists so the
    /// tests can shrink the window to nothing and exercise the stale-session
    /// path, which is the half that actually protects anything.
    /// </remarks>
    public const int DefaultFreshLoginMinutes = 15;

    private static TimeSpan FreshAuthWindow(IConfiguration config) =>
        TimeSpan.FromMinutes(config.GetValue("Auth:FreshLoginMinutes", DefaultFreshLoginMinutes));

    /// <summary>Shared by registration and password change, so the two cannot drift apart.</summary>
    public const int MinPasswordLength = 8;

    /// <summary>Which session row this cookie belongs to (dev-plan 3.5).</summary>
    public const string SessionClaim = "tesria:session";

    /// <summary>However active, a session ends this long after it began. <c>Auth:SessionAbsoluteDays</c> overrides.</summary>
    public const int DefaultSessionAbsoluteDays = 90;

    /// <summary>
    /// Destructive administration re-asks for the password unless the session
    /// authenticated within this window (dev-plan 3.5, "sudo mode").
    /// Shorter than the fresh-login window on purpose: minting recovery codes
    /// for yourself is not in the same class as demoting an administrator.
    /// <c>Auth:SudoMinutes</c> overrides.
    /// </summary>
    public const int DefaultSudoMinutes = 5;

    public record RegisterRequest(string Email, string DisplayName, string Password, string? InviteToken);
    public record LoginRequest(string Email, string Password);
    /// <summary>
    /// <paramref name="HasPassword"/> is false for accounts provisioned through
    /// OIDC, whose identity provider owns their email and password. The SPA
    /// renders those fields read-only rather than letting a submit fail.
    /// </summary>
    public record UserResponse(
        Guid Id, string Email, string DisplayName, UserRole Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword,
        int RecoveryCodesRemaining, bool TotpEnabled, bool TotpRequired, EmailNotificationMode EmailNotifications,
        /// <summary>The rights this account holds (dev-plan 11.1); the SPA renders from these.</summary>
        string[] Permissions, string RoleName,
        /// <summary>The owner of an instance whose first-run setup is unfinished (dev-plan 10.2).</summary>
        bool SetupRequired,
        /// <summary>The tour and tips this person has or has not seen (dev-plan 10.3).</summary>
        Onboarding.Summary Onboarding);
    public record NotificationPreferenceRequest(EmailNotificationMode EmailNotifications);

    /// <summary>The password was right; a one-time code is still needed.</summary>
    public record TotpChallengeResponse(bool RequiresTotp, string Challenge);
    public record TotpLoginRequest(string Challenge, string Code);
    public record TotpSetupRequest(string? CurrentPassword);
    public record TotpSetupResponse(string Secret, string OtpauthUri);
    public record TotpCodeRequest(string Code);
    public record TotpDisableRequest(string? CurrentPassword, string? Code);
    public record ReauthRequest(string? Password, string? Code);
    public record SessionResponse(
        Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? Ip, string? UserAgent,
        bool Current, DateTimeOffset? RevokedAt);
    public record OidcStatusResponse(bool Enabled, string DisplayName);
    public record RecoveryOptionsResponse(bool EmailEnabled);
    public record RecoverByEmailRequest(string Email);
    public record UpdateProfileRequest(string DisplayName);
    public record ChangeEmailRequest(string CurrentPassword, string Email);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record RecoverWithCodeRequest(string Email, string Code, string NewPassword);
    public record ResetWithTokenRequest(string Token, string NewPassword);
    public record RegenerateCodesRequest(string? CurrentPassword);
    /// <summary>The plaintext codes. Returned once, at the only moment they exist.</summary>
    public record RecoveryCodesResponse(IReadOnlyList<string> Codes);
    public record RecoveryStatusResponse(int Remaining);
    /// <summary>
    /// Registration's response: the user, plus the recovery codes at the only
    /// moment they exist in plaintext.
    ///
    /// Flattened rather than nesting a <see cref="UserResponse"/>, so this
    /// stays a superset of what registration returned before: every existing
    /// caller reads the same field names and keeps working.
    /// </summary>
    public record RegisteredResponse(
        Guid Id, string Email, string DisplayName, UserRole Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword,
        IReadOnlyList<string> RecoveryCodes);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Auth");

        // The endpoints that take a credential share one per-address budget.
        group.MapPost("/register", Register).RequireRateLimiting(RateLimits.AuthPolicy);
        group.MapPost("/login", Login).RequireRateLimiting(RateLimits.AuthPolicy);
        group.MapPost("/login/totp", LoginWithTotp).RequireRateLimiting(RateLimits.AuthPolicy);
        group.MapPost("/reauth", Reauthenticate).RequireAuthorization().RequireRateLimiting(RateLimits.AuthPolicy);
        group.MapPut("/me/notifications", SetNotificationPreference).RequireAuthorization();
        group.MapPut("/me/onboarding", UpdateOnboarding).RequireAuthorization();
        group.MapGet("/me/sessions", ListSessions).RequireAuthorization();
        group.MapDelete("/me/sessions/others", RevokeOtherSessions).RequireAuthorization();
        group.MapDelete("/me/sessions/{id:guid}", RevokeSession).RequireAuthorization();
        group.MapPost("/me/totp/setup", TotpSetup).RequireAuthorization();
        group.MapPost("/me/totp/enable", TotpEnable).RequireAuthorization();
        group.MapPost("/me/totp/disable", TotpDisable).RequireAuthorization();
        // SignOut result clears the auth cookie; declared without an HttpContext
        // parameter so it binds as a route handler (avoids ASP0016).
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/me", Me);
        group.MapPut("/me", UpdateProfile).RequireAuthorization();
        group.MapPut("/me/email", ChangeEmail).RequireAuthorization();
        group.MapPut("/me/password", ChangePassword).RequireAuthorization();
        group.MapGet("/me/recovery-codes", RecoveryStatus).RequireAuthorization();
        group.MapPost("/me/recovery-codes", RegenerateCodes).RequireAuthorization();
        group.MapPost("/me/recovery-codes/acknowledge", AcknowledgeCodes).RequireAuthorization();
        group.MapGet("/recovery-options", RecoveryOptions);
        group.MapPost("/recover/email", RecoverByEmail).RequireRateLimiting(RateLimits.AuthPolicy);
        group.MapPost("/recover/code", RecoverWithCode).RequireRateLimiting(RateLimits.AuthPolicy);
        group.MapPost("/recover/token", ResetWithToken).RequireRateLimiting(RateLimits.AuthPolicy);

        group.MapGet("/oidc/status", OidcStatus);
        // A full-page browser redirect, not a fetch call: the IdP needs to
        // navigate the user's own browser through its login page.
        group.MapGet("/oidc/login", OidcLogin);

        return routes;
    }

    private static IResult OidcStatus(IConfiguration config)
    {
        var enabled = !string.IsNullOrWhiteSpace(config["Oidc:Authority"]);
        var displayName = config["Oidc:DisplayName"];
        return Results.Ok(new OidcStatusResponse(
            enabled, string.IsNullOrWhiteSpace(displayName) ? "Single sign-on" : displayName));
    }

    private static IResult OidcLogin(string? returnUrl, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config["Oidc:Authority"]))
            return Results.NotFound(new { message = "Single sign-on is not configured on this instance." });

        // Only ever redirect back into this same app, never to an
        // attacker-supplied external URL (an open-redirect otherwise).
        var target = returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = target },
            [OidcAuthenticationDefaults.Scheme]);
    }

    private static async Task<IResult> Register(
        RegisterRequest req, AppDbContext db, IPasswordHasher hasher, HttpContext http,
        ISiteSettingsService settings, IAccountRecoveryService recovery, IInviteService invites,
        ISecurityDetector detector)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var displayName = (req.DisplayName ?? "").Trim();

        if (!IsValidEmail(email))
            return Results.ValidationProblem(Error("email", "A valid email address is required."));
        if (displayName.Length == 0)
            return Results.ValidationProblem(Error("displayName", "Display name is required."));
        if ((req.Password ?? "").Length < MinPasswordLength)
            return Results.ValidationProblem(Error("password",
                $"Password must be at least {MinPasswordLength} characters."));

        // Both the duplicate-email check and "is this the first account?" read the
        // table before writing to it, so they run in one serializable transaction:
        // without it, two simultaneous first registrations could each observe an
        // empty table and both be created as admin.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new { message = "An account with this email already exists." });

        // The first account on an empty instance owns it (dev-plan 10.1):
        // otherwise a fresh install has content and nobody able to manage it.
        var isFirstAccount = !await db.Users.AnyAsync();

        // Closed registration is deliberately ignored for that very first
        // account: otherwise an operator who turns it off before anyone has
        // signed up can never set the instance up at all.
        Invite? invite = null;
        if (!isFirstAccount && !(await settings.GetAsync()).AllowPublicRegistration)
        {
            invite = await invites.FindUsableAsync(req.InviteToken ?? "", email);
            if (invite is null)
                return Results.Problem(
                    "Registration is by invitation on this instance.",
                    statusCode: StatusCodes.Status403Forbidden);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            PasswordHash = hasher.Hash(req.Password!),
            Status = UserStatus.Active,
            Role = isFirstAccount ? UserRole.Owner : UserRole.Member,
            RoleId = await Infrastructure.Permissions.RoleSeed.BuiltInIdAsync(
                db, isFirstAccount ? UserRole.Owner : UserRole.Member),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);

        // Issued here rather than on demand: codes are only useful if they
        // exist before they are needed, and a prompt later is a prompt most
        // people dismiss.
        var codes = recovery.IssueCodes(user.Id);

        // Spent inside the same transaction as the account it created, so a
        // failure part-way cannot burn an invite without producing a user.
        if (invite is not null)
        {
            invite.UsedAt = DateTimeOffset.UtcNow;
            invite.UsedByUserId = user.Id;
        }

        await detector.RegistrationAsync(ClientIp(http));
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await SignIn(http, db, user);
        return Results.Ok(new RegisteredResponse(
            user.Id, user.Email, user.DisplayName, user.Role,
            user.AvatarHash, user.AvatarVariant, user.PasswordHash != null, codes));
    }

    private static async Task<IResult> Login(
        LoginRequest req, AppDbContext db, IPasswordHasher hasher, HttpContext http,
        IAuditLogger audit, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        ISecurityDetector detector, ITotpService totp,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        var now = DateTimeOffset.UtcNow;

        // Verify even when the user is missing to keep timing uniform, and return
        // the same generic error for "no such user" and "wrong password".
        var ok = user?.PasswordHash is not null
            && hasher.Verify(req.Password ?? "", user.PasswordHash);

        // A locked account fails even with the right password, with the same
        // response: the lock must not become a way to confirm the password.
        // The right password does not reset the counter while locked, or the
        // attacker who just found it clears their own lock.
        var locked = user is not null && AuthLockout.IsLocked(user, now);

        if (!ok || locked || user!.Status != UserStatus.Active)
        {
            // Deliberately records no email and no user id: an audit log readable
            // by every admin should not become a list of addresses somebody tried,
            // and a failure cannot be attributed to an account without confirming
            // that the account exists. The per-account counter lives on the
            // user row, where only administrators see it.
            audit.RecordAs(null, "user.login_failed", "instance", null,
                new { Ip = ClientIp(http) });
            await detector.FailedLoginAsync(ClientIp(http), email);
            if (user is not null && !locked)
            {
                AuthLockout.RecordFailure(user, await siteSettings.GetAsync(), now);
                if (AuthLockout.IsLocked(user, now)) await detector.AccountLockedAsync(user, ClientIp(http));
            }
            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }

        // The one moment the plaintext is in hand: bring an old hash up to the
        // current parameters (dev-plan 3.5).
        if (hasher.NeedsRehash(user.PasswordHash!)) user.PasswordHash = hasher.Hash(req.Password!);

        if (user.TotpEnabledAt is not null)
        {
            // Password accepted; not signed in. The challenge proves that step
            // to the code endpoint. The failure counter is not reset yet: a
            // wrong code counts as a failure too.
            await db.SaveChangesAsync();
            return Results.Ok(new TotpChallengeResponse(true, totp.IssueChallenge(user.Id, ClientIp(http))));
        }

        return await CompleteSignInAsync(db, http, audit, recovery, siteSettings, detector, user, totp: false, rights);
    }

    /// <summary>The second step of a two-factor sign-in: a one-time code, or a recovery code in its place.</summary>
    private static async Task<IResult> LoginWithTotp(
        TotpLoginRequest req, AppDbContext db, HttpContext http, IAuditLogger audit,
        IAccountRecoveryService recovery, ISiteSettingsService siteSettings, ISecurityDetector detector,
        ITotpService totp,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var userId = totp.RedeemChallenge(req.Challenge, ClientIp(http));
        var user = userId is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var now = DateTimeOffset.UtcNow;

        if (user is null || user.TotpEnabledAt is null || user.Status != UserStatus.Active || AuthLockout.IsLocked(user, now))
        {
            audit.RecordAs(null, "user.login_failed", "instance", null, new { Ip = ClientIp(http), Step = "totp" });
            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }

        // A recovery code stands in for the authenticator (dev-plan 1.3/3.5:
        // one set of codes, not two). Spent on use.
        var ok = totp.Verify(user, req.Code)
            || (await recovery.RedeemCodeAsync(user.Email, req.Code ?? "")) is not null;
        if (!ok)
        {
            // Six digits is a small space; wrong codes count toward the lockout.
            audit.RecordAs(null, "user.login_failed", "instance", null, new { Ip = ClientIp(http), Step = "totp" });
            await detector.FailedLoginAsync(ClientIp(http), user.Email);
            AuthLockout.RecordFailure(user, await siteSettings.GetAsync(), now);
            if (AuthLockout.IsLocked(user, now)) await detector.AccountLockedAsync(user, ClientIp(http));
            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }

        return await CompleteSignInAsync(db, http, audit, recovery, siteSettings, detector, user, totp: true, rights);
    }

    /// <summary>What both sign-in paths share once identity is proven.</summary>
    private static async Task<IResult> CompleteSignInAsync(
        AppDbContext db, HttpContext http, IAuditLogger audit, IAccountRecoveryService recovery,
        ISiteSettingsService siteSettings, ISecurityDetector detector, User user, bool totp,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        AuthLockout.Reset(user);
        // Before the login row is written, so the history it consults is the
        // history *before* this sign-in.
        await detector.SucceededLoginAsync(user, ClientIp(http));
        audit.RecordAs(user.Id, "user.login", "user", user.Id, new { Ip = ClientIp(http), Totp = totp });
        await db.SaveChangesAsync();

        await SignIn(http, db, user);
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    /// <summary>
    /// Confirms the password (or a one-time code) for a session that is past
    /// the sudo window, re-issuing the cookie with a fresh auth time.
    /// </summary>
    private static async Task<IResult> Reauthenticate(
        ReauthRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current, HttpContext http,
        IAuditLogger audit, ITotpService totp, ISiteSettingsService siteSettings, ISecurityDetector detector)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        var now = DateTimeOffset.UtcNow;
        if (!await VerifyPasswordOrCodeAsync(
                req.Password, req.Code, user, db, hasher, totp, siteSettings, detector, http, now))
            return Results.Unauthorized();

        audit.Record("user.reauthenticated", "user", user.Id);
        await db.SaveChangesAsync();
        await SignIn(http, db, user, authTime: now, sessionId: SessionIdOf(http.User));
        return Results.NoContent();
    }

    /// <summary>
    /// Records that this person says they have saved their recovery codes
    /// (dev-plan 10.2). Its own endpoint rather than a flag on the generate
    /// call, because the claim is made after the codes have been read, and
    /// the wizard will not continue until it is.
    /// </summary>
    /// <summary>
    /// The tour and tips, for this account only (dev-plan 10.3). Every field
    /// is optional, so the SPA sends one thing at a time: dismissing a tip
    /// should not be able to turn the tour back on by omission.
    /// </summary>
    private static async Task<IResult> UpdateOnboarding(
        Onboarding.UpdateRequest req, AppDbContext db, CurrentUser current)
    {
        // No status check here: OnValidatePrincipal already rejects the
        // cookie of any account that is not Active, so a suspended one never
        // reaches this handler at all.
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        var summary = Onboarding.Apply(user, req);
        await db.SaveChangesAsync();
        return Results.Ok(summary);
    }

    private static async Task<IResult> AcknowledgeCodes(
        AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.RecoveryCodesAcknowledgedAt is null)
        {
            user.RecoveryCodesAcknowledgedAt = DateTimeOffset.UtcNow;
            audit.Record("user.recovery_codes_acknowledged", "user", user.Id);
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        AppDbContext db, CurrentUser current, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        if (current.Id is not { } id) return Results.Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        return user is null ? Results.Unauthorized() : Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> Logout(AppDbContext db, HttpContext http)
    {
        // Revoke the row, not just the cookie: a copy of the cookie taken
        // earlier must not outlive the sign-out.
        if (SessionIdOf(http.User) is { } sessionId)
            await db.UserSessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        return Results.SignOut(new AuthenticationProperties(), [CookieAuthenticationDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> SetNotificationPreference(
        NotificationPreferenceRequest req, AppDbContext db, CurrentUser current,
        IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        user.EmailNotifications = req.EmailNotifications;
        await db.SaveChangesAsync();
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> ListSessions(AppDbContext db, CurrentUser current, HttpContext http)
    {
        var mine = SessionIdOf(http.User);
        var rows = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == current.RequireId())
            .ToListAsync();
        // Live sessions first, newest first; a few recently revoked ones for
        // context. Ordered in memory (SQLite cannot order DateTimeOffset).
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        return Results.Ok(rows
            .Where(s => s.RevokedAt == null || s.RevokedAt > cutoff)
            .OrderBy(s => s.RevokedAt != null)
            .ThenByDescending(s => s.LastSeenAt)
            .Select(s => new SessionResponse(s.Id, s.CreatedAt, s.LastSeenAt, s.Ip, s.UserAgent, s.Id == mine, s.RevokedAt)));
    }

    private static async Task<IResult> RevokeSession(Guid id, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == current.RequireId());
        if (session is null) return Results.NotFound();
        session.RevokedAt ??= DateTimeOffset.UtcNow;
        audit.Record("user.session_revoked", "user", current.RequireId(), new { SessionId = id });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOtherSessions(AppDbContext db, CurrentUser current, HttpContext http, IAuditLogger audit)
    {
        var mine = SessionIdOf(http.User);
        var others = await db.UserSessions
            .Where(s => s.UserId == current.RequireId() && s.RevokedAt == null && s.Id != mine)
            .ToListAsync();
        foreach (var s in others) s.RevokedAt = DateTimeOffset.UtcNow;
        audit.Record("user.other_sessions_revoked", "user", current.RequireId(), new { Count = others.Count });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> TotpSetup(
        TotpSetupRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current, HttpContext http,
        ITotpService totp, ISiteSettingsService siteSettings, IConfiguration config)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.PasswordHash is null)
            return Results.ValidationProblem(Error("currentPassword",
                "This account signs in through your identity provider, which handles two-factor."));

        // The same rule as recovery codes: a supplied password must be right;
        // omitting it is allowed only inside the fresh-login window.
        if (!string.IsNullOrEmpty(req.CurrentPassword))
        {
            if (!hasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.ValidationProblem(Error("currentPassword", "Current password is incorrect."));
        }
        else if (!IsFreshlyAuthenticated(http, FreshAuthWindow(config)))
        {
            return Results.ValidationProblem(Error("currentPassword", "Enter your password to set up two-factor sign-in."));
        }

        var (secret, uri) = totp.BeginEnrolment(user, (await siteSettings.GetAsync()).InstanceName);
        await db.SaveChangesAsync();
        return Results.Ok(new TotpSetupResponse(secret, uri));
    }

    private static async Task<IResult> TotpEnable(
        TotpCodeRequest req, AppDbContext db, CurrentUser current, HttpContext http, IAuditLogger audit,
        ITotpService totp, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.TotpPendingSecretProtected is null)
            return Results.ValidationProblem(Error("code", "Start by scanning a new secret."));
        if (!totp.Verify(user, req.Code, pending: true))
            return Results.ValidationProblem(Error("code", "That code is not right. Check the time on your device and try the next one."));

        totp.Enable(user);
        // Every other session is signed out: whoever else holds a cookie for
        // this account should have to pass the new second factor.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("user.totp_enabled", "user", user.Id);
        await db.SaveChangesAsync();
        await SignIn(http, db, user, AuthTimeOf(http), SessionIdOf(http.User));
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> TotpDisable(
        TotpDisableRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current, HttpContext http,
        IAuditLogger audit, ITotpService totp, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.TotpEnabledAt is null) return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));

        // Turning the second factor off needs the second factor or the
        // password, never just a live session.
        var ok = (!string.IsNullOrEmpty(req.Code) && totp.Verify(user, req.Code))
            || (!string.IsNullOrEmpty(req.CurrentPassword) && user.PasswordHash is not null && hasher.Verify(req.CurrentPassword, user.PasswordHash));
        if (!ok) return Results.ValidationProblem(Error("code", "Enter your current password or a code from your authenticator."));

        if ((await siteSettings.GetAsync()).RequireTotpForAdmins && user.Role >= UserRole.Admin)
            return Results.ValidationProblem(Error("code", "Administrators on this instance must keep two-factor on."));

        totp.Disable(user);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("user.totp_disabled", "user", user.Id);
        await db.SaveChangesAsync();
        await SignIn(http, db, user, AuthTimeOf(http), SessionIdOf(http.User));
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    /// <summary>
    /// Issues the cookie. A new session row is created unless
    /// <paramref name="sessionId"/> names an existing one: re-issues after a
    /// profile edit or a stamp rotation keep the session they started with.
    /// </summary>
    private static async Task SignIn(
        HttpContext http, AppDbContext db, User user, DateTimeOffset? authTime = null, Guid? sessionId = null)
    {
        var authenticatedAt = authTime ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;

        if (sessionId is null || !await db.UserSessions.AnyAsync(s => s.Id == sessionId && s.RevokedAt == null))
        {
            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CreatedAt = now,
                LastSeenAt = now,
                Ip = ClientIp(http),
                UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 300),
            };
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            // Compared against the stored stamp on every request, so rotating
            // it revokes every existing cookie for this account immediately.
            new(SecurityStampClaim, user.SecurityStamp),
            new(AuthTimeClaim, authenticatedAt.ToUnixTimeSeconds().ToString()),
            new(SessionClaim, sessionId.Value.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private static string? Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

    public static Guid? SessionIdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionClaim), out var id) ? id : null;

    /// <summary>
    /// Verifies a password or one-time code against <paramref name="user"/>,
    /// with the same rules and consequences as <c>/auth/reauth</c>: a locked
    /// account is refused outright, and a wrong answer is recorded as a failed
    /// sign-in and counts toward lockout, because a guess here is a guess at
    /// the password.
    ///
    /// Shared so that an action too grave for the five-minute sudo window can
    /// ask for the password inside its own request (dev-plan 11.3). The caller
    /// saves; this only writes the failure path, which must persist even when
    /// the caller then abandons its own work.
    /// </summary>
    public static async Task<bool> VerifyPasswordOrCodeAsync(
        string? password, string? code, User user, AppDbContext db, IPasswordHasher hasher,
        ITotpService totp, ISiteSettingsService siteSettings, ISecurityDetector detector,
        HttpContext http, DateTimeOffset now)
    {
        if (AuthLockout.IsLocked(user, now)) return false;

        var ok = (!string.IsNullOrEmpty(password) && user.PasswordHash is not null && hasher.Verify(password, user.PasswordHash))
            || (!string.IsNullOrEmpty(code) && totp.Verify(user, code));
        if (ok) return true;

        AuthLockout.RecordFailure(user, await siteSettings.GetAsync(), now);
        if (AuthLockout.IsLocked(user, now)) await detector.AccountLockedAsync(user, ClientIp(http));
        await db.SaveChangesAsync();
        return false;
    }

    /// <summary>
    /// Refuses a destructive administrative action from a session past the
    /// sudo window (dev-plan 3.5). The 403 carries <c>code: reauth_required</c>
    /// so the SPA can ask for the password and retry, rather than fail.
    /// </summary>
    public static IResult? RequireSudo(HttpContext http, IConfiguration config)
    {
        var window = TimeSpan.FromMinutes(config.GetValue("Auth:SudoMinutes", DefaultSudoMinutes));
        if (IsFreshlyAuthenticated(http, window)) return null;
        return Results.Json(new
        {
            title = "Re-authentication required",
            status = 403,
            code = "reauth_required",
            detail = "Confirm your password to continue.",
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<UserResponse> ResponseForAsync(
        AppDbContext db, IAccountRecoveryService recovery, ISiteSettingsService siteSettings, User user,
        Infrastructure.Permissions.IInstancePermissions? rights = null)
    {
        var remaining = await recovery.RemainingCodesAsync(user.Id);
        var enabled = user.TotpEnabledAt is not null;
        var required = user.Role >= UserRole.Admin && !enabled && (await siteSettings.GetAsync()).RequireTotpForAdmins;
        // The SPA renders from these: which nav entries, tabs and buttons
        // exist at all (dev-plan 11.1). Every one is enforced server-side too.
        var held = rights is null ? [] : (await rights.ForUserAsync(user.Id)).Order().ToArray();
        var roleName = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync() ?? Role.NameFor(user.Role);
        var setupRequired = await Features.Setup.SetupEndpoints.RequiredForAsync(user, siteSettings);
        return new UserResponse(user.Id, user.Email, user.DisplayName, user.Role, user.AvatarHash, user.AvatarVariant,
            user.PasswordHash != null, remaining, enabled, required, user.EmailNotifications, held, roleName,
            setupRequired, Onboarding.SummaryFor(user));
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest req, AppDbContext db, CurrentUser current, HttpContext http,
        IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var displayName = (req.DisplayName ?? "").Trim();
        if (displayName.Length == 0)
            return Results.ValidationProblem(Error("displayName", "Display name is required."));
        if (displayName.Length > 200)
            return Results.ValidationProblem(Error("displayName", "Display name is too long."));

        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        user.DisplayName = displayName;
        await db.SaveChangesAsync();

        // The name is carried in the cookie's claims, so re-issue it: otherwise
        // the topbar would keep showing the old name until the next sign-in.
        await SignIn(http, db, user, AuthTimeOf(http), SessionIdOf(http.User));
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> ChangeEmail(
        ChangeEmailRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current,
        IAuditLogger audit, HttpContext http, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
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

        if (email != user.Email && await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new { message = "An account with this email already exists." });

        var previous = user.Email;
        user.Email = email;
        // The address is an identity, so the change is worth a record: the old
        // value included, since "who used to be this address" is the question
        // an operator will actually be asking.
        audit.Record("user.email_changed", "user", user.Id, new { From = previous, To = email });
        await db.SaveChangesAsync();

        await SignIn(http, db, user, AuthTimeOf(http), SessionIdOf(http.User));
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current,
        IAuditLogger audit, HttpContext http, IAccountRecoveryService recovery, ISiteSettingsService siteSettings,
        Infrastructure.Permissions.IInstancePermissions rights)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.PasswordHash is null)
            return Results.ValidationProblem(Error("newPassword",
                "This account signs in through your identity provider, which owns its password."));

        if (!hasher.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return Results.ValidationProblem(Error("currentPassword", "Current password is incorrect."));

        // Same rule as registration, in one place rather than two.
        if ((req.NewPassword ?? "").Length < MinPasswordLength)
            return Results.ValidationProblem(Error("newPassword",
                $"Password must be at least {MinPasswordLength} characters."));

        user.PasswordHash = hasher.Hash(req.NewPassword!);
        // Rotating the stamp is what actually signs the other sessions out: a
        // stolen cookie stops working on its next request, which is the point of
        // changing a password you think someone else has.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("user.password_changed", "user", user.Id);
        await db.SaveChangesAsync();

        // Re-issue this session with the new stamp, so the person who just
        // changed their password is not signed out along with everyone else.
        await SignIn(http, db, user, sessionId: SessionIdOf(http.User));
        return Results.Ok(await ResponseForAsync(db, recovery, siteSettings, user, rights));
    }

    private static async Task<IResult> RecoveryStatus(
        AppDbContext db, CurrentUser current, IAccountRecoveryService recovery)
    {
        var remaining = await recovery.RemainingCodesAsync(current.RequireId());
        return Results.Ok(new RecoveryStatusResponse(remaining));
    }

    private static async Task<IResult> RegenerateCodes(
        RegenerateCodesRequest req, AppDbContext db, IPasswordHasher hasher,
        CurrentUser current, IAuditLogger audit, IAccountRecoveryService recovery, HttpContext http,
        IConfiguration config)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.PasswordHash is null)
            return Results.ValidationProblem(Error("currentPassword",
                "This account signs in through your identity provider and does not use recovery codes."));

        // Two ways to prove this is really you, and the order matters.
        //
        // If a password was supplied it must be correct, even in a fresh
        // session. Accepting a wrong one because the session happens to be
        // recent would tell someone their password was right when it was not.
        //
        // If none was supplied, a recent sign-in stands in for it: the sign-in
        // *is* the re-authentication, and demanding the same password seconds
        // after it was typed proves nothing while training people to retype
        // passwords into prompts. Outside that window it is required again, so
        // a session left open on a shared machine cannot mint codes.
        if (!string.IsNullOrEmpty(req.CurrentPassword))
        {
            if (!hasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.ValidationProblem(Error("currentPassword", "Current password is incorrect."));
        }
        else if (!IsFreshlyAuthenticated(http, FreshAuthWindow(config)))
        {
            return Results.ValidationProblem(Error("currentPassword",
                "Enter your password to generate new recovery codes."));
        }

        var codes = recovery.IssueCodes(user.Id);
        audit.Record("user.recovery_codes_regenerated", "user", user.Id);
        await db.SaveChangesAsync();

        return Results.Ok(new RecoveryCodesResponse(codes));
    }

    /// <summary>
    /// Spends a recovery code to set a new password.
    ///
    /// Anonymous by necessity: the whole point is that the caller cannot sign
    /// in. Every failure returns the same 400 whatever went wrong, so this
    /// cannot be used to discover which addresses have accounts.
    /// </summary>
    /// <summary>Which recovery paths the sign-in page should offer.</summary>
    private static async Task<IResult> RecoveryOptions(ISiteSettingsService settings) =>
        Results.Ok(new RecoveryOptionsResponse((await settings.GetAsync()).EmailEnabled));

    /// <summary>
    /// Emails a one-time reset link (dev-plan 4.2). Always 202 with the same
    /// body: whether the address has an account, whether email is even on,
    /// whether the send succeeded: none of it is told to the caller, who
    /// may be probing. The account holder finds out by checking their inbox.
    /// </summary>
    private static async Task<IResult> RecoverByEmail(
        RecoverByEmailRequest req, AppDbContext db, IAccountRecoveryService recovery,
        RecoveryAttemptLimiter limiter, ISiteSettingsService settings, IConfiguration config,
        Infrastructure.Email.IEmailSender email, IAuditLogger audit, HttpContext http)
    {
        var address = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!limiter.TryAttempt(address))
            return Results.Problem("Too many attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);

        var accepted = Results.Accepted(value: new
        {
            message = "If that address has an account, a reset link is on its way. It works once and expires in an hour.",
        });

        var s = await settings.GetAsync();
        if (!s.EmailEnabled || !IsValidEmail(address)) return accepted;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address);
        if (user is null || user.Status != UserStatus.Active || user.PasswordHash is null) return accepted;

        var token = recovery.IssueResetToken(user.Id, issuedById: null);
        audit.RecordAs(user.Id, "user.recovery_email_requested", "user", user.Id, new { Ip = ClientIp(http) });
        await db.SaveChangesAsync();

        var link = $"{Infrastructure.Email.SiteUrl.Resolve(s, config)}/reset?token={token}";
        await email.SendAsync(new Infrastructure.Email.EmailMessage(
            user.Email,
            $"[{s.InstanceName}] Reset your password",
            $"Someone, probably you, asked to reset the password for {user.Email} on {s.InstanceName}.\n\n" +
            $"Choose a new password here (the link works once and expires in one hour):\n{link}\n\n" +
            "If you did not ask for this, ignore this message; your password has not changed."));

        return accepted;
    }

    private static async Task<IResult> RecoverWithCode(
        RecoverWithCodeRequest req, AppDbContext db, IPasswordHasher hasher,
        IAuditLogger audit, IAccountRecoveryService recovery, RecoveryAttemptLimiter limiter,
        HttpContext http)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();

        if (!limiter.TryAttempt(email))
            return Results.Problem(
                "Too many attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);

        if ((req.NewPassword ?? "").Length < MinPasswordLength)
            return Results.ValidationProblem(Error("newPassword",
                $"Password must be at least {MinPasswordLength} characters."));

        var user = await recovery.RedeemCodeAsync(email, req.Code ?? "");
        if (user is null)
        {
            // Deliberately identical to "wrong code": distinguishing them would
            // turn this endpoint into an account-existence oracle.
            audit.RecordAs(null, "user.recovery_failed", "instance", null, new { Ip = ClientIp(http) });
            await db.SaveChangesAsync();
            return Results.ValidationProblem(Error("code", "That code is not valid."));
        }

        await ApplyRecoveredPasswordAsync(db, hasher, audit, user, req.NewPassword!, "code");
        limiter.Reset(email);
        return Results.NoContent();
    }

    /// <summary>Spends an administrator-issued reset link. Same reasoning as above.</summary>
    private static async Task<IResult> ResetWithToken(
        ResetWithTokenRequest req, AppDbContext db, IPasswordHasher hasher,
        IAuditLogger audit, IAccountRecoveryService recovery, RecoveryAttemptLimiter limiter,
        HttpContext http)
    {
        var token = (req.Token ?? "").Trim();

        if (!limiter.TryAttempt($"token:{token}"))
            return Results.Problem(
                "Too many attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);

        if ((req.NewPassword ?? "").Length < MinPasswordLength)
            return Results.ValidationProblem(Error("newPassword",
                $"Password must be at least {MinPasswordLength} characters."));

        var user = await recovery.RedeemResetTokenAsync(token);
        if (user is null)
        {
            audit.RecordAs(null, "user.recovery_failed", "instance", null, new { Ip = ClientIp(http) });
            await db.SaveChangesAsync();
            return Results.ValidationProblem(Error("token", "That reset link is not valid or has expired."));
        }

        await ApplyRecoveredPasswordAsync(db, hasher, audit, user, req.NewPassword!, "token");
        return Results.NoContent();
    }

    /// <summary>
    /// The half both recovery paths share: set the password and rotate the
    /// security stamp, which signs out every session the person who locked
    /// them out might still be holding. That is the point of a recovery, so it
    /// must not be forgotten in either path.
    /// </summary>
    private static async Task ApplyRecoveredPasswordAsync(
        AppDbContext db, IPasswordHasher hasher, IAuditLogger audit,
        User user, string newPassword, string method)
    {
        user.PasswordHash = hasher.Hash(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        // Proving ownership through recovery ends any lockout: the person the
        // lock was protecting is the one standing here.
        AuthLockout.Reset(user);
        audit.RecordAs(user.Id, "user.password_recovered", "user", user.Id, new { Method = method });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The caller's address as the server currently sees it.
    ///
    /// Behind Caddy this is the proxy's own address, not the client's, until
    /// forwarded-header handling lands (dev-plan 3.0): recorded anyway so the
    /// history exists, and so it becomes correct the moment that ships. Do not
    /// build per-IP logic on this value before then.
    /// </summary>
    private static string? ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString();

    /// <summary>The moment this session authenticated, or null if it cannot be read.</summary>
    private static DateTimeOffset? AuthTimeOf(HttpContext http) => AuthTimeOf(http.User);

    public static DateTimeOffset? AuthTimeOf(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(AuthTimeClaim);
        return long.TryParse(raw, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>Whether this session authenticated recently enough to skip a password prompt.</summary>
    private static bool IsFreshlyAuthenticated(HttpContext http, TimeSpan window) =>
        AuthTimeOf(http) is { } at && DateTimeOffset.UtcNow - at <= window;

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 320
        && email.Contains('@')
        && email.IndexOf('@') > 0
        && email.IndexOf('@') < email.Length - 1;

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
