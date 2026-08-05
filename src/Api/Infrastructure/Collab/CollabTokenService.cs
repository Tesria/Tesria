using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tesria.Api.Infrastructure.Collab;

/// <summary>
/// Issues short-lived, HMAC-signed tokens that authorise a user to join the
/// real-time editing session for one specific page.
/// <para>
/// The collaboration server is a separate Node process and cannot evaluate our
/// permission model, so the API acts as the gatekeeper: it only issues a token
/// after checking the caller may edit that page, and binds the token to that
/// page id. The sidecar merely verifies the signature, the expiry, and that the
/// document being opened matches the token.
/// </para>
/// </summary>
public interface ICollabTokenService
{
    string Issue(Guid pageId, Guid userId, string displayName);
    bool IsConfigured { get; }
}

public sealed class CollabTokenService(IConfiguration config) : ICollabTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private string? Secret => config["Collab:Secret"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Secret);

    public string Issue(Guid pageId, Guid userId, string displayName)
    {
        if (Secret is not { } secret || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Collab:Secret is not configured.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new CollabTokenPayload(
            pageId.ToString(),
            userId.ToString(),
            displayName,
            DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds()));

        var encodedPayload = Base64Url(payload);
        var signature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(encodedPayload)));

        return $"{encodedPayload}.{signature}";
    }

    // Base64url (RFC 4648 §5): URL- and header-safe, no padding.
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private record CollabTokenPayload(string pageId, string userId, string displayName, long exp);
}
