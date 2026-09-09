namespace Tesria.Api.Domain;

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

    /// <summary>
    /// Instance-level role. The first account created on an empty instance is
    /// <see cref="UserRole.Admin"/>; everyone after is a member. An enum rather
    /// than a bool so a future Viewer/Moderator is a new value, not a migration.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Member;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Roughly when this account last made an authenticated request. Written at
    /// most once every few minutes (see LastSeenTracker), so it is accurate to
    /// that interval, not to the second — enough for "active in the last 7
    /// days" and deliberately not a per-request write. Null until the user's
    /// first request after this column existed.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}

public enum UserStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>
/// Instance-level role. Deliberately NOT a permission bypass: an admin sees
/// exactly what their space grants allow, like anyone else. What the role
/// confers is access to instance operations (<c>/api/admin/*</c>) and the
/// audited recover-access action, which writes an explicit space-admin grant.
/// See docs/architecture.md, "Roles and administrators".
/// </summary>
public enum UserRole
{
    Member = 0,
    Admin = 1,
}
