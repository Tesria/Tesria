namespace Tesria.Api.Domain;

/// <summary>
/// A single-use invitation to register on a closed instance (dev-plan 1.4).
///
/// The only way to add a user when <see cref="SiteSettings.AllowPublicRegistration"/>
/// is off and there is no email server — an administrator creates one and
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
}
