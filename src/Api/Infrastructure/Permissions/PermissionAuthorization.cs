using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Tesria.Api.Infrastructure.Permissions;

public sealed class PermissionRequirement(string key) : IAuthorizationRequirement
{
    public string Key { get; } = key;
}

/// <summary>
/// Turns <c>perm:&lt;key&gt;</c> into a policy on demand (dev-plan 11.1), so a
/// route can name its right without every key being registered at startup and
/// without a second list to keep in step with the catalog.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var key = policyName[Prefix.Length..];
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(key))
            .Build();
    }
}

/// <summary>
/// Satisfies a <see cref="PermissionRequirement"/> from the caller's role.
///
/// The two-factor rule from dev-plan 3.5 rides along, but only for callers in
/// the administrator tier or above: an unenrolled administrator cannot use
/// administrative rights, while an ordinary user's right to create a space has
/// nothing to do with two-factor.
/// </summary>
public sealed class PermissionRequirementHandler(
    IInstancePermissions permissions, CurrentUser current, ISiteSettingsService settings, AppDbContext db)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!await permissions.HasAsync(requirement.Key)) return;

        if (await current.RoleAsync() >= UserRole.Admin
            && !await AdminRequirementHandler.TwoFactorSatisfied(current, settings, db)) return;

        context.Succeed(requirement);
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Requires one instance right (dev-plan 11.1), e.g. <c>backups.policy</c>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string key)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!InstancePermissions.IsAssignable(key) && !InstancePermissions.IsReserved(key))
            throw new ArgumentException($"'{key}' is not in the permission catalog.", nameof(key));

        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + key);
        // Recorded so a test can walk the endpoints and prove every
        // administrative route names a right the catalog defines.
        builder.WithMetadata(new PermissionRequirement(key));
        return builder;
    }
}
