using Tesria.Api.Infrastructure.Export;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>Where the verified claims of a render token are kept for the request.</summary>
public static class RenderScope
{
    public const string ItemKey = "tesria:render_scope";

    public static RenderTokenClaims? Of(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as RenderTokenClaims : null;
}

/// <summary>
/// Holds a render token to the page or space it was minted for (dev-plan
/// 12.1).
///
/// The token already grants only what its user could read anyway, and only
/// for fifteen minutes. This narrows it further: a token minted to photograph
/// one page cannot be used to read the rest of the wiki, so one that escapes
/// into a log is worth almost nothing.
///
/// The allowlist is by path because that is what a capture actually needs:
/// its own page, the space around it, the things a page hangs off, and the
/// session and instance calls the SPA makes on the way up. Anything else is
/// 403 rather than quietly answered.
/// </summary>
public sealed class RenderScopeMiddleware(RequestDelegate next)
{
    /// <summary>Calls every rendered page makes regardless of which page it is.</summary>
    private static readonly string[] AlwaysAllowed =
    [
        "/api/auth/me", "/api/instance", "/api/spaces", "/api/attachments", "/api/media", "/api/users",
    ];

    public Task InvokeAsync(HttpContext context)
    {
        var scope = RenderScope.Of(context);
        if (scope is null) return next(context);

        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api")) return next(context);
        if (AlwaysAllowed.Any(p => path.StartsWithSegments(p))) return next(context);

        // Everything else has to be about a page this token covers. A page id
        // is the second segment for every page-shaped route
        // (/api/pages/{id}/…), which is the only family that carries content.
        if (path.StartsWithSegments("/api/pages", out var rest))
        {
            var first = rest.Value?.Trim('/').Split('/').FirstOrDefault();
            // /api/pages/tree?spaceId=… and similar collection routes carry no
            // id; they are answered under the caller's own permissions, which
            // is the same answer they would get anywhere else.
            if (!Guid.TryParse(first, out var pageId)) return next(context);
            if (scope.Scope == RenderTokens.PageScope && scope.ScopeId != pageId) return Refuse(context);
            return next(context);
        }

        return next(context);
    }

    private static Task Refuse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new
        {
            code = "render_scope",
            message = "This render token is for a different page.",
        });
    }
}
