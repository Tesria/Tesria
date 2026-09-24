using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.ApiTokens;

public static class ApiTokenEndpoints
{
    /// <param name="ReadOnly">Mint a read-only token (dev-plan 8.4). Omitted means full access, as every token was before scopes existed.</param>
    /// <param name="ExpiresInDays">
    /// How long the token lasts (dev-plan 14.1): 1 to 3650 days, or 0 for no
    /// expiry, which has to be asked for. Omitted is the default, 90 days.
    /// </param>
    public record CreateTokenRequest(string Name, bool? ReadOnly, int? ExpiresInDays = null);
    public record CreatedTokenResponse(
        Guid Id, string Name, string Prefix, bool ReadOnly, DateTimeOffset CreatedAt, string Token, DateTimeOffset? ExpiresAt);
    public record TokenResponse(
        Guid Id, string Name, string Prefix, bool ReadOnly, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt,
        DateTimeOffset? ExpiresAt, long UseCount = 0, string? LastUsedFrom = null);

    public const int DefaultLifetimeDays = 90;

    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api-tokens").WithTags("ApiTokens").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create).RequireRateLimiting(Infrastructure.Security.RateLimits.TokenMintPolicy)
            .RequirePermission(Infrastructure.Permissions.InstancePermissions.TokensUse);
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
            .Select(t => new TokenResponse(t.Id, t.Name, t.Prefix, t.ReadOnly, t.CreatedAt, t.LastUsedAt, t.ExpiresAt, t.UseCount, t.LastUsedFrom)));
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

        var days = req.ExpiresInDays ?? DefaultLifetimeDays;
        if (days is < 0 or > 3650)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["expiresInDays"] = ["A token lasts from 1 to 3650 days, or 0 for no expiry."],
            });
        DateTimeOffset? expiresAt = days == 0 ? null : DateTimeOffset.UtcNow.AddDays(days);

        var (raw, entity) = await tokens.IssueAsync(current.RequireId(), name, req.ReadOnly ?? false, expiresAt);
        await detector.TokenMintedAsync(current.RequireId());
        await db.SaveChangesAsync();
        // The raw token is returned exactly once: it is not retrievable again.
        return Results.Created($"/api/api-tokens/{entity.Id}",
            new CreatedTokenResponse(entity.Id, entity.Name, entity.Prefix, entity.ReadOnly, entity.CreatedAt, raw, entity.ExpiresAt));
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
