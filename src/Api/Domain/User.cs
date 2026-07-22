namespace ConfluenceClone.Api.Domain;

/// <summary>
/// An account that can sign in and author content. Local accounts use an
/// Argon2id <see cref="PasswordHash"/>; <see cref="OidcSubject"/> is reserved
/// for a future OIDC/SSO phase (PLAN §1) and is null for local accounts.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Login identity. Stored lower-cased; unique (case-insensitive).</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Argon2id encoded hash. Null only for OIDC-provisioned users.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Subject id from an external identity provider (future SSO).</summary>
    public string? OidcSubject { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }
}

public enum UserStatus
{
    Active = 0,
    Suspended = 1,
}
