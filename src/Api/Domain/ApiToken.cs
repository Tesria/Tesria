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

    /// <summary>
    /// How many requests it has made, counted from 0.6 (tokens from before
    /// start at zero), and the address the last one came from: what lets an
    /// administrator see who uses tokens and spot one being used from
    /// somewhere unexpected (the owner, 2026-09-24). Written in the same save
    /// as <see cref="LastUsedAt"/>, so counting costs nothing extra.
    /// </summary>
    public long UseCount { get; set; }

    public string? LastUsedFrom { get; set; }

    /// <summary>
    /// When the token stops working (dev-plan 14.1); null never expires, which
    /// is a choice made when the token is created. Tokens from before expiry
    /// existed were given 90 days from the upgrade, not "never".
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When its owner was told it expires within a week, so they are told once.</summary>
    public DateTimeOffset? ExpiryWarnedAt { get; set; }
}
