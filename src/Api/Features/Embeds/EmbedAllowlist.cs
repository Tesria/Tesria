namespace Tesria.Api.Features.Embeds;

/// <summary>
/// Host matching for embeds. Deliberately tiny and deliberately strict:
/// this is the whole of the trust decision, so it must be readable in one
/// sitting.
/// </summary>
public static class EmbedAllowlist
{
    /// <summary>Entries from the stored setting: one per line or comma-separated, blanks dropped, lower-cased.</summary>
    public static string[] Parse(string? raw) =>
        (raw ?? "")
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant().TrimEnd('.'))
            .Where(e => e.Length > 0)
            .Distinct()
            .ToArray();

    /// <summary>
    /// Whether a host is allowed. An entry beginning with <c>.</c> matches the
    /// domain and its subdomains (<c>.youtube.com</c> matches
    /// <c>www.youtube.com</c> and <c>youtube.com</c>); any other entry must
    /// match exactly.
    ///
    /// Suffix matching is done on a label boundary, never as a plain
    /// `EndsWith`: <c>.youtube.com</c> must not admit
    /// <c>evil-youtube.com</c> or <c>youtube.com.attacker.net</c>.
    /// </summary>
    public static bool IsAllowed(string? host, IEnumerable<string> entries)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var candidate = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var entry in entries)
        {
            if (entry.StartsWith('.'))
            {
                var domain = entry[1..];
                if (candidate == domain || candidate.EndsWith('.' + domain, StringComparison.Ordinal)) return true;
            }
            else if (candidate == entry)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The `frame-src` sources for the CSP, derived from the same entries.
    /// A subdomain entry becomes a wildcard origin; an exact entry becomes a
    /// bare host. `https:` only — an http frame on an https page is blocked
    /// by the browser anyway, and allowing it here would only be misleading.
    /// </summary>
    public static IEnumerable<string> CspSources(IEnumerable<string> entries) =>
        entries.Select(e => e.StartsWith('.') ? $"https://*{e}" : $"https://{e}").Distinct();
}
