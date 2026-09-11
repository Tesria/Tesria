using System.Security.Claims;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// The one scope an API token has (dev-plan 8.4). Read the principal, never
/// the database: the handler stamped the claim when it validated the token.
/// </summary>
public static class TokenScope
{
    public const string ClaimType = "tesria:token_scope";
    public const string Read = "read";
    public const string Write = "write";

    /// <summary>True only for a read-only API token. A cookie session, or a full-access token, may write.</summary>
    public static bool IsReadOnly(ClaimsPrincipal user) =>
        user.FindFirst(ClaimType)?.Value == Read;
}
