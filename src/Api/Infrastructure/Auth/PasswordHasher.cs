using Isopoh.Cryptography.Argon2;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>Hashes and verifies user passwords.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}

/// <summary>
/// Argon2id password hashing (PLAN §4). Uses the Isopoh library's self-describing
/// encoded string, which embeds the salt and parameters, so verification needs no
/// separate salt column and parameters can evolve without breaking old hashes.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) =>
        Argon2.Hash(password, type: Argon2Type.HybridAddressing);

    public bool Verify(string password, string encodedHash) =>
        Argon2.Verify(encodedHash, password);
}
