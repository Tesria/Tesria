using Tesria.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>Named authorization policies, so route registrations don't repeat string literals.</summary>
public static class AuthPolicies
{
    /// <summary>Requires the instance <see cref="Domain.UserRole.Admin"/> role.</summary>
    public const string RequireAdmin = "RequireAdmin";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Satisfies <see cref="AdminRequirement"/> from the database via
/// <see cref="CurrentUser.IsAdminAsync"/> rather than from a claim — see that
/// method for why the role is not carried in the cookie.
///
/// When <c>RequireTotpForAdmins</c> is on (dev-plan 3.5), an administrator
/// who has not enrolled is refused here, on every admin route at once: the
/// role is not usable until two-factor is set up. The enrolment endpoints
/// live under /auth/me, outside this policy, so the way out is always open.
///
/// Failing simply doesn't call <c>Succeed</c>: the cookie handler's
/// <c>OnRedirectToAccessDenied</c> already turns that into a 403 rather than a
/// redirect, which is what an API caller expects.
/// </summary>
public sealed class AdminRequirementHandler(CurrentUser current, ISiteSettingsService settings, AppDbContext db)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (!await current.IsAdminAsync()) return;

        if ((await settings.GetAsync()).RequireTotpForAdmins)
        {
            var enrolled = await db.Users.AsNoTracking()
                .Where(u => u.Id == current.Id)
                .Select(u => u.TotpEnabledAt != null)
                .FirstOrDefaultAsync();
            if (!enrolled) return;
        }

        context.Succeed(requirement);
    }
}
