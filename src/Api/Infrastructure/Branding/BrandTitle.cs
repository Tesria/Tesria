namespace Tesria.Api.Infrastructure.Branding;

/// <summary>
/// The browser tab's title (dev-plan 13.1, decision 15), in the owner's
/// words: "Instance Name - Space Name / Page Name".
///
/// Mirrored exactly by <c>src/web/src/title.ts</c>, which the SPA uses after
/// it loads. The server uses this copy for the first paint and for exports,
/// so a tab never shows one title and then another for the same page. The
/// two share a set of test cases for that reason.
/// </summary>
public static class BrandTitle
{
    public const string Default = "Tesria";

    /// <summary>
    /// The longest a space's name or a page's title runs in the tab before it
    /// ends in "…" (QA t3-R04: a 500-character title made a 500-character tab
    /// title, which the window list and history show in full).
    /// </summary>
    public const int MaxPart = 80;

    /// <summary>
    /// A page: <c>Acme Docs - Engineering / Architecture</c>. A space:
    /// <c>Acme Docs - Engineering</c>. A section: <c>Acme Docs - Search</c>.
    /// Nothing: <c>Acme Docs</c>. Names are used as given, slashes and
    /// hyphens included: this is a title, not a path.
    /// </summary>
    public static string Format(string? instance, string? space = null, string? page = null, string? section = null)
    {
        var name = Clean(instance) ?? Default;
        var s = Shorten(Clean(space));
        var p = Shorten(Clean(page));
        if (s is not null) return p is null ? $"{name} - {s}" : $"{name} - {s} / {p}";
        var sec = Clean(section);
        return sec is null ? name : $"{name} - {sec}";
    }

    /// <summary>
    /// The section a path belongs to, for routes that are not inside a
    /// space. Kept beside the SPA's copy of the same table.
    /// </summary>
    public static string? SectionFor(string path)
    {
        var p = (path ?? "").TrimEnd('/').ToLowerInvariant();
        if (p is "" or "/") return null;
        if (p == "/spaces") return "Spaces";
        if (p.StartsWith("/spaces/")) return null;
        if (p.StartsWith("/search")) return "Search";
        if (p.StartsWith("/labels")) return "Labels";
        if (p.StartsWith("/profile")) return "Profile";
        if (p.StartsWith("/admin")) return "Administration";
        if (p == "/login") return "Sign In";
        if (p == "/register") return "Create Account";
        if (p is "/recover" or "/reset") return "Reset Your Password";
        if (p == "/confirm-email") return "Confirm Your Email";
        if (p == "/setup") return "Set Up";
        if (p == "/welcome") return "Welcome";
        return null;
    }

    /// <summary>
    /// Cut to <see cref="MaxPart"/> UTF-16 units with "…", never through the
    /// middle of a surrogate pair. Counted the way JavaScript counts, so the
    /// SPA's copy cuts at the same place.
    /// </summary>
    private static string? Shorten(string? value)
    {
        if (value is null || value.Length <= MaxPart) return value;
        var cut = MaxPart - 1;
        if (char.IsHighSurrogate(value[cut - 1])) cut--;
        return value[..cut].TrimEnd() + "\u2026";
    }

    private static string? Clean(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }
}
