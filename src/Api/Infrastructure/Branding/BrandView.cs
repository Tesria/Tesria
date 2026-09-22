using Tesria.Api.Domain;

namespace Tesria.Api.Infrastructure.Branding;

/// <summary>One logo as the page needs it: where to fetch it, and its shape.</summary>
public sealed record BrandLogo(string Url, string Format, int? Width, int? Height);

/// <summary>
/// The instance's branding as everything that renders it sees it (dev-plan
/// 13.1): the SPA through <c>/api/instance</c>, the server-rendered page
/// shell, and the exports. One projection of <see cref="SiteSettings"/>, so
/// the three cannot disagree about what "branded" means.
///
/// Every default reproduces Tesria exactly. That is the owner's rule: "This
/// should only replace the branding if someone intentionally configures the
/// branding."
/// </summary>
public sealed record BrandView(
    string Name,
    bool HasCustomName,
    string Display,
    string SignInArrangement,
    BrandLogo? Logo,
    BrandLogo? LogoDark,
    string? FaviconHash,
    bool FaviconHasSvg,
    string ThemePolicy,
    string AccentPolicy,
    string? AccentName,
    string? AccentLight,
    string? AccentDark,
    bool IsCustomized)
{
    public const string DisplayBoth = "logo-and-name";
    public const string DisplayLogo = "logo";
    public const string DisplayName = "name";
    public static readonly string[] Displays = [DisplayBoth, DisplayLogo, DisplayName];

    public const string SideBySide = "side-by-side";
    public const string Stacked = "stacked";
    public static readonly string[] Arrangements = [SideBySide, Stacked];

    public const string Any = "any";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Locked = "locked";
    public static readonly string[] ThemePolicies = [Any, Light, Dark];
    public static readonly string[] AccentPolicies = [Any, Locked];

    /// <summary>The accent that means "the custom colours".</summary>
    public const string BrandAccent = "brand";
    public static readonly string[] BuiltInAccents = ["blue", "teal", "green", "purple", "orange", "magenta"];

    /// <summary>
    /// A brand name or a logo: the instance has its own identity rather than
    /// Tesria's. What "Powered by Tesria" under the sign-in card keys off, and
    /// nothing else: a theme lock alone is not a brand (decision F).
    /// </summary>
    public bool HasIdentity => HasCustomName || Logo is not null;

    /// <summary>The accent everyone is held to, or null when people choose.</summary>
    public string? LockedAccent => AccentPolicy == Locked ? EffectiveAccent : null;

    /// <summary>The accent someone gets before they have picked one.</summary>
    public string EffectiveAccent => AccentName ?? "blue";

    public bool HasBrandAccent => AccentLight is not null || AccentDark is not null;

    public static BrandView From(SiteSettings s)
    {
        BrandLogo? Logo(string path, string? hash, string? format, int? w, int? h) =>
            hash is null || format is null ? null : new BrandLogo($"/api/branding/{path}?v={hash}", format, w, h);

        var name = string.IsNullOrWhiteSpace(s.BrandName) ? null : s.BrandName.Trim();
        var logo = Logo("logo", s.BrandLogoHash, s.BrandLogoFormat, s.BrandLogoWidth, s.BrandLogoHeight);
        var logoDark = Logo("logo-dark", s.BrandLogoDarkHash, s.BrandLogoDarkFormat, s.BrandLogoDarkWidth, s.BrandLogoDarkHeight);
        var light = AccentColors.Normalize(s.BrandAccentLight);
        var dark = AccentColors.Normalize(s.BrandAccentDark);

        // A policy asking for the brand accent with no brand colour to show
        // falls back to Tesria's, rather than rendering an accent that does
        // not exist.
        var accent = s.AccentName;
        if (accent == BrandAccent && light is null && dark is null) accent = null;
        if (accent is not null && accent != BrandAccent && !BuiltInAccents.Contains(accent)) accent = null;

        var display = Displays.Contains(s.BrandDisplay) ? s.BrandDisplay : DisplayBoth;
        // "Logo only" with no logo would show nothing at all. Show the name.
        if (display == DisplayLogo && logo is null) display = DisplayBoth;

        var view = new BrandView(
            Name: name ?? BrandTitle.Default,
            HasCustomName: name is not null,
            Display: display,
            SignInArrangement: Arrangements.Contains(s.SignInArrangement) ? s.SignInArrangement : SideBySide,
            Logo: logo,
            LogoDark: logoDark,
            FaviconHash: s.BrandFaviconHash,
            FaviconHasSvg: s.BrandFaviconHash is not null && s.BrandFaviconHasSvg,
            ThemePolicy: ThemePolicies.Contains(s.ThemePolicy) ? s.ThemePolicy : Any,
            AccentPolicy: AccentPolicies.Contains(s.AccentPolicy) ? s.AccentPolicy : Any,
            AccentName: accent,
            AccentLight: light,
            AccentDark: dark,
            IsCustomized: false);
        return view with
        {
            IsCustomized = view.HasIdentity || logoDark is not null || s.BrandFaviconHash is not null
                || light is not null || dark is not null || accent is not null
                || view.ThemePolicy != Any || view.AccentPolicy != Any
                || s.BrandDisplay != DisplayBoth || s.SignInArrangement != SideBySide,
        };
    }

    /// <summary>
    /// The attributes the page's <c>&lt;html&gt;</c> element carries, read
    /// by the inline theme script before first paint and by the favicon
    /// painter. Attributes rather than script, because the script is allowed
    /// by the CSP by its hash and must stay byte-identical (decision 3).
    /// </summary>
    public IReadOnlyList<(string Name, string Value)> HtmlAttributes()
    {
        var attrs = new List<(string, string)>();
        if (ThemePolicy != Any) attrs.Add(("data-theme-lock", ThemePolicy));
        if (LockedAccent is { } locked) attrs.Add(("data-accent-lock", locked));
        else if (AccentName is { } preferred) attrs.Add(("data-accent-default", preferred));
        if (AccentLight is not null) attrs.Add(("data-brand-accent-light", AccentLight));
        if (AccentDark is not null) attrs.Add(("data-brand-accent-dark", AccentDark));
        if (FaviconHash is not null) attrs.Add(("data-brand-favicon", "1"));
        return attrs;
    }

    /// <summary>The favicon links for the page head, or null to keep Tesria's generated one.</summary>
    public string? FaviconLinks()
    {
        if (FaviconHash is null) return null;
        var v = FaviconHash;
        var svg = FaviconHasSvg
            ? $"<link rel=\"icon\" type=\"image/svg+xml\" href=\"/api/branding/favicon.svg?v={v}\" />\n    "
            : "";
        return svg
            + $"<link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/api/branding/favicon-32.png?v={v}\" />\n    "
            + $"<link rel=\"apple-touch-icon\" sizes=\"180x180\" href=\"/api/branding/favicon-180.png?v={v}\" />";
    }

    /// <summary>The custom accent's stylesheet, empty when there is none.</summary>
    public string AccentStylesheet() => AccentColors.Stylesheet(AccentLight, AccentDark);
}
