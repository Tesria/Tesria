using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Security;

namespace Tesria.Api.Features.Auth;

/// <summary>
/// What single sign-on shares with a password sign-in once the provider has
/// said who someone is (t2-020, the owner's choice of 2026-09-29).
///
/// An account with Tesria two-factor on is not signed in yet: it gets the
/// same challenge a correct password gets, and the same One More Step page,
/// where a recovery code works too. SSO used to sign such an account straight
/// in, so turning on two-factor protected only the password.
/// </summary>
public static partial class AuthEndpoints
{
    /// <summary>
    /// Carries a single sign-on's two-factor challenge to the code step, so
    /// it is never in the address bar. Only the sign-in endpoints read it.
    /// </summary>
    public const string SsoChallengeCookie = "tesria.sso2fa";

    private static CookieOptions SsoChallengeCookieOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/api/auth",
        IsEssential = true,
    };

    /// <summary>
    /// Called by the OpenID Connect handler with the account the provider
    /// resolved to. Returns where to send the browser for the second step,
    /// or null to sign in now (which this records as a sign-in, as a
    /// password sign-in is).
    /// </summary>
    public static async Task<string?> AfterSingleSignOnAsync(HttpContext http, User user, string? returnUrl)
    {
        var services = http.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();

        if (user.TotpEnabledAt is not null)
        {
            var challenge = services.GetRequiredService<ITotpService>().IssueChallenge(user, ClientIp(http));
            await db.SaveChangesAsync();
            var options = SsoChallengeCookieOptions(http);
            options.MaxAge = TotpService.ChallengeLifetime;
            http.Response.Cookies.Append(SsoChallengeCookie, challenge, options);
            var target = IsLocalPath(returnUrl) ? returnUrl! : "/spaces";
            return "/login?sso=code&returnUrl=" + Uri.EscapeDataString(target);
        }

        await RecordSignInAsync(http, services.GetRequiredService<IAuditLogger>(),
            services.GetRequiredService<ISecurityDetector>(), user, totp: false, sso: true);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>
    /// The record every completed sign-in leaves, whichever way it came:
    /// the audit row, and the new-address check for administrators. Before
    /// the row is written, so the history the check reads is the history
    /// before this sign-in. The caller saves.
    /// </summary>
    private static async Task RecordSignInAsync(
        HttpContext http, IAuditLogger audit, ISecurityDetector detector, User user, bool totp, bool sso)
    {
        await detector.SucceededLoginAsync(user, ClientIp(http));
        object metadata = sso
            ? new { Ip = ClientIp(http), Totp = totp, Sso = true }
            : new { Ip = ClientIp(http), Totp = totp };
        audit.RecordAs(user.Id, "user.login", "user", user.Id, metadata);
    }
}
