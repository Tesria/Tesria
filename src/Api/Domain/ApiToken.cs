namespace Tesria.Api.Domain;

/// <summary>
/// A personal access token, letting external scripts and integrations call the
/// REST API without a browser cookie session. The raw token is shown to the
/// user exactly once at creation; only its hash is stored, mirroring how
/// passwords are handled.
/// </summary>
public class ApiToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>User-chosen label, e.g. "CI pipeline".</summary>
    public required string Name { get; set; }

    /// <summary>SHA-256 hash of the full raw token (id + secret).</summary>
    public required string TokenHash { get; set; }

    /// <summary>First few characters of the raw token, for identifying it in listings.</summary>
    public required string Prefix { get; set; }

    /// <summary>
    /// The token's one scope (dev-plan 8.4): a read-only token may call
    /// anything that does not change state. Enforced for REST by
    /// <c>TokenScopeMiddleware</c> and for MCP by each write tool. Existing
    /// tokens are full-access: nothing narrows silently on upgrade.
    /// </summary>
    public bool ReadOnly { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
