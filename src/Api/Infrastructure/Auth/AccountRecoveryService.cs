using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

public interface IAccountRecoveryService
{
    /// <summary>
    /// Replaces any existing codes with a fresh set and returns the plaintext —
    /// the only time it exists. Callers must save the DbContext.
    /// </summary>
    IReadOnlyList<string> IssueCodes(Guid userId);

    /// <summary>
    /// Spends a code. Returns the user when it matched an unused one; null for
    /// every other reason, so the caller cannot accidentally leak which.
    /// Callers must save the DbContext.
    /// </summary>
    Task<User?> RedeemCodeAsync(string email, string code, CancellationToken ct = default);

    /// <summary>How many unused codes remain, for the profile page's warning.</summary>
    Task<int> RemainingCodesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Mints an admin reset token, returning the plaintext once. Caller saves.</summary>
    string IssueResetToken(Guid userId, Guid? issuedById);

    /// <summary>Spends a reset token, or null if it is unknown, expired or used. Caller saves.</summary>
    Task<User?> RedeemResetTokenAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Offline account recovery (dev-plan 1.3): recovery codes the user keeps, and
/// a one-time reset ticket an administrator can issue. Neither needs email,
/// which for a self-hosted instance is usually not configured at all.
/// </summary>
public sealed class AccountRecoveryService(AppDbContext db) : IAccountRecoveryService
{
    public const int CodeCount = 8;

    /// <summary>Reset links expire quickly — a link is a bearer credential.</summary>
    public static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Crockford-style alphabet: no 0/O, 1/I/L, or U. Recovery codes get
    /// written on paper and typed back months later, so the characters people
    /// confuse are simply not in the set.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    private const int GroupSize = 4;
    private const int GroupCount = 3;

    public IReadOnlyList<string> IssueCodes(Guid userId)
    {
        // Regenerating invalidates the previous set: a user who thinks their
        // codes are compromised needs a way to make them stop working.
        db.RecoveryCodes.RemoveRange(db.RecoveryCodes.Where(c => c.UserId == userId));

        var now = DateTimeOffset.UtcNow;
        var codes = new List<string>(CodeCount);
        for (var i = 0; i < CodeCount; i++)
        {
            var code = GenerateCode();
            codes.Add(code);
            db.RecoveryCodes.Add(new RecoveryCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CodeHash = Hash(Normalize(code)),
                CreatedAt = now,
            });
        }
        return codes;
    }

    public async Task<User?> RedeemCodeAsync(string email, string code, CancellationToken ct = default)
    {
        var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        // An account that signs in through an identity provider has no local
        // password for a code to reset.
        if (user is null || user.Status != UserStatus.Active || user.PasswordHash is null) return null;

        var hash = Hash(Normalize(code ?? ""));
        var candidates = await db.RecoveryCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .ToListAsync(ct);

        // Fixed-time compare so the match cannot be found byte-by-byte by
        // timing, the same way ApiTokenService verifies a token.
        var match = candidates.FirstOrDefault(c => FixedTimeEquals(c.CodeHash, hash));
        if (match is null) return null;

        match.UsedAt = DateTimeOffset.UtcNow;
        return user;
    }

    public Task<int> RemainingCodesAsync(Guid userId, CancellationToken ct = default) =>
        db.RecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);

    public string IssueResetToken(Guid userId, Guid? issuedById)
    {
        // Only one live ticket per user: issuing a second must not leave the
        // first usable, or a stale link handed out earlier still works.
        db.PasswordResetTokens.RemoveRange(db.PasswordResetTokens.Where(t => t.UserId == userId));

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(token),
            ExpiresAt = DateTimeOffset.UtcNow.Add(ResetTokenLifetime),
            IssuedById = issuedById,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return token;
    }

    public async Task<User?> RedeemResetTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = Hash(token ?? "");
        var now = DateTimeOffset.UtcNow;

        // Unused tokens only in SQL; expiry is compared in memory because the
        // SQLite provider used by the tests cannot translate a DateTimeOffset
        // comparison — the same limitation AuditEndpoints already works around
        // for ordering. At most one live token exists per user, so the set this
        // materialises is tiny.
        var candidates = await db.PasswordResetTokens
            .Include(t => t.User)
            .Where(t => t.UsedAt == null)
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(t =>
            t.ExpiresAt > now && FixedTimeEquals(t.TokenHash, hash));
        if (match?.User is null || match.User.Status != UserStatus.Active) return null;

        match.UsedAt = now;
        return match.User;
    }

    /// <summary>e.g. <c>H7K2-9PQR-3MNX</c>.</summary>
    private static string GenerateCode()
    {
        var builder = new StringBuilder(GroupCount * GroupSize + GroupCount - 1);
        for (var group = 0; group < GroupCount; group++)
        {
            if (group > 0) builder.Append('-');
            for (var i = 0; i < GroupSize; i++)
                builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Strips formatting and case so a code works however it was written down —
    /// with or without dashes, in either case. The stored hash is of this
    /// canonical form.
    /// </summary>
    private static string Normalize(string code)
    {
        var chars = code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        return new string(chars);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }
}

/// <summary>
/// A fixed-window limiter for the recovery endpoint, keyed by <em>email</em>
/// rather than IP.
///
/// Per-IP is the obvious choice and is wrong here today: every request arrives
/// with Caddy's container address (see the security baseline in the dev plan),
/// so an IP limiter would throttle the whole world as one caller. Keying on the
/// supplied email bounds guesses against any one account, which is the actual
/// threat, and is unaffected by the proxy. Dev-plan 3.2 adds the real per-IP
/// limiting once 3.0 makes client addresses real.
/// </summary>
public sealed class RecoveryAttemptLimiter
{
    public const int MaxAttempts = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _attempts = new();

    public bool TryAttempt(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var normalized = key.Trim().ToLowerInvariant();
        var allowed = true;

        _attempts.AddOrUpdate(
            normalized,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.WindowStart > Window) return (1, now);
                if (existing.Count >= MaxAttempts) { allowed = false; return existing; }
                return (existing.Count + 1, existing.WindowStart);
            });

        if (_attempts.Count > 5_000) Prune(now);
        return allowed;
    }

    /// <summary>Clears the window after a success, so a legitimate user is not held out.</summary>
    public void Reset(string key) => _attempts.TryRemove(key.Trim().ToLowerInvariant(), out _);

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, entry) in _attempts)
            if (now - entry.WindowStart > Window) _attempts.TryRemove(key, out _);
    }
}
