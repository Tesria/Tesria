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
    /// A page: <c>Acme Docs - Engineering / Architecture</c>. A space:
    /// <c>Acme Docs - Engineering</c>. A section: <c>Acme Docs - Search</c>.
    /// Nothing: <c>Acme Docs</c>. Names are used as given, slashes and
    /// hyphens included: this is a title, not a path.
    /// </summary>
    public static string Format(string? instance, string? space = null, string? page = null, string? section = null)
    {
        var name = Clean(instance) ?? Default;
        var s = Clean(space);
        var p = Clean(page);
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
        if (p == "/login") return "Sign in";
        if (p == "/register") return "Create account";
        if (p is "/recover" or "/reset") return "Reset your password";
        if (p == "/setup") return "Set up";
        if (p == "/welcome") return "Welcome";
        return null;
    }

    private static string? Clean(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }
}
