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
    /// otherwise — the caller must not learn which of those it was.
    /// </summary>
    Task<Invite?> FindUsableAsync(string token, string email, CancellationToken ct = default);
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

    public async Task<Invite?> FindUsableAsync(string token, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = Hash(token.Trim());
        var now = DateTimeOffset.UtcNow;

        // Expiry compared in memory: the SQLite provider used by the tests
        // cannot translate a DateTimeOffset comparison. Unused invites are few.
        var candidates = await db.Invites.Where(i => i.UsedAt == null).ToListAsync(ct);
        var match = candidates.FirstOrDefault(i => i.ExpiresAt > now && FixedTimeEquals(i.TokenHash, hash));
        if (match is null) return null;

        // An address-bound invite is bound: otherwise a link intended for one
        // person becomes a registration for whoever it is forwarded to.
        if (match.Email is not null && !string.Equals(match.Email, email, StringComparison.OrdinalIgnoreCase))
            return null;

        return match;
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string a, string b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
