using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Tesria.Api.Domain;
using Tesria.Api.Features.Auth;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Collab;

public static class CollabEndpoints
{
    public record CollabTokenResponse(string Token, string DocumentName, bool Enabled);

    /// <summary>One open or opening connection, as the collaboration service verified it from its token.</summary>
    public record Connection(string Key, Guid UserId, Guid PageId, string Sk, Guid Sid, string St);
    public record AuthorizeRequest(List<Connection> Connections);
    public record AuthorizeResponse(Dictionary<string, bool> Allowed);

    public static IEndpointRouteBuilder MapCollabEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/pages/{id:guid}/collab-token", IssueToken)
            .WithTags("Collaboration").RequireAuthorization();
        return routes;
    }

    /// <summary>
    /// The collaboration service asking whether connections may stand
    /// (the review's SEC-02, 2026-09-24). Not under <c>/api</c>: nothing but
    /// the service calls it, Caddy refuses <c>/internal</c> from outside, and
    /// the shared secret is checked here regardless.
    /// </summary>
    public static IEndpointRouteBuilder MapCollabInternalEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/internal/collab/authorize", Authorize).AllowAnonymous().ExcludeFromDescription();
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
        var user = await db.Users.Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.SecurityStamp }).FirstAsync();

        // What the token stands on, so the service can ask again whether it
        // still does. A principal with neither (none today) gets no token.
        CollabSubject? subject =
            AuthEndpoints.SessionIdOf(http.User) is { } sessionId
                ? new(CollabSubject.Session, sessionId, CollabSubject.HashStamp(user.SecurityStamp))
            : Guid.TryParse(http.User.FindFirstValue(TokenUsage.TokenIdClaim), out var tokenId)
                ? new(CollabSubject.ApiToken, tokenId, CollabSubject.HashStamp(user.SecurityStamp))
            : null;
        if (subject is null) return Results.Forbid();

        return Results.Ok(new CollabTokenResponse(
            tokens.Issue(id, userId, user.DisplayName, subject), id.ToString(), Enabled: true));
    }

    /// <summary>
    /// Answers, for each connection, whether it may stand: the account is
    /// active and its security stamp unchanged, the session or API token it
    /// was issued under is still valid (and the token not read-only, and its
    /// owner still allowed tokens), and the account may edit the page now.
    /// The same rules as issuing a token, asked again at every connection and
    /// once a minute for every open one.
    /// </summary>
    private static async Task<IResult> Authorize(
        AuthorizeRequest req, HttpContext http, IConfiguration config, AppDbContext db,
        IPermissionService perms, IInstancePermissions rights)
    {
        if (!SecretMatches(http.Request.Headers["X-Collab-Secret"].ToString(), config["Collab:Secret"]))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var now = DateTimeOffset.UtcNow;
        var userIds = req.Connections.Select(c => c.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.Status, u.SecurityStamp });

        var sessionIds = req.Connections.Where(c => c.Sk == CollabSubject.Session).Select(c => c.Sid).Distinct().ToList();
        var sessions = await db.UserSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id) && s.RevokedAt == null)
            .ToDictionaryAsync(s => s.Id, s => s.UserId);

        var tokenIds = req.Connections.Where(c => c.Sk == CollabSubject.ApiToken).Select(c => c.Sid).Distinct().ToList();
        // Expiry checked here rather than in SQL, as the token validator does:
        // SQLite, under the tests, cannot compare offsets.
        var apiTokens = (await db.ApiTokens.AsNoTracking()
                .Where(t => tokenIds.Contains(t.Id) && !t.ReadOnly)
                .Select(t => new { t.Id, t.UserId, t.ExpiresAt })
                .ToListAsync())
            .Where(t => t.ExpiresAt is null || t.ExpiresAt > now)
            .ToDictionary(t => t.Id, t => t.UserId);

        var answers = new Dictionary<string, bool>();
        // One evaluation per user and page: a page open in two tabs is one question.
        var decided = new Dictionary<(Guid, Guid), bool>();
        foreach (var c in req.Connections)
        {
            var ok = users.TryGetValue(c.UserId, out var user)
                && user.Status == UserStatus.Active
                && string.Equals(CollabSubject.HashStamp(user.SecurityStamp), c.St, StringComparison.Ordinal)
                && c.Sk switch
                {
                    CollabSubject.Session => sessions.TryGetValue(c.Sid, out var owner) && owner == c.UserId,
                    CollabSubject.ApiToken => apiTokens.TryGetValue(c.Sid, out var owner) && owner == c.UserId
                        && (await rights.ForUserAsync(c.UserId)).Contains(InstancePermissions.TokensUse),
                    _ => false,
                };
            if (ok)
            {
                if (!decided.TryGetValue((c.UserId, c.PageId), out var may))
                {
                    may = await perms.AsUser(c.UserId).CanEditPageAsync(c.PageId);
                    decided[(c.UserId, c.PageId)] = may;
                }
                ok = may;
            }
            answers[c.Key] = ok;
        }
        return Results.Ok(new AuthorizeResponse(answers));
    }

    private static bool SecretMatches(string provided, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
