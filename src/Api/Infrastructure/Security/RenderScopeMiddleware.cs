using Microsoft.EntityFrameworkCore;
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
/// 12.1, tightened in 14.1).
///
/// The token already grants only what its user could read anyway, and only
/// for fifteen minutes. This narrows it further: a token minted to photograph
/// one page, or one space, reads that and nothing else, so one that escapes
/// into a log is worth almost nothing.
///
/// Until 14.1 the list below was a list of things to let through, and
/// everything not on it was let through as well; a space-wide token was not
/// held to its space at all, and <c>/mcp</c> was open to it. Now it is the
/// whole list, taken from what a full site export actually requested
/// (2026-09-24), and everything else is refused:
/// <list type="bullet">
/// <item>who is asking and the instance (<c>GET /api/auth/me</c>, <c>/api/instance</c>), avatars and branding (<c>/api/media</c>), and embed previews (<c>/api/embeds</c>);</item>
/// <item>the space, by id or key, when it is the token's space;</item>
/// <item>a page, and everything under it (its attachments list and live blocks), when the page is in scope;</item>
/// <item>an attachment, when the page it belongs to is in scope;</item>
/// <item>the capture route itself, <c>/export/pages/{id}</c>, for a page in scope, and the app's own files.</item>
/// </list>
/// A page-scoped token's space is its page's space, so a page's capture can
/// load the space around it; its page routes stay held to its one page.
/// </summary>
public sealed class RenderScopeMiddleware(RequestDelegate next)
{
    private static readonly string[] Shared = ["/api/instance", "/api/media", "/api/embeds"];

    public async Task InvokeAsync(HttpContext context)
    {
        var scope = RenderScope.Of(context);
        if (scope is null) { await next(context); return; }

        var path = context.Request.Path;
        if (await AllowedAsync(context, scope, path)) { await next(context); return; }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "render_scope",
            message = "This render token is for a different page or space.",
        });
    }

    private static async Task<bool> AllowedAsync(HttpContext context, RenderTokenClaims scope, PathString path)
    {
        // The MCP server is for assistants with API tokens, never a capture.
        if (path.StartsWithSegments("/mcp")) return false;

        if (path.StartsWithSegments("/export/pages", out var exportRest))
            return Guid.TryParse(FirstSegment(exportRest), out var exportId) && await PageInScopeAsync(context, scope, exportId);

        // The app's own files (scripts, styles, fonts) and the SPA shell.
        if (!path.StartsWithSegments("/api")) return true;

        if (path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase)) return true;
        if (Shared.Any(p => path.StartsWithSegments(p))) return true;

        if (path.StartsWithSegments("/api/spaces", out var spaceRest))
        {
            var idOrKey = FirstSegment(spaceRest);
            return idOrKey is not null && await SpaceInScopeAsync(context, scope, idOrKey);
        }
        if (path.StartsWithSegments("/api/pages", out var pageRest))
            return Guid.TryParse(FirstSegment(pageRest), out var pageId) && await PageInScopeAsync(context, scope, pageId);
        if (path.StartsWithSegments("/api/attachments", out var attachmentRest))
        {
            if (!Guid.TryParse(FirstSegment(attachmentRest), out var attachmentId)) return false;
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var pageId = await db.Attachments.AsNoTracking().Where(a => a.Id == attachmentId)
                .Select(a => (Guid?)a.PageId).FirstOrDefaultAsync(context.RequestAborted);
            // An unknown id goes on to the endpoint, which answers 404 as usual.
            return pageId is null || await PageInSpaceOfScopeAsync(context, scope, pageId.Value);
        }
        return false;
    }

    private static string? FirstSegment(PathString rest) =>
        rest.Value?.Trim('/').Split('/').FirstOrDefault() is { Length: > 0 } s ? s : null;

    /// <summary>The space a token is held to: its own, or its page's.</summary>
    private static async Task<Guid?> ScopeSpaceAsync(HttpContext context, RenderTokenClaims scope)
    {
        if (scope.Scope == RenderTokens.SpaceScope) return scope.ScopeId;
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        return await db.Pages.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == scope.ScopeId)
            .Select(p => (Guid?)p.SpaceId).FirstOrDefaultAsync(context.RequestAborted);
    }

    private static async Task<bool> PageInScopeAsync(HttpContext context, RenderTokenClaims scope, Guid pageId)
    {
        if (scope.Scope == RenderTokens.PageScope) return scope.ScopeId == pageId;
        return await PageInSpaceOfScopeAsync(context, scope, pageId);
    }

    private static async Task<bool> PageInSpaceOfScopeAsync(HttpContext context, RenderTokenClaims scope, Guid pageId)
    {
        var space = await ScopeSpaceAsync(context, scope);
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var pageSpace = await db.Pages.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == pageId)
            .Select(p => (Guid?)p.SpaceId).FirstOrDefaultAsync(context.RequestAborted);
        // An unknown page goes on to the endpoint, which answers 404 as usual.
        return pageSpace is null || pageSpace == space;
    }

    private static async Task<bool> SpaceInScopeAsync(HttpContext context, RenderTokenClaims scope, string idOrKey)
    {
        var space = await ScopeSpaceAsync(context, scope);
        if (Guid.TryParse(idOrKey, out var id)) return id == space;
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var keyed = await db.Spaces.AsNoTracking().Where(s => s.Key == idOrKey.ToUpperInvariant())
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(context.RequestAborted);
        return keyed is null || keyed == space;
    }
}
