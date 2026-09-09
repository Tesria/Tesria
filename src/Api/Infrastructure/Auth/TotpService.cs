using System.Text.Json;
using Tesria.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;

namespace Tesria.Api.Infrastructure.Auth;

public interface ITotpService
{
    /// <summary>A fresh secret, stored on the user as pending until <see cref="Verify"/> confirms it.</summary>
    (string Base32Secret, string OtpauthUri) BeginEnrolment(User user, string issuer);

    /// <summary>
    /// Checks a code against the user's pending or enabled secret. A code
    /// that matches a time step already used is refused, so a code seen over
    /// someone's shoulder is worthless once they have typed it.
    /// </summary>
    bool Verify(User user, string code, bool pending = false);

    /// <summary>Promotes the pending secret to the enabled one.</summary>
    void Enable(User user);
    void Disable(User user);

    /// <summary>A short-lived, signed token proving the password step passed.</summary>
    string IssueChallenge(Guid userId, string? ip);
    Guid? RedeemChallenge(string challenge, string? ip);
}

/// <summary>
/// RFC 6238 one-time codes (dev-plan 3.5): 20-byte secrets, SHA-1, 30-second
/// steps, six digits — the parameters every authenticator app supports.
/// Secrets rest under Data Protection, whose keys live in the database, so a
/// database backup restores them and a database *dump* alone does not read
/// them.
/// </summary>
public sealed class TotpService(IDataProtectionProvider dataProtection) : ITotpService
{
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _secrets = dataProtection.CreateProtector("Tesria.Totp.Secret.v1");
    private readonly IDataProtector _challenges = dataProtection.CreateProtector("Tesria.Totp.Challenge.v1");

    private sealed record Challenge(Guid UserId, string? Ip, DateTimeOffset ExpiresAt);

    public (string Base32Secret, string OtpauthUri) BeginEnrolment(User user, string issuer)
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(secret);
        user.TotpPendingSecretProtected = _secrets.Protect(base32);

        var label = Uri.EscapeDataString($"{issuer}:{user.Email}");
        var uri = $"otpauth://totp/{label}?secret={base32}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
        return (base32, uri);
    }

    public bool Verify(User user, string code, bool pending = false)
    {
        var stored = pending ? user.TotpPendingSecretProtected : user.TotpSecretProtected;
        if (string.IsNullOrEmpty(stored)) return false;

        string base32;
        try { base32 = _secrets.Unprotect(stored); }
        catch (System.Security.Cryptography.CryptographicException) { return false; }

        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 6) return false;

        var totp = new Totp(Base32Encoding.ToBytes(base32));
        if (!totp.VerifyTotp(digits, out var step, VerificationWindow.RfcSpecifiedNetworkDelay)) return false;

        // One step, one use. Refusing an earlier-or-equal step also refuses
        // the previous window's code once a later one has been accepted.
        if (user.TotpLastStep is { } last && step <= last) return false;
        user.TotpLastStep = step;
        return true;
    }

    public void Enable(User user)
    {
        user.TotpSecretProtected = user.TotpPendingSecretProtected;
        user.TotpPendingSecretProtected = null;
        user.TotpEnabledAt = DateTimeOffset.UtcNow;
    }

    public void Disable(User user)
    {
        user.TotpSecretProtected = null;
        user.TotpPendingSecretProtected = null;
        user.TotpEnabledAt = null;
        user.TotpLastStep = null;
    }

    public string IssueChallenge(Guid userId, string? ip) =>
        _challenges.Protect(JsonSerializer.Serialize(new Challenge(userId, ip, DateTimeOffset.UtcNow.Add(ChallengeLifetime))));

    public Guid? RedeemChallenge(string challenge, string? ip)
    {
        Challenge? parsed;
        try { parsed = JsonSerializer.Deserialize<Challenge>(_challenges.Unprotect(challenge ?? "")); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { return null; }
        if (parsed is null || parsed.ExpiresAt < DateTimeOffset.UtcNow) return null;
        // Bound to the address that passed the password step: a challenge
        // lifted from one network is no use on another.
        if (!string.Equals(parsed.Ip, ip, StringComparison.Ordinal)) return null;
        return parsed.UserId;
    }
}
