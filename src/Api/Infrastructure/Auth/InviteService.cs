using System.Security.Cryptography;
using System.Text;
using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

public interface IInviteService
{
    /// <summary>Mints an invite, returning the plaintext token once. Caller saves.</summary>
    string Issue(Guid createdById, string? email, TimeSpan lifetime);

    /// <summary>
    /// The invite this token names, if it is unused, unexpired, and (when the
    /// invite is address-bound) issued for <paramref name="email"/>. Null
    /// otherwise; <see cref="CheckAsync"/> says which it was.
    /// </summary>
    Task<Invite?> FindUsableAsync(string token, string email, CancellationToken ct = default);

    /// <summary>
    /// As <see cref="FindUsableAsync"/>, and when the invite cannot be used,
    /// why not (T7-005, t2-005). A null <paramref name="email"/> skips the
    /// address check, for a link that has only been opened.
    /// </summary>
    Task<InviteCheck> CheckAsync(string token, string? email, CancellationToken ct = default);
}

/// <summary>Why an invite link cannot make an account.</summary>
public enum InviteProblem
{
    /// <summary>No invite has this token: mistyped, cut short, or revoked (revoking deletes it).</summary>
    NotValid,
    Used,
    Expired,
    OtherAddress,
}

/// <summary>The invite, when it can be used; otherwise the reason it cannot.</summary>
public sealed record InviteCheck(Invite? Usable, InviteProblem? Problem)
{
    /// <summary>
    /// What to tell the person holding the link (T7-005, t2-005). Every case
    /// used to answer "Registration is by invitation on this instance.",
    /// which read as if they had no invite at all. Saying which it was gives
    /// nothing away: only somebody holding a real token (256 random bits)
    /// hears anything but "not valid", and they were sent the invite.
    /// </summary>
    public static string Message(InviteProblem problem) => problem switch
    {
        InviteProblem.Used =>
            "This invite has already been used: each invite makes one account. If that was you, sign in; otherwise ask whoever invited you for a new one.",
        InviteProblem.Expired =>
            "This invite has expired. Ask whoever invited you for a new one.",
        InviteProblem.OtherAddress =>
            "This invite is for a different email address. Use the address it was sent to, or ask whoever invited you for a new one.",
        _ =>
            "This invite link is not valid: it may have been revoked, or not copied in full. Ask whoever invited you for a new one.",
    };

    /// <summary>The same, as a reason a script can check (the refusal's <c>code</c>).</summary>
    public static string Code(InviteProblem problem) => problem switch
    {
        InviteProblem.Used => "invite_used",
        InviteProblem.Expired => "invite_expired",
        InviteProblem.OtherAddress => "invite_other_address",
        _ => "invite_not_valid",
    };
}

public sealed class InviteService(AppDbContext db) : IInviteService
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    public string Issue(Guid createdById, string? email, TimeSpan lifetime)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        db.Invites.Add(new Invite
        {
            Id = Guid.NewGuid(),
            TokenHash = Hash(token),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
            CreatedById = createdById,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return token;
    }

    public async Task<Invite?> FindUsableAsync(string token, string email, CancellationToken ct = default) =>
        (await CheckAsync(token, email, ct)).Usable;

    public async Task<InviteCheck> CheckAsync(string token, string? email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return new(null, InviteProblem.NotValid);

        var hash = Hash(token.Trim());
        var now = DateTimeOffset.UtcNow;

        // Used ones too, so that a used link can be told apart from a wrong
        // one. Expiry compared in memory: the SQLite provider used by the
        // tests cannot translate a DateTimeOffset comparison. Invites are few.
        var candidates = await db.Invites.ToListAsync(ct);
        var match = candidates.FirstOrDefault(i => FixedTimeEquals(i.TokenHash, hash));
        if (match is null) return new(null, InviteProblem.NotValid);
        if (match.UsedAt is not null) return new(null, InviteProblem.Used);
        if (match.ExpiresAt <= now) return new(null, InviteProblem.Expired);

        // An address-bound invite is bound: otherwise a link intended for one
        // person becomes a registration for whoever it is forwarded to.
        if (email is not null && match.Email is not null
            && !string.Equals(match.Email, email, StringComparison.OrdinalIgnoreCase))
            return new(null, InviteProblem.OtherAddress);

        return new(match, null);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string a, string b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
