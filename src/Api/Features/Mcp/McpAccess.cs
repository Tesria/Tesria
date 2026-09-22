using ModelContextProtocol;
using Tesria.Api.Infrastructure.Auth;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// What a tool asks before acting (architecture.md, "MCP server"). The
/// transport is all POST, so the REST scope middleware cannot see a write
/// tool; each one calls <see cref="RequireWrite"/> instead: first, before
/// anything changes.
/// </summary>
public static class McpAccess
{
    public static void RequireWrite(CurrentUser current, IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user is null || !current.IsAuthenticated)
            throw new McpException("Sign in with an API token to use this tool.");
        if (TokenScope.IsReadOnly(user))
            throw new McpException("This API token is read-only. Mint one with write access at Profile → API tokens.");
    }

    /// <summary>The same words REST uses: a thing you may not see does not exist.</summary>
    public static McpException NotFound(string what) => new($"{what} not found.");
}
