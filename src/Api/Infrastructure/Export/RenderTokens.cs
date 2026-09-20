using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tesria.Api.Infrastructure.Export;

/// <summary>
/// The credential the PDF sidecar's browser uses to load a page it is about
/// to capture (dev-plan 12.1).
///
/// An export is now a photograph of the real page, so the browser taking it
/// has to be able to fetch that page as the person who asked for the export.
/// It has no session, and giving it one would mean a cookie leaving the
/// instance. Instead the export endpoint mints a token bound to exactly the
/// work it is for.
///
/// What keeps this small:
///
/// * **No database row.** It is an HMAC over its own claims, verified by
///   recomputation, so nothing is stored and nothing needs cleaning up. It
///   also does not count against the twenty-an-hour API token mint limit,
///   which a fifty-page site export would exhaust on its own.
/// * **Read only, and scoped.** A token says "this user, this page" or "this
///   user, this space", for five minutes. It cannot write, and it cannot be
///   pointed at a different page.
/// * **It grants nothing new.** The principal it yields is the user who asked
///   for the export, or the anonymous principal for an anonymous export, so
///   every permission check downstream is the one that would have run for
///   that reader anyway. Masking is inherited rather than reimplemented.
///
/// Signed with <c>Pdf:SharedSecret</c>, which the app and the sidecar already
/// share, so there is no new secret to configure or rotate.
/// </summary>
public interface IRenderTokens
{
    bool IsConfigured { get; }

    /// <summary>A token for one page, as this user (null for an anonymous export).</summary>
    string IssueForPage(Guid pageId, Guid? userId);

    /// <summary>A token for every page in one space, as this user (null for anonymous).</summary>
    string IssueForSpace(Guid spaceId, Guid? userId);

    /// <summary>The claims, or null when the token is malformed, unsigned, or expired.</summary>
    RenderTokenClaims? Verify(string token);
}

/// <param name="UserId">Null means the anonymous principal, deliberately.</param>
/// <param name="Scope">"page" or "space".</param>
public record RenderTokenClaims(Guid? UserId, string Scope, Guid ScopeId)
{
    /// <summary>Whether this token may be used to render the given page.</summary>
    public bool CoversPage(Guid pageId, Guid spaceId) =>
        Scope == RenderTokens.PageScope ? ScopeId == pageId : ScopeId == spaceId;
}

public sealed class RenderTokens(IConfiguration config) : IRenderTokens
{
    public const string Prefix = "trx_";
    public const string PageScope = "page";
    public const string SpaceScope = "space";

    /// <summary>
    /// Long enough for a site export of a few hundred pages, short enough
    /// that a token found in a log is worthless by the time it is read.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private string? Secret => config["Pdf:SharedSecret"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Secret);

    public string IssueForPage(Guid pageId, Guid? userId) => Issue(PageScope, pageId, userId);
    public string IssueForSpace(Guid spaceId, Guid? userId) => Issue(SpaceScope, spaceId, userId);

    private string Issue(string scope, Guid scopeId, Guid? userId)
    {
        if (Secret is not { } secret || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Pdf:SharedSecret is not configured.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(
            userId?.ToString(), scope, scopeId.ToString(),
            DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds()));

        var encoded = Base64Url(payload);
        var signature = Base64Url(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(encoded)));
        return $"{Prefix}{encoded}.{signature}";
    }

    public RenderTokenClaims? Verify(string token)
    {
        if (Secret is not { } secret || string.IsNullOrWhiteSpace(secret)) return null;
        if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        var parts = token[Prefix.Length..].Split('.');
        if (parts.Length != 2) return null;

        var expected = Base64Url(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(parts[0])));
        // Fixed-time: the comparison is against a signature an attacker
        // controls, which is exactly where a timing leak would be usable.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
            return null;

        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(FromBase64Url(parts[0])); }
        catch (JsonException) { return null; }
        if (payload is null) return null;

        if (DateTimeOffset.FromUnixTimeSeconds(payload.Exp) < DateTimeOffset.UtcNow) return null;
        if (payload.Scope is not (PageScope or SpaceScope)) return null;
        if (!Guid.TryParse(payload.ScopeId, out var scopeId)) return null;

        Guid? userId = null;
        if (payload.UserId is not null)
        {
            if (!Guid.TryParse(payload.UserId, out var parsed)) return null;
            userId = parsed;
        }
        return new RenderTokenClaims(userId, payload.Scope, scopeId);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    private record Payload(
        [property: JsonPropertyName("sub")] string? UserId,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("id")] string ScopeId,
        [property: JsonPropertyName("exp")] long Exp);
}
