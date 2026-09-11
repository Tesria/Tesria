using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.ApiTokens;

public static class ApiTokenEndpoints
{
    /// <param name="ReadOnly">Mint a read-only token (dev-plan 8.4). Omitted means full access, as every token was before scopes existed.</param>
    public record CreateTokenRequest(string Name, bool? ReadOnly);
    public record CreatedTokenResponse(Guid Id, string Name, string Prefix, bool ReadOnly, DateTimeOffset CreatedAt, string Token);
    public record TokenResponse(
        Guid Id, string Name, string Prefix, bool ReadOnly, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api-tokens").WithTags("ApiTokens").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create).RequireRateLimiting(Infrastructure.Security.RateLimits.TokenMintPolicy);
        group.MapDelete("/{id:guid}", Revoke);
        return routes;
    }

    private static async Task<IResult> List(AppDbContext db, CurrentUser current)
    {
        var userId = current.RequireId();
        var tokens = await db.ApiTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
        // Sorted in memory: the set per user is small, and the SQLite test
        // provider cannot ORDER BY DateTimeOffset.
        return Results.Ok(tokens
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TokenResponse(t.Id, t.Name, t.Prefix, t.ReadOnly, t.CreatedAt, t.LastUsedAt)));
    }

    private static async Task<IResult> Create(
        CreateTokenRequest req, IApiTokenService tokens, CurrentUser current,
        Infrastructure.Security.ISecurityDetector detector, AppDbContext db)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A name is required so you can tell your tokens apart."],
            });

        var (raw, entity) = await tokens.IssueAsync(current.RequireId(), name, req.ReadOnly ?? false);
        await detector.TokenMintedAsync(current.RequireId());
        await db.SaveChangesAsync();
        // The raw token is returned exactly once — it is not retrievable again.
        return Results.Created($"/api/api-tokens/{entity.Id}",
            new CreatedTokenResponse(entity.Id, entity.Name, entity.Prefix, entity.ReadOnly, entity.CreatedAt, raw));
    }

    private static async Task<IResult> Revoke(Guid id, AppDbContext db, CurrentUser current)
    {
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.UserId == current.RequireId());
        if (token is null) return Results.NotFound();
        db.ApiTokens.Remove(token);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
