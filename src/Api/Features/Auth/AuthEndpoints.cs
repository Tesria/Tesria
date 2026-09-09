using System.Data;
using System.Security.Claims;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
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

    /// <summary>Shared by registration and password change, so the two cannot drift apart.</summary>
    public const int MinPasswordLength = 8;

    public record RegisterRequest(string Email, string DisplayName, string Password);
    public record LoginRequest(string Email, string Password);
    /// <summary>
    /// <paramref name="HasPassword"/> is false for accounts provisioned through
    /// OIDC, whose identity provider owns their email and password. The SPA
    /// renders those fields read-only rather than letting a submit fail.
    /// </summary>
    public record UserResponse(
        Guid Id, string Email, string DisplayName, UserRole Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword);
    public record OidcStatusResponse(bool Enabled, string DisplayName);
    public record UpdateProfileRequest(string DisplayName);
    public record ChangeEmailRequest(string CurrentPassword, string Email);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record RecoverWithCodeRequest(string Email, string Code, string NewPassword);
    public record ResetWithTokenRequest(string Token, string NewPassword);
    public record RegenerateCodesRequest(string CurrentPassword);
    /// <summary>The plaintext codes. Returned once, at the only moment they exist.</summary>
    public record RecoveryCodesResponse(IReadOnlyList<string> Codes);
    public record RecoveryStatusResponse(int Remaining);
    /// <summary>
    /// Registration's response: the user, plus the recovery codes at the only
    /// moment they exist in plaintext.
    ///
    /// Flattened rather than nesting a <see cref="UserResponse"/>, so this
    /// stays a superset of what registration returned before — every existing
    /// caller reads the same field names and keeps working.
    /// </summary>
    public record RegisteredResponse(
        Guid Id, string Email, string DisplayName, UserRole Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword,
        IReadOnlyList<string> RecoveryCodes);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        // SignOut result clears the auth cookie; declared without an HttpContext
        // parameter so it binds as a route handler (avoids ASP0016).
        group.MapPost("/logout", () => Results.SignOut(
            new AuthenticationProperties(),
            [CookieAuthenticationDefaults.AuthenticationScheme])).RequireAuthorization();
        group.MapGet("/me", Me);
        group.MapPut("/me", UpdateProfile).RequireAuthorization();
        group.MapPut("/me/email", ChangeEmail).RequireAuthorization();
        group.MapPut("/me/password", ChangePassword).RequireAuthorization();
        group.MapGet("/me/recovery-codes", RecoveryStatus).RequireAuthorization();
        group.MapPost("/me/recovery-codes", RegenerateCodes).RequireAuthorization();
        group.MapPost("/recover/code", RecoverWithCode);
        group.MapPost("/recover/token", ResetWithToken);

        group.MapGet("/oidc/status", OidcStatus);
        // A full-page browser redirect, not a fetch call — the IdP needs to
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
        ISiteSettingsService settings, IAccountRecoveryService recovery)
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

        // The first account on an empty instance administers it — otherwise a
        // fresh install has content and nobody able to manage it.
        var isFirstAccount = !await db.Users.AnyAsync();

        // Closed registration is deliberately ignored for that very first
        // account: otherwise an operator who turns it off before anyone has
        // signed up can never set the instance up at all. Every later account
        // needs it on (dev-plan 1.4 adds invite links as the other way in).
        if (!isFirstAccount && !(await settings.GetAsync()).AllowPublicRegistration)
            return Results.Problem(
                "Registration is by invitation on this instance.",
                statusCode: StatusCodes.Status403Forbidden);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            PasswordHash = hasher.Hash(req.Password!),
            Status = UserStatus.Active,
            Role = isFirstAccount ? UserRole.Admin : UserRole.Member,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);

        // Issued here rather than on demand: codes are only useful if they
        // exist before they are needed, and a prompt later is a prompt most
        // people dismiss.
        var codes = recovery.IssueCodes(user.Id);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await SignIn(http, user);
        return Results.Ok(new RegisteredResponse(
            user.Id, user.Email, user.DisplayName, user.Role,
            user.AvatarHash, user.AvatarVariant, user.PasswordHash != null, codes));
    }

    private static async Task<IResult> Login(
        LoginRequest req, AppDbContext db, IPasswordHasher hasher, HttpContext http,
        IAuditLogger audit)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Verify even when the user is missing to keep timing uniform, and return
        // the same generic error for "no such user" and "wrong password".
        var ok = user?.PasswordHash is not null
            && hasher.Verify(req.Password ?? "", user.PasswordHash);
        if (!ok || user!.Status != UserStatus.Active)
        {
            // Deliberately records no email and no user id: an audit log readable
            // by every admin should not become a list of addresses somebody tried,
            // and a failure cannot be attributed to an account without confirming
            // that the account exists. The per-account counter that brute-force
            // protection needs is dev-plan 3.2's job, not this log's.
            audit.RecordAs(null, "user.login_failed", "instance", null,
                new { Ip = ClientIp(http) });
            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }

        audit.RecordAs(user.Id, "user.login", "user", user.Id, new { Ip = ClientIp(http) });
        await db.SaveChangesAsync();

        await SignIn(http, user);
        return Results.Ok(new UserResponse(user.Id, user.Email, user.DisplayName, user.Role, user.AvatarHash, user.AvatarVariant, user.PasswordHash != null));
    }

    private static async Task<IResult> Me(AppDbContext db, CurrentUser current)
    {
        if (current.Id is not { } id) return Results.Unauthorized();
        var user = await db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserResponse(u.Id, u.Email, u.DisplayName, u.Role, u.AvatarHash, u.AvatarVariant, u.PasswordHash != null))
            .FirstOrDefaultAsync();
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static Task SignIn(HttpContext http, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            // Compared against the stored stamp on every request, so rotating
            // it revokes every existing cookie for this account immediately.
            new(SecurityStampClaim, user.SecurityStamp),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest req, AppDbContext db, CurrentUser current, HttpContext http)
    {
        var displayName = (req.DisplayName ?? "").Trim();
        if (displayName.Length == 0)
            return Results.ValidationProblem(Error("displayName", "Display name is required."));
        if (displayName.Length > 200)
            return Results.ValidationProblem(Error("displayName", "Display name is too long."));

        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        user.DisplayName = displayName;
        await db.SaveChangesAsync();

        // The name is carried in the cookie's claims, so re-issue it — otherwise
        // the topbar would keep showing the old name until the next sign-in.
        await SignIn(http, user);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> ChangeEmail(
        ChangeEmailRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current,
        IAuditLogger audit, HttpContext http)
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
        // The address is an identity, so the change is worth a record — the old
        // value included, since "who used to be this address" is the question
        // an operator will actually be asking.
        audit.Record("user.email_changed", "user", user.Id, new { From = previous, To = email });
        await db.SaveChangesAsync();

        await SignIn(http, user);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest req, AppDbContext db, IPasswordHasher hasher, CurrentUser current,
        IAuditLogger audit, HttpContext http)
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
        await SignIn(http, user);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> RecoveryStatus(
        AppDbContext db, CurrentUser current, IAccountRecoveryService recovery)
    {
        var remaining = await recovery.RemainingCodesAsync(current.RequireId());
        return Results.Ok(new RecoveryStatusResponse(remaining));
    }

    private static async Task<IResult> RegenerateCodes(
        RegenerateCodesRequest req, AppDbContext db, IPasswordHasher hasher,
        CurrentUser current, IAuditLogger audit, IAccountRecoveryService recovery)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (user.PasswordHash is null)
            return Results.ValidationProblem(Error("currentPassword",
                "This account signs in through your identity provider and does not use recovery codes."));

        if (!hasher.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return Results.ValidationProblem(Error("currentPassword", "Current password is incorrect."));

        var codes = recovery.IssueCodes(user.Id);
        audit.Record("user.recovery_codes_regenerated", "user", user.Id);
        await db.SaveChangesAsync();

        return Results.Ok(new RecoveryCodesResponse(codes));
    }

    /// <summary>
    /// Spends a recovery code to set a new password.
    ///
    /// Anonymous by necessity — the whole point is that the caller cannot sign
    /// in. Every failure returns the same 400 whatever went wrong, so this
    /// cannot be used to discover which addresses have accounts.
    /// </summary>
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
        audit.RecordAs(user.Id, "user.password_recovered", "user", user.Id, new { Method = method });
        await db.SaveChangesAsync();
    }

    private static UserResponse ToResponse(User user) =>
        new(user.Id, user.Email, user.DisplayName, user.Role, user.AvatarHash, user.AvatarVariant,
            user.PasswordHash != null);

    /// <summary>
    /// The caller's address as the server currently sees it.
    ///
    /// Behind Caddy this is the proxy's own address, not the client's, until
    /// forwarded-header handling lands (dev-plan 3.0) — recorded anyway so the
    /// history exists, and so it becomes correct the moment that ships. Do not
    /// build per-IP logic on this value before then.
    /// </summary>
    private static string? ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString();

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 320
        && email.Contains('@')
        && email.IndexOf('@') > 0
        && email.IndexOf('@') < email.Length - 1;

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
