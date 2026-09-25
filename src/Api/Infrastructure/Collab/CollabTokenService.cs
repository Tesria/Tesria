using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tesria.Api.Infrastructure.Collab;

/// <summary>
/// Issues short-lived, HMAC-signed tokens that authorize a user to join the
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
    string Issue(Guid pageId, Guid userId, string displayName, CollabSubject subject);
    bool IsConfigured { get; }
}

/// <summary>
/// What the token was issued under, so the app can be asked again later
/// whether it still stands (the review's SEC-02, 2026-09-24): a browser
/// session (<c>s</c>) or an API token (<c>t</c>), by id, and a short hash of
/// the account's security stamp, which a password change, a suspension or
/// "sign out everywhere" replaces. A hash, not the stamp: the token's payload
/// is readable by whoever holds it.
/// </summary>
public sealed record CollabSubject(string Kind, Guid Id, string StampHash)
{
    public const string Session = "s";
    public const string ApiToken = "t";

    public static string HashStamp(string stamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..16].ToLowerInvariant();
}

public sealed class CollabTokenService(IConfiguration config) : ICollabTokenService
{
    /// <summary>
    /// Ten minutes (dev-plan 14.3; it was thirty): the sidecar ends a
    /// connection when its token expires, and the editor reconnects with a
    /// fresh one, so this is how long someone who lost access could stay
    /// connected if the app could not reach the sidecar to say so.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private string? Secret => config["Collab:Secret"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Secret);

    public string Issue(Guid pageId, Guid userId, string displayName, CollabSubject subject)
    {
        if (Secret is not { } secret || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Collab:Secret is not configured.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new CollabTokenPayload(
            pageId.ToString(),
            userId.ToString(),
            displayName,
            DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds(),
            subject.Kind,
            subject.Id.ToString(),
            subject.StampHash));

        var encodedPayload = Base64Url(payload);
        var signature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(encodedPayload)));

        return $"{encodedPayload}.{signature}";
    }

    // Base64url (RFC 4648 §5): URL- and header-safe, no padding.
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private record CollabTokenPayload(
        string pageId, string userId, string displayName, long exp, string sk, string sid, string st);
}
