namespace Tesria.Api.Features.Embeds;

/// <summary>
/// Host matching for embeds. Deliberately tiny and deliberately strict:
/// this is the whole of the trust decision, so it must be readable in one
/// sitting.
/// </summary>
public static class EmbedAllowlist
{
    /// <summary>
    /// Entries from the stored setting: one per line or comma-separated,
    /// blanks dropped, lower-cased. Anything that is not a host name is left
    /// out, so a value stored before entries were checked (or written to the
    /// database by hand) can never reach the CSP or an exported page.
    /// </summary>
    public static string[] Parse(string? raw) => Read(raw).Valid;

    /// <summary>
    /// The same reading, keeping what was refused, for the settings form:
    /// a save with any refused entry is refused as a whole, naming them.
    /// </summary>
    public static (string[] Valid, string[] Invalid) Read(string? raw)
    {
        var valid = new List<string>();
        var invalid = new List<string>();
        foreach (var item in (raw ?? "").Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Normalize(item) is { } entry)
            {
                if (!valid.Contains(entry)) valid.Add(entry);
            }
            else if (!invalid.Contains(item))
            {
                invalid.Add(item);
            }
        }
        return (valid.ToArray(), invalid.ToArray());
    }

    /// <summary>
    /// One entry as it is stored and matched, or null when it is not a host
    /// name: letters, digits and hyphens in dot-separated labels, with an
    /// optional leading <c>.</c> for "and its subdomains" (<c>*.</c> is
    /// read as the same thing). No scheme, port, path, spaces or quotes:
    /// these entries are written into the CSP and into exported pages.
    /// </summary>
    public static string? Normalize(string item)
    {
        var e = item.Trim().ToLowerInvariant();
        if (e.StartsWith("*.", StringComparison.Ordinal)) e = e[1..];
        var subdomains = e.StartsWith('.');
        var host = (subdomains ? e[1..] : e).TrimEnd('.');
        if (host.Length is 0 or > 253) return null;
        if (host.Any(c => c > 127))
        {
            try { host = new System.Globalization.IdnMapping().GetAscii(host); }
            catch (ArgumentException) { return null; }
        }
        if (!HostName.IsMatch(host)) return null;
        return subdomains ? "." + host : host;
    }

    private static readonly System.Text.RegularExpressions.Regex HostName = new(
        @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
    /// bare host. `https:` only: an http frame on an https page is blocked
    /// by the browser anyway, and allowing it here would only be misleading.
    /// </summary>
    public static IEnumerable<string> CspSources(IEnumerable<string> entries) =>
        entries.Select(e => e.StartsWith('.') ? $"https://*{e}" : $"https://{e}").Distinct();
}
