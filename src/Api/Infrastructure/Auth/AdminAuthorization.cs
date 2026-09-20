using Tesria.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>Named authorization policies, so route registrations don't repeat string literals.</summary>
public static class AuthPolicies
{
    /// <summary>Requires <see cref="Domain.UserRole.Admin"/> or above.</summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>Requires <see cref="Domain.UserRole.Owner"/> (dev-plan 10.1).</summary>
    public const string RequireOwner = "RequireOwner";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>The two actions an administrator cannot take: changing roles, and handing over the instance.</summary>
public sealed class OwnerRequirement : IAuthorizationRequirement;

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
        if (!await TwoFactorSatisfied(current, settings, db)) return;

        context.Succeed(requirement);
    }

    /// <summary>
    /// The two-factor rule both administrative policies share: when the
    /// instance requires it, the role is not usable until enrolment. The
    /// enrolment endpoints live under /auth/me, outside both policies, so the
    /// way out is always open.
    /// </summary>
    internal static async Task<bool> TwoFactorSatisfied(
        CurrentUser current, ISiteSettingsService settings, AppDbContext db)
    {
        if (!(await settings.GetAsync()).RequireTotpForAdmins) return true;
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == current.Id)
            .Select(u => u.TotpEnabledAt != null)
            .FirstOrDefaultAsync();
    }
}

/// <summary>
/// Satisfies <see cref="OwnerRequirement"/>, the same way and with the same
/// two-factor rule as <see cref="AdminRequirementHandler"/>. Separate from it
/// so an owner-only route reads as one policy name rather than an admin
/// policy plus a check in the handler.
/// </summary>
public sealed class OwnerRequirementHandler(CurrentUser current, ISiteSettingsService settings, AppDbContext db)
    : AuthorizationHandler<OwnerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OwnerRequirement requirement)
    {
        if (!await current.IsOwnerAsync()) return;
        if (!await AdminRequirementHandler.TwoFactorSatisfied(current, settings, db)) return;

        context.Succeed(requirement);
    }
}
