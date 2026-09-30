using System.Reflection;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// The one rule for a new password (T1-026), used by every path that sets
/// one: registration (the setup wizard's owner included), a password change,
/// a reset link and a recovery code. Sign-in never applies it, so a password
/// set before the rule keeps working until its owner changes it.
///
/// At least <see cref="MinLength"/> characters, not only spaces, not one of
/// the ten thousand most common passwords (compared ignoring case: those are
/// the first anyone guesses), and at most <see cref="MaxLength"/>, which
/// bounds how long hashing one can take.
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 8;

    /// <summary>A 5,000-character password took about a second to hash (T1-026).</summary>
    public const int MaxLength = 1024;

    /// <summary>
    /// SecLists' 10k-most-common.txt, embedded (CommonPasswords/LICENSE.txt
    /// says where it is from; the About tab's notices carry its license).
    /// </summary>
    private static readonly Lazy<HashSet<string>> Common = new(Load);

    private static HashSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tesria.CommonPasswords.txt")
            ?? throw new InvalidOperationException("The common password list is missing from the build.");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
            if (line.Length > 0) set.Add(line);
        return set;
    }

    /// <summary>Whether this is on the bundled list of common passwords, ignoring case.</summary>
    public static bool IsCommon(string password) => Common.Value.Contains(password);

    /// <summary>What is wrong with a new password, in words for the person choosing it, or null when it will do.</summary>
    public static string? Problem(string? password)
    {
        var p = password ?? "";
        if (p.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (p.Length > MaxLength) return $"Password must be at most {MaxLength} characters.";
        if (string.IsNullOrWhiteSpace(p)) return "Password cannot be only spaces.";
        if (IsCommon(p))
            return "That password is one of the most common ones, which are the first anyone guesses. Choose another.";
        return null;
    }
}
