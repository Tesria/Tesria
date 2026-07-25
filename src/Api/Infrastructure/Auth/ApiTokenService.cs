using System.Security.Cryptography;
using System.Text;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Infrastructure.Auth;

/// <summary>
/// Issues and validates personal access tokens. A raw token is
/// <c>{tokenId}.{secret}</c>: the id gives an O(1) lookup, and the secret (32
/// random bytes, base64url) is never stored — only its hash, verified in
/// constant time, mirroring password handling.
/// </summary>
public interface IApiTokenService
{
    Task<(string RawToken, ApiToken Entity)> IssueAsync(Guid userId, string name);
    Task<ApiToken?> ValidateAsync(string rawToken);
}

public sealed class ApiTokenService(AppDbContext db) : IApiTokenService
{
    private const string Prefix = "cct_"; // "ConfluenceClone token"

    public async Task<(string RawToken, ApiToken Entity)> IssueAsync(Guid userId, string name)
    {
        var id = Guid.NewGuid();
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var rawToken = $"{Prefix}{id:N}.{secret}";

        var entity = new ApiToken
        {
            Id = id,
            UserId = userId,
            Name = name,
            TokenHash = Hash(rawToken),
            Prefix = rawToken[..(Prefix.Length + 8)] + "…",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ApiTokens.Add(entity);
        await db.SaveChangesAsync();

        return (rawToken, entity);
    }

    public async Task<ApiToken?> ValidateAsync(string rawToken)
    {
        if (!rawToken.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var withoutPrefix = rawToken[Prefix.Length..];
        var dot = withoutPrefix.IndexOf('.');
        if (dot < 0 || !Guid.TryParseExact(withoutPrefix[..dot], "N", out var id)) return null;

        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id);
        if (token is null) return null;

        // Constant-time compare against the stored hash of the full raw token.
        var expected = Convert.FromHexString(token.TokenHash);
        var actual = Convert.FromHexString(Hash(rawToken));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

        token.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return token;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
