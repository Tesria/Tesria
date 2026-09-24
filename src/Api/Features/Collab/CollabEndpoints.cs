using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Collab;

public static class CollabEndpoints
{
    public record CollabTokenResponse(string Token, string DocumentName, bool Enabled);

    public static IEndpointRouteBuilder MapCollabEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/pages/{id:guid}/collab-token", IssueToken)
            .WithTags("Collaboration").RequireAuthorization();
        return routes;
    }

    /// <summary>
    /// Issues a token letting the caller join the live editing session for this
    /// page. This is the permission gate for real-time editing: the sidecar
    /// trusts the token, so it is only issued to users who may edit the page.
    /// </summary>
    private static async Task<IResult> IssueToken(
        Guid id, AppDbContext db, IPermissionService perms, CurrentUser current,
        ICollabTokenService tokens, HttpContext http)
    {
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();
        // A GET, so the read-only check that covers writes never sees it; but
        // what it hands out is write access to the live draft. A read-only
        // API token could edit a page through it (found 2026-09-23).
        if (TokenScope.IsReadOnly(http.User))
            return Results.Json(new
            {
                code = "read_only_token",
                message = "This API token is read-only. Mint one with write access at Profile → API tokens.",
            }, statusCode: StatusCodes.Status403Forbidden);

        // Collaboration is optional: without a shared secret the SPA falls back
        // to plain single-user editing rather than failing.
        if (!tokens.IsConfigured)
            return Results.Ok(new CollabTokenResponse("", id.ToString(), Enabled: false));

        var userId = current.RequireId();
        var displayName = await db.Users.Where(u => u.Id == userId)
            .Select(u => u.DisplayName).FirstAsync();

        return Results.Ok(new CollabTokenResponse(
            tokens.Issue(id, userId, displayName), id.ToString(), Enabled: true));
    }
}
