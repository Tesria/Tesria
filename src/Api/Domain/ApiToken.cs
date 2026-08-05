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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
