using System.Text.RegularExpressions;
using Isopoh.Cryptography.Argon2;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>Hashes and verifies user passwords.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);

    /// <summary>
    /// True when a hash was made with weaker parameters than the current
    /// ones. Checked on successful sign-in, the one moment the plaintext is
    /// available, so raising the parameters here upgrades every account
    /// over time without a forced reset.
    /// </summary>
    bool NeedsRehash(string encodedHash);
}

/// <summary>
/// Argon2id with pinned parameters (dev-plan 3.5): 64 MiB, 3 passes, 4
/// lanes, 32-byte output: the OWASP-recommended shape for interactive
/// logins in 2026, and deliberately written down rather than left to the
/// library's defaults, which have changed between versions. The encoded
/// string carries salt and parameters, so old hashes keep verifying and
/// <see cref="NeedsRehash"/> can tell them apart.
/// </summary>
public sealed partial class Argon2PasswordHasher : IPasswordHasher
{
    public const int TimeCost = 3;
    public const int MemoryCostKiB = 65536;
    public const int Parallelism = 4;
    public const int HashLength = 32;

    public string Hash(string password) =>
        Argon2.Hash(password, TimeCost, MemoryCostKiB, Parallelism, Argon2Type.HybridAddressing, HashLength);

    public bool Verify(string password, string encodedHash) =>
        Argon2.Verify(encodedHash, password);

    public bool NeedsRehash(string encodedHash)
    {
        var m = Parameters().Match(encodedHash ?? "");
        if (!m.Success) return true;
        return int.Parse(m.Groups["m"].Value) < MemoryCostKiB
            || int.Parse(m.Groups["t"].Value) < TimeCost
            || int.Parse(m.Groups["p"].Value) != Parallelism
            || !encodedHash!.StartsWith("$argon2id$", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\$m=(?<m>\d+),t=(?<t>\d+),p=(?<p>\d+)\$")]
    private static partial Regex Parameters();
}
