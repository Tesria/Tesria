namespace Tesria.Api.Domain;

/// <summary>
/// A single-use invitation to register on a closed instance (dev-plan 1.4).
///
/// The only way to add a user when <see cref="SiteSettings.AllowPublicRegistration"/>
/// is off and there is no email server: an administrator creates one and
/// passes the link on however they already communicate.
/// </summary>
public class Invite
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 of the token, hex-encoded. Same construction as ApiToken.</summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Optional: when set, only this address may use the invite. Left null for
    /// a link an admin wants to hand to whoever needs it.
    /// </summary>
    public string? Email { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>The account created with it, once it has been used.</summary>
    public Guid? UsedByUserId { get; set; }

    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The tier the account starts at (dev-plan 21.3): Member, or Admin when
    /// the inviter may make administrators. Checked again when the account is
    /// made, since the inviter may have lost the right meanwhile.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>The groups the account goes into when it is made (21.3).</summary>
    public ICollection<InviteGroup> Groups { get; set; } = new List<InviteGroup>();
}

/// <summary>
/// One group an invite puts its account in (dev-plan 21.3): Global Viewers,
/// Global Reviewers, or one of a space's four. A table rather than a JSON
/// column so that each row points at its group: when a space is deleted, its
/// groups go and so do the invite's places in them, rather than leaving an id
/// that names nothing for the registration to trip over.
/// </summary>
public class InviteGroup
{
    public Guid InviteId { get; set; }
    public Invite? Invite { get; set; }

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }
}
