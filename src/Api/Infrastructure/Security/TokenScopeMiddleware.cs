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
