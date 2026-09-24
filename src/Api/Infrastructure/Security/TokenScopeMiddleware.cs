using Tesria.Api.Infrastructure.Auth;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Refuses state-changing REST calls from a read-only API token (dev-plan
/// 8.4). One place, by HTTP method, rather than a check in every endpoint:
/// a scope enforced per endpoint is a scope that is forgotten on the next
/// endpoint.
///
/// <c>/mcp</c> is excluded because its transport is all POST; the MCP write
/// tools check the same claim themselves (<c>McpAccess</c>).
/// </summary>
public sealed class TokenScopeMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> Unsafe = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    public Task InvokeAsync(HttpContext context)
    {
        if (Unsafe.Contains(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api")
            && TokenScope.IsReadOnly(context.User))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new
            {
                code = "read_only_token",
                message = "This API token is read-only. Mint one with write access at Profile → API tokens.",
            });
        }
        return next(context);
    }
}

/// <summary>
/// Keeps API tokens away from the account itself (dev-plan 14.1): a token
/// may say who it belongs to (<c>GET /api/auth/me</c>) and nothing else
/// under <c>/api/auth</c> or <c>/api/api-tokens</c>. Before this, a token could
/// mint more tokens, revoke every session including the owner's, and, by
/// saving the profile, be handed a fresh browser session that passed every
/// "confirm your password" check (found in the 14.1 review). Managing
/// tokens, sessions, passwords, two-factor and the profile needs a person
/// signed in to a browser.
/// </summary>
public sealed class TokenAccountGuardMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var byToken = context.User.Identity is { IsAuthenticated: true, AuthenticationType: Auth.ApiTokenAuthenticationDefaults.AuthenticationScheme };
        if (byToken
            && (path.StartsWithSegments("/api/api-tokens")
                || (path.StartsWithSegments("/api/auth")
                    && !(HttpMethods.IsGet(context.Request.Method) && path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase)))))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new
            {
                code = "token_not_allowed",
                message = "API tokens cannot manage tokens, sessions, passwords, two-factor or the profile. Sign in in a browser to do this.",
            });
        }
        return next(context);
    }
}
