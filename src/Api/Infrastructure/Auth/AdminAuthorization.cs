using Microsoft.AspNetCore.Authorization;

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
/// Failing simply doesn't call <c>Succeed</c>: the cookie handler's
/// <c>OnRedirectToAccessDenied</c> already turns that into a 403 rather than a
/// redirect, which is what an API caller expects.
/// </summary>
public sealed class AdminRequirementHandler(CurrentUser current)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (await current.IsAdminAsync()) context.Succeed(requirement);
    }
}
