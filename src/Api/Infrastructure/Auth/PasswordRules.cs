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
/// the first anyone guesses), not one of them dressed up with numbers or
/// symbols at the ends or the usual letter swaps ("password123", "P@ssw0rd!":
/// guessers try those next), and at most <see cref="MaxLength"/>, which
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

    /// <summary>
    /// Whether this is a common password with numbers or symbols added at
    /// either end, or with letters swapped for look-alikes (@ for a, 0 for
    /// o, 3 for e, 1 for i or l, $ or 5 for s, 7 for t): "password123",
    /// "Summer2026!", "p@ssw0rd". Only a core of four or more letters counts,
    /// so a short word inside a long password does not.
    /// </summary>
    public static bool IsDressedUpCommon(string password)
    {
        foreach (var core in Cores(password))
            if (core.Length >= 4 && core.Any(char.IsLetter) && Common.Value.Contains(core)) return true;
        return false;
    }

    private static IEnumerable<string> Cores(string password)
    {
        static bool Decoration(char c) => char.IsDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c);
        var stripped = password.Trim();
        var start = 0;
        while (start < stripped.Length && Decoration(stripped[start])) start++;
        var end = stripped.Length;
        while (end > start && Decoration(stripped[end - 1])) end--;
        var trimmed = stripped[start..end];
        yield return trimmed;
        // Swapped letters, read back both ways a 1 can stand for, in the
        // whole password and in what is left once its ends are gone.
        foreach (var one in new[] { 'i', 'l' })
            foreach (var source in new[] { stripped, trimmed })
            {
                var swapped = new string(source.Select(c => c switch
                {
                    '@' or '4' => 'a', '0' => 'o', '3' => 'e', '$' or '5' => 's', '7' => 't', '1' or '!' or '|' => one,
                    _ => c,
                }).ToArray());
                yield return swapped;
                var s2 = 0;
                while (s2 < swapped.Length && Decoration(swapped[s2])) s2++;
                var e2 = swapped.Length;
                while (e2 > s2 && Decoration(swapped[e2 - 1])) e2--;
                yield return swapped[s2..e2];
            }
    }

    /// <summary>What is wrong with a new password, in words for the person choosing it, or null when it will do.</summary>
    public static string? Problem(string? password)
    {
        var p = password ?? "";
        if (p.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (p.Length > MaxLength) return $"Password must be at most {MaxLength} characters.";
        if (string.IsNullOrWhiteSpace(p)) return "Password cannot be only spaces.";
        if (IsCommon(p))
            return "That password is one of the most common ones, which are the first anyone guesses. Choose another.";
        if (IsDressedUpCommon(p))
            return "That is a common password with numbers, symbols or swapped letters added, which guessers try straight after the common ones. Choose another.";
        return null;
    }
}
