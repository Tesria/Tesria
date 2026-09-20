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

    /// <summary>
    /// The role whose rights this account holds (dev-plan 11.1). Null means
    /// the built-in role of <see cref="Role"/>'s tier, which is what
    /// <c>RoleSeed</c> fills in; every write since sets it explicitly. Its
    /// tier always matches <see cref="Role"/>.
    /// </summary>
    public Guid? RoleId { get; set; }

    public Domain.Role? InstanceRole { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Roughly when this account last made an authenticated request. Written at
    /// most once every few minutes (see LastSeenTracker), so it is accurate to
    /// that interval, not to the second — enough for "active in the last 7
    /// days" and deliberately not a per-request write. Null until the user's
    /// first request after this column existed.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// Storage key of the uploaded avatar, or null to use the generated default
    /// (dev-plan 1.2). Deterministic per user, so a replacement overwrites
    /// rather than leaving an orphan behind.
    /// </summary>
    public string? AvatarKey { get; set; }

    /// <summary>
    /// Content hash of the stored avatar, used as the cache-busting value on
    /// its URL. Stored rather than computed on read so serving an avatar costs
    /// no hashing, and so the URL can be built from data already loaded with
    /// the user.
    /// </summary>
    public string? AvatarHash { get; set; }

    /// <summary>
    /// Which generated avatar this user picked, or null to derive one from
    /// their id. Only consulted when <see cref="AvatarKey"/> is null — an
    /// uploaded image always wins.
    ///
    /// Stored as an index rather than a colour so the generated set can be
    /// restyled later without rewriting every row.
    /// </summary>
    public int? AvatarVariant { get; set; }

    /// <summary>
    /// Rotated whenever every existing session for this account must stop
    /// working: a password change, and later suspension (dev-plan 2.2), an
    /// admin force-logout (3.3) and 2FA enrolment (3.5).
    ///
    /// The value is carried as a claim in the auth cookie and compared against
    /// this column on each request, so a rotation takes effect immediately
    /// rather than at the cookie's next expiry. That is the whole reason a
    /// stateless cookie scheme can still revoke a session.
    ///
    /// Not a secret — it is an opaque version marker, and knowing it grants
    /// nothing without the signed cookie it lives in.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Consecutive failed sign-ins since the last success (dev-plan 3.2).
    /// Persisted, not in memory, so a restart does not hand an attacker a
    /// fresh budget and so administrators can see it.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// Until when sign-in is refused regardless of the password. Always
    /// temporary; the response is the same generic 401 as a wrong password so
    /// the lock itself reveals nothing.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    // --- Two-factor (dev-plan 3.5). Secrets are Data Protection payloads;
    // the plaintext exists only inside TotpService.

    public string? TotpSecretProtected { get; set; }

    /// <summary>A secret shown to the user but not yet confirmed with a code.</summary>
    public string? TotpPendingSecretProtected { get; set; }

    public DateTimeOffset? TotpEnabledAt { get; set; }

    /// <summary>The last time step a code was accepted for, so no code is accepted twice.</summary>
    public long? TotpLastStep { get; set; }

    /// <summary>How this person wants their own notifications by email (dev-plan 4.3). Security alerts to administrators ignore this.</summary>
    public EmailNotificationMode EmailNotifications { get; set; } = EmailNotificationMode.Off;

    /// <summary>When the last daily digest went out, so the next is a day later.</summary>
    public DateTimeOffset? LastDigestAt { get; set; }
}

public enum EmailNotificationMode
{
    Off = 0,
    Immediate = 1,
    DailyDigest = 2,
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

    /// <summary>
    /// The one account that owns the instance (dev-plan 10.1). Everything an
    /// administrator can do, plus the two things only it can: change anyone's
    /// role, and hand ownership to someone else. Exactly one exists, it cannot
    /// be demoted or suspended, and ownership moves only by transfer, so an
    /// attacker holding an admin session cannot take the instance.
    ///
    /// Ordered above <see cref="Admin"/> on purpose: every administrative
    /// check is "this role or above", so nothing has to list both.
    /// </summary>
    Owner = 2,
}
