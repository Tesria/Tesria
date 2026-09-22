namespace Tesria.Api.Domain;

/// <summary>
/// One single-use offline recovery code. A set is minted at registration and
/// shown to the user exactly once; only the hash is kept.
///
/// This is the recovery path that works with no email server configured, which
/// for a self-hosted wiki is the common case.
/// </summary>
public class RecoveryCode
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// SHA-256 of the code, hex-encoded.
    ///
    /// Deliberately not Argon2id, unlike a password. Argon2's cost exists to
    /// make guessing a <em>low-entropy</em> secret expensive; these codes are
    /// 60 bits of cryptographic randomness, where a fast hash is already
    /// unguessable. Using Argon2 here would instead mean up to eight
    /// deliberately-slow verifications per attempt: bad for the user and a
    /// free denial-of-service lever for an attacker. Same reasoning, and the
    /// same construction, as <see cref="ApiToken"/>.
    /// </summary>
    public required string CodeHash { get; set; }

    /// <summary>Set the moment the code is spent, so it can never be replayed.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A one-time password-reset ticket minted by an administrator, for the case
/// where someone has lost both their password and their recovery codes and
/// there is no email server to fall back on. The admin hands the link over out
/// of band; possession of it is the proof.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 of the token, hex-encoded. See <see cref="RecoveryCode.CodeHash"/>.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Short-lived on purpose: a reset link is a bearer credential.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>
    /// The administrator who issued it, or null when the account holder
    /// requested it by email (dev-plan 4.2).
    /// </summary>
    public Guid? IssuedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
