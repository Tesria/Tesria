using System.Text;
using Tesria.Api.Domain;

namespace Tesria.Api.Features.Export;

/// <summary>
/// The application's furniture around an exported page: the top bar with the
/// brand and the appearance menu, and (for a site) the space sidebar with its
/// icon, name and page tree. Requested 2026-09-20: an exported page should
/// look more like the product itself.
///
/// <para><b>Why this is built here rather than captured.</b> Phase 12's rule is
/// that an export should be a photograph of the page, not a second rendering
/// of it, because a second renderer drifts. That rule is about page
/// <i>content</i>: thirty-five node types, eight of which only a browser can
/// draw at all. The chrome is not content. It is site furniture the exporter
/// already owns and already decides: which pages are in the site, what they
/// are called, where they live, which one you are on. None of it exists on the
/// captured page, because the capture route deliberately has no chrome.</para>
///
/// <para>Building it once here also covers the two pages that are <i>not</i>
/// captures, the site's index and its 404. Rendering it in the SPA instead
/// would leave those two needing a second copy, which is the drift this is
/// meant to avoid.</para>
///
/// <para><b>What keeps it from drifting anyway</b> is that it emits the
/// application's own class names and ships the application's own compiled
/// stylesheet. Restyle <c>.topbar</c> or <c>.sidebar</c> in <c>index.css</c>
/// and an export restyles with it, with nothing to keep in step. What is
/// duplicated is the markup's shape, which is small, and the few icon paths,
/// which are noted where they are copied from.</para>
///
/// <para>PDFs get none of this. A PDF is paper: no toolbar, no sidebar, no
/// theme, and the capture route keeps forcing it light.</para>
/// </summary>
public static partial class SiteChrome
{
    /// <summary>
    /// The instance's branding as an export carries it (dev-plan 13.1,
    /// decision 12): the name and logo in the top-left of the bar, the
    /// favicon, the custom accent and the theme locks, as they were at the
    /// moment of export. An export is a photograph and does not change later.
    ///
    /// <para>Paths are either site-relative (<c>assets/brand-logo.svg</c>,
    /// for a site) or <c>data:</c> URIs (for a single file), and a logo is only
    /// ever referenced by <c>&lt;img&gt;</c>, never inlined as markup. An
    /// exported file is opened from disk with no CSP at all, so that is the
    /// whole of its defense against a hostile SVG (decision 6).</para>
    /// </summary>
    /// <param name="Name">The brand name, or Tesria.</param>
    /// <param name="LogoPath">The logo, or null for Tesria's mark.</param>
    public record Brand(string Name, string? LogoPath = null)
    {
        /// <summary><c>logo-and-name</c>, <c>logo</c> or <c>name</c>.</summary>
        public string Display { get; init; } = "logo-and-name";
        public string? LogoDarkPath { get; init; }
        public int? LogoWidth { get; init; }
        public int? LogoHeight { get; init; }

        /// <summary>The instance name, which titles every exported page.</summary>
        public string Instance { get; init; } = "Tesria";

        public string? FaviconHref { get; init; }
        public string FaviconType { get; init; } = "image/png";

        /// <summary>The image rule as a <c>&lt;meta&gt;</c> CSP (dev-plan 14.3); empty when images are not restricted.</summary>
        public string ImagePolicyMeta { get; init; } = "";

        /// <summary>The custom accent's stylesheet; empty when there is none.</summary>
        public string AccentCss { get; init; } = "";

        /// <summary>The <c>data-theme-lock</c> and friends, which the export's theme script reads.</summary>
        public IReadOnlyList<(string Name, string Value)> Attributes { get; init; } = [];

        public string? ThemeLock => Attributes.FirstOrDefault(a => a.Name == "data-theme-lock").Value;
        public string? AccentLock => Attributes.FirstOrDefault(a => a.Name == "data-accent-lock").Value;
        public bool HasBrandAccent => AccentCss.Length > 0;
    }

    /// <summary>What the sidebar needs to know about the space it is showing.</summary>
    /// <param name="IconPath">
    /// A site-relative path to the space's uploaded icon, or null when it has
    /// none and the tile is drawn from its key.
    /// </param>
    /// <summary>
    /// No tile color: the application's per-space color is deliberately not
    /// carried into an export, because an export is one space and the color
    /// only means something in a list of them. See <see cref="SpaceIcon"/>.
    /// </summary>
    public record SpaceHead(
        string Key, string Name, bool IsPublic,
        SpaceIconKind IconKind, string? IconValue, string? IconPath,
        SpaceTreeStyle TreeStyle = SpaceTreeStyle.Plain);

    public static SpaceHead HeadOf(Space space, string? iconPath) =>
        new(space.Key, space.Name, space.IsPublic, space.IconKind, space.IconValue, iconPath, space.TreeStyle);

    /* ---- the pieces ------------------------------------------------------ */

    /// <summary>
    /// The top bar: the brand on the left, the appearance menu on the right.
    /// The brand links home, which for a site is its index and for a
    /// single-page file is nowhere, so it is rendered as plain text there
    /// rather than as a link that goes nowhere.
    /// </summary>
    public static string Topbar(Brand brand, string? homeHref, string currentPath = "")
    {
        var inner = BrandInner(brand, currentPath);
        var home = homeHref is null
            ? $"<span class=\"brand\">{inner}</span>"
            : $"<a class=\"brand\" href=\"{SiteExport.Escape(homeHref)}\">{inner}</a>";

        // The phone menu (2026-09-24: on a phone the page tree sat
        // above the page and pushed it off the screen). The app's own button,
        // shown only on a phone; it opens the sidebar over the page.
        var menu = """<button type="button" class="topbar__hamburger site-menu" aria-label="Pages" aria-expanded="false" aria-controls="site-pages">☰</button>""";
        // Docked from the start (the owner, 2026-09-28): in Glass the bar is
        // the frosted strip the app's becomes on scrolling, so the logo never
        // floats on its own. Minimal draws its bar either way.
        return $"""<header class="topbar is-docked">{menu}{home}<div class="topbar__right">{WidthToggle()}{ThemeMenu(brand)}</div></header>""";
    }

    /// <summary>
    /// The mark and the name, in the same markup and classes as the header in
    /// <c>Layout.tsx</c>, so the stylesheet the export ships sizes them the
    /// same. A dark-mode logo is a second image the stylesheet swaps in.
    /// </summary>
    private static string BrandInner(Brand brand, string currentPath)
    {
        var showLogo = brand.Display != "name";
        var showName = brand.Display != "logo" || brand.LogoPath is null;
        var mark = !showLogo ? "" : brand.LogoPath is null
            ? $"<span class=\"brand__mark\">{BrandMark}</span>"
            : $"<span class=\"brand__logo-wrap\">{LogoImg(brand.LogoPath, brand, currentPath, dark: false)}"
              + (brand.LogoDarkPath is null ? "" : LogoImg(brand.LogoDarkPath, brand, currentPath, dark: true))
              + "</span>";
        // Tesria's own name is its wordmark (the brand kit); an instance's
        // name keeps its own letters.
        var wordClass = brand.Name == "Tesria" ? "brand__word brand__word--tesria" : "brand__word";
        var word = showName ? $"<span class=\"{wordClass}\">{SiteExport.Escape(brand.Name)}</span>" : "";
        return mark + word;
    }

    private static string LogoImg(string path, Brand brand, string currentPath, bool dark)
    {
        var size = brand.LogoWidth is { } w && brand.LogoHeight is { } h ? $" width=\"{w}\" height=\"{h}\"" : "";
        var cls = brand.LogoDarkPath is null ? "brand__logo" : dark ? "brand__logo brand__logo--dark" : "brand__logo brand__logo--light";
        // The name beside it already says who this is; alone, the logo is the name.
        var alt = brand.Display == "logo" ? SiteExport.Escape(brand.Name) : "";
        return $"<img class=\"{cls}\" src=\"{SiteExport.Escape(Href(currentPath, path))}\" alt=\"{alt}\"{size} />";
    }

    /// <summary>A site path made relative to the current page, or a data: URI left alone.</summary>
    public static string Href(string currentPath, string path) =>
        path.StartsWith("data:", StringComparison.Ordinal) ? path : SiteExport.Relative(currentPath, path);

    /// <summary>
    /// Everything an exported document's head needs from the instance: the
    /// image rule, the favicon and the custom accent. Everything else in the
    /// head is the exporter's own.
    /// </summary>
    public static string HeadExtras(Brand brand, string currentPath)
    {
        var html = brand.ImagePolicyMeta;
        if (brand.FaviconHref is { } icon)
            html += $"<link rel=\"icon\" type=\"{brand.FaviconType}\" href=\"{SiteExport.Escape(Href(currentPath, icon))}\" />";
        if (brand.AccentCss.Length > 0)
            html += $"<style id=\"brand-accent\">{brand.AccentCss}</style>";
        return html;
    }

    /// <summary>
    /// Brings a captured document into line with the brand, whatever the
    /// capture happened to carry: its title, its favicon, its accent and its
    /// theme locks. The capture's page shell had the instance's attributes
    /// and style block in it, and an icon link pointing at the instance,
    /// which an exported file cannot reach. Those are taken out and the
    /// export's own put in, so there is exactly one of each.
    /// </summary>
    public static string ApplyToDocument(string html, Brand brand, string title, string currentPath)
    {
        html = BrandStyle().Replace(html, "");
        html = IconLinks().Replace(html, "");
        html = TitleTag().Replace(html, $"<title>{SiteExport.Escape(title)}</title>", 1);
        html = HtmlTag().Replace(html, m =>
        {
            var tag = BrandAttribute().Replace(m.Value, "");
            var extra = string.Concat(brand.Attributes.Select(a => $" {a.Name}=\"{SiteExport.Escape(a.Value)}\""));
            return tag[..^1] + extra + ">";
        }, 1);
        return html.Replace("</head>", HeadExtras(brand, currentPath) + "</head>");
    }

    /// <summary>The opening tag of the root element, with the attributes an export's theme script reads.</summary>
    public static string HtmlOpen(Brand brand) =>
        "<html lang=\"en\"" + string.Concat(brand.Attributes.Select(a => $" {a.Name}=\"{SiteExport.Escape(a.Value)}\"")) + ">";

    [System.Text.RegularExpressions.GeneratedRegex("<style id=\"brand-accent\">[\\s\\S]*?</style>")]
    private static partial System.Text.RegularExpressions.Regex BrandStyle();

    [System.Text.RegularExpressions.GeneratedRegex("<link\\s+rel=\"(?:icon|apple-touch-icon)\"[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex IconLinks();

    [System.Text.RegularExpressions.GeneratedRegex("<title>[^<]*</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex TitleTag();

    [System.Text.RegularExpressions.GeneratedRegex("<html\\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex HtmlTag();

    /// <summary>What the capture's shell and the capture browser put on the root element, and nothing else.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("\\s(?:data-theme|data-accent|data-theme-lock|data-accent-lock|data-accent-default|data-style|data-style-default|data-motion|data-os|data-sb|data-brand-[a-z-]+)=\"[^\"]*\"")]
    private static partial System.Text.RegularExpressions.Regex BrandAttribute();

    /// <summary>
    /// Switches the page between its normal width and full width, the same
    /// control the reading view has in its action bar. An export has no action
    /// bar, so it sits in the top bar beside the appearance menu.
    ///
    /// <para>The page opens at whatever width it was given in the wiki; this
    /// is the reader's override, and like the theme it is theirs and sticks
    /// across the pages of a site.</para>
    /// </summary>
    /// <remarks>
    /// Both labels ship and the script shows one, because a captured export
    /// has no React left to re-render the text. <c>data-width-label</c> names
    /// the state the label belongs to, while the text names what clicking
    /// does, which is the same inversion the reading view's button has: while
    /// the page is full width the button offers "Normal Width".
    /// </remarks>
    private static string WidthToggle() =>
        """
        <button type="button" class="btn btn--ghost btn--sm" data-export-width-toggle>
        <span data-width-label="normal">&#10530; Full Width</span>
        <span data-width-label="full" style="display: none">&#10529; Normal Width</span>
        </button>
        """;

    /// <summary>
    /// What goes before each page in a numbered or bulleted tree (dev-plan
    /// 15.8), from the pages' depths in tree order: outline numbers (1, 1.1,
    /// 1.2, 2) like a numbered table of contents, or a bullet that changes
    /// with the level. The same rule as treeMarkers.ts in the app, so an
    /// exported site numbers its pages as the wiki does.
    /// </summary>
    public static IReadOnlyList<string?> TreeMarkers(IReadOnlyList<int> depths, SpaceTreeStyle style)
    {
        var result = new string?[depths.Count];
        if (style == SpaceTreeStyle.Plain) return result;
        var counters = new List<int>();
        for (var i = 0; i < depths.Count; i++)
        {
            var depth = depths[i];
            while (counters.Count <= depth) counters.Add(0);
            counters[depth]++;
            counters.RemoveRange(depth + 1, counters.Count - depth - 1);
            result[i] = style == SpaceTreeStyle.Numbered
                ? string.Join('.', counters.Take(depth + 1))
                : Bullets[depth % Bullets.Length];
        }
        return result;
    }

    private static readonly string[] Bullets = ["•", "◦", "▪"];

    /// <summary>The filter's "and the pages under it" toggle; the same drawing as ChildrenIcon in PageTree.tsx.</summary>
    /// <summary>The filter's clear button, the app's own (PageTree.tsx).</summary>
    private const string ClearIcon = """<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>""";
    private const string ChildrenIcon = """<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 4h9" /><path d="M8 4v12a2 2 0 0 0 2 2h1" /><path d="M8 10h3" /><path d="M14 10h5" /><path d="M14 18h5" /></svg>""";

    /// <summary>
    /// The space sidebar: the icon, key and name, then the page tree under a
    /// "Pages" heading. The same three bands the application shows, minus the
    /// parts that only mean something signed in (+ New page, Space settings),
    /// which is also what a reader of a public space sees.
    ///
    /// <para>It hides and shows as the app's does (2026-09-29): the button
    /// at the head's right hides it, leaving the rail's button that brings
    /// it back, the same markup as <c>SpacePage.tsx</c>, and in Glass the
    /// same motion (<see cref="ThemeScript"/>). The rail ships hidden; the
    /// small script after the panel applies the reader's stored choice
    /// before the page is first drawn, as the app's own key
    /// (<c>tesria-sidebar-collapsed</c>) says. On a phone the Pages button
    /// takes over and neither button shows.</para>
    /// </summary>
    public static string Sidebar(SpaceHead space, IReadOnlyList<SiteExport.Placed> pages, string currentPath)
    {
        var badge = space.IsPublic
            ? """<span class="badge badge--public" title="Readable by anyone on the internet">public</span>"""
            : "";
        return $"""
        <div class="sidebar-rail" data-sidebar-rail style="display: none"><button type="button" class="sidebar__toggle" data-sidebar-toggle title="Show the Sidebar" aria-label="Show the Sidebar" aria-expanded="false">{SidebarIcon}</button></div>
        <aside class="sidebar" id="site-pages">
        <div class="sidebar__top"><div class="sidebar__head">{SpaceIcon(space, 32, currentPath)}<div>
        <div class="sidebar__key">{SiteExport.Escape(space.Key)}{badge}</div>
        <div class="sidebar__name">{SiteExport.Escape(space.Name)}</div>
        </div><button type="button" class="sidebar__toggle sidebar__toggle--hide" data-sidebar-toggle title="Hide the Sidebar" aria-label="Hide the Sidebar" aria-expanded="true">{SidebarIcon}</button></div></div>
        <div class="tree-section">
        <div class="tree-section__heading"><span>{PagesIcon} Pages</span></div>
        <div class="tree-filter"><span class="tree-filter__field"><input type="search" class="tree-filter__input" placeholder="Filter pages" aria-label="Filter pages" /><button type="button" class="tree-filter__clear" aria-label="Clear the Filter" title="Clear the Filter" style="display:none">{ClearIcon}</button></span><button type="button" class="tree-filter__children is-on" aria-pressed="true" aria-label="Show the Pages Under Each Match" title="Show the Pages Under Each Match">{ChildrenIcon}</button></div>
        {Tree(pages, currentPath, space.TreeStyle)}
        <p class="muted small tree-filter__none" style="display:none">No pages match.</p>
        </div>
        </aside>
        <script data-export-keep>window.__tesriaSidebar && window.__tesriaSidebar()</script>
        """;
    }

    /// <summary>From <c>NavIcons.tsx</c>'s SidebarIcon: a panel with its left side marked.</summary>
    private static readonly string SidebarIcon = Svg(16,
        """<rect x="3.5" y="4.5" width="17" height="15" rx="2" /><path d="M9 4.5v15" />""")
        .Replace("<svg ", "<svg class=\"nav-icon\" focusable=\"false\" ");

    /// <summary>
    /// The page tree, flattened with the same indentation the application
    /// uses (8px plus 14px a level, set inline there and so inline here).
    /// </summary>
    public static string Tree(IReadOnlyList<SiteExport.Placed> pages, string currentPath, SpaceTreeStyle style = SpaceTreeStyle.Plain)
    {
        if (pages.Count == 0) return """<p class="muted small">No pages yet.</p>""";
        var markers = TreeMarkers(pages.Select(p => p.Depth).ToList(), style);
        var sb = new StringBuilder("""<nav class="tree">""");
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var current = page.Path == currentPath;
            var cls = current ? "tree__link is-active" : "tree__link";
            var aria = current ? " aria-current=\"page\"" : "";
            sb.Append($"<a class=\"{cls}\"{aria} data-depth=\"{page.Depth}\" style=\"padding-left: {8 + page.Depth * 14}px\" ")
              .Append($"href=\"{SiteExport.Relative(currentPath, page.Path)}\">")
              // The same markup as the app's tree (dev-plan 15.7, 15.8).
              .Append(markers[i] is { } marker ? $"<span class=\"tree__marker\" aria-hidden=\"true\">{marker}</span>" : "")
              .Append(page.Emoji is null ? "" : $"<span class=\"tree__emoji\" aria-hidden=\"true\">{SiteExport.Escape(page.Emoji)}</span>")
              .Append($"<span class=\"tree__title\">{SiteExport.Escape(page.Title)}</span></a>");
        }
        return sb.Append("</nav>").ToString();
    }

    /// <summary>
    /// The front page's contents (2026-09-24: a site's front page
    /// with only a name and a description "seems blank"): each top-level page
    /// and the pages directly under it, linked, numbered as the sidebar
    /// numbers them. Two levels, so a large space stays a page, not a list.
    /// </summary>
    public static string Contents(IReadOnlyList<SiteExport.Placed> pages, SpaceTreeStyle style = SpaceTreeStyle.Plain)
    {
        if (pages.Count == 0) return "";
        var markers = TreeMarkers(pages.Select(p => p.Depth).ToList(), style);
        string Link(int i, string cls) =>
            $"<a class=\"{cls}\" href=\"{SiteExport.Relative("", pages[i].Path)}\">"
            + (markers[i] is { } m ? $"<span class=\"tree__marker\" aria-hidden=\"true\">{m}</span>" : "")
            + (pages[i].Emoji is { } e ? $"<span class=\"tree__emoji\" aria-hidden=\"true\">{SiteExport.Escape(e)}</span>" : "")
            + $"<span>{SiteExport.Escape(pages[i].Title)}</span></a>";

        var sb = new StringBuilder("<nav class=\"site-contents\" aria-label=\"Contents\"><h2 class=\"site-contents__heading\">Contents</h2><ul class=\"site-contents__sections\">");
        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i].Depth != 0) continue;
            sb.Append("<li class=\"site-contents__section\">").Append(Link(i, "site-contents__title"));
            var children = new StringBuilder();
            for (var j = i + 1; j < pages.Count && pages[j].Depth > 0; j++)
                if (pages[j].Depth == 1) children.Append("<li>").Append(Link(j, "site-contents__page")).Append("</li>");
            if (children.Length > 0) sb.Append("<ul class=\"site-contents__pages\">").Append(children).Append("</ul>");
            sb.Append("</li>");
        }
        return sb.Append("</ul></nav>").ToString();
    }

    /// <summary>
    /// A space's icon: an uploaded picture, a chosen emoji, or the key's first
    /// letter on a colored tile. Mirrors <c>SpaceIcon.tsx</c>, including its
    /// rounded-square radius, so the sidebar shows the same tile the
    /// application does.
    /// </summary>
    private static string SpaceIcon(SpaceHead space, int size, string currentPath)
    {
        var radius = (int)Math.Round(size * 0.22);

        if (space.IconKind == SpaceIconKind.Image && space.IconPath is not null)
            // Relative to the page: a site is files, and `assets/…` from two
            // directories down is not the same file.
            return $"<img class=\"space-icon\" src=\"{SiteExport.Escape(SiteExport.Relative(currentPath, space.IconPath))}\" width=\"{size}\" height=\"{size}\" "
                 + $"style=\"border-radius: {radius}px\" alt=\"\" aria-hidden=\"true\" />";

        if (space.IconKind == SpaceIconKind.Emoji && !string.IsNullOrEmpty(space.IconValue))
            return $"<span class=\"space-icon space-icon--emoji\" aria-hidden=\"true\" "
                 + $"style=\"width: {size}px; height: {size}px; border-radius: {radius}px; font-size: {(int)Math.Round(size * 0.62)}px\">"
                 + $"{SiteExport.Escape(space.IconValue)}</span>";

        // In an export the generated tile is the theme's accent, where in the
        // application it is one of twelve colors picked per space. The owner's
        // reasoning (2026-09-20): those colors exist to tell spaces apart in a
        // list, and an export is one space by definition, so the color carries
        // no information there and may as well look like the rest of the
        // product. It follows the reader's accent and light/dark with it, which
        // is why these are the tokens rather than the hex they resolve to;
        // --on-primary is the letter's color for the same reason a filled
        // button uses it, being the one already tuned for contrast on --primary
        // in each theme.
        var initial = char.ToUpperInvariant(space.Key.Length > 0 ? space.Key[0] : '?');
        return $"<svg class=\"space-icon\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 40 40\" role=\"img\" aria-hidden=\"true\" focusable=\"false\">"
             + "<rect width=\"40\" height=\"40\" rx=\"9\" fill=\"var(--primary)\" />"
             + "<text x=\"20\" y=\"20\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"var(--on-primary)\" "
             + $"font-size=\"19\" font-weight=\"700\" font-family=\"inherit\">{SiteExport.Escape(initial.ToString())}</text></svg>";
    }

    /* ---- icons ----------------------------------------------------------- */

    private static string Svg(int size, string body, double stroke = 1.8) =>
        $"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
        + $"stroke-width=\"{stroke.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" "
        + $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{body}</svg>";

    /// <summary>
    /// Tesria's mark, from <c>BrandMark.tsx</c>: four layers in their fixed
    /// colors (docs/brand), whose per-layer strokes override the currentColor
    /// the shared Svg helper sets.
    /// </summary>
    private static readonly string BrandMark = Svg(20,
        """<path d="M4 17.25l8 4 8-4" stroke="var(--tesria-automate)" /><path d="M4 13.75l8 4 8-4" stroke="var(--tesria-share)" /><path d="M4 10.25l8 4 8-4" stroke="var(--tesria-keep)" /><path d="M12 2.75l8 4-8 4-8-4z" stroke="var(--tesria-write)" />""", stroke: 2);

    /// <summary>From <c>NavIcons.tsx</c>, which draws these at 16px.</summary>
    private static readonly string PagesIcon = Svg(16,
        """<path d="M13.5 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8.5L13.5 3z" />"""
        + """<path d="M13.5 3v4a1.5 1.5 0 0 0 1.5 1.5h4" /><path d="M8.75 13h6.5M8.75 16.75h4.5" />""")
        .Replace("<svg ", "<svg class=\"nav-icon\" focusable=\"false\" ");

    /* ---- the appearance menu --------------------------------------------- */

    private static readonly (string Mode, string Label, string Hint, string Icon)[] Modes =
    [
        ("system", "System", "Follows your operating system", Svg(17,
            """<rect x="2.75" y="4" width="18.5" height="13" rx="2" /><path d="M9 20.5h6M12 17v3.5" />""")),
        ("light", "Light", "Always light", Svg(17,
            """<circle cx="12" cy="12" r="4.25" /><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.2 5.2l1.4 1.4M17.4 17.4l1.4 1.4M18.8 5.2l-1.4 1.4M6.6 17.4l-1.4 1.4" />""")),
        ("dark", "Dark", "Always dark", Svg(17,
            """<path d="M20 13.5A8.5 8.5 0 1 1 10.5 4a6.75 6.75 0 0 0 9.5 9.5Z" />""")),
    ];

    /// <summary>The five accents, from <c>theme.ts</c> (teal retired 2026-09-26).</summary>
    private static readonly (string Name, string Label)[] Accents =
    [
        ("blue", "Blue"), ("green", "Green"),
        ("purple", "Purple"), ("orange", "Orange"), ("magenta", "Magenta"),
    ];

    /// <summary>
    /// The appearance menu, markup-identical to <c>ThemeToggle.tsx</c>. The
    /// React component's behavior cannot survive a capture (every script but
    /// the theme script is stripped), so <see cref="ThemeScript"/> drives this
    /// markup instead, through the <c>data-theme-*</c> hooks.
    /// </summary>
    private static string ThemeMenu(Brand brand)
    {
        // Locked parts are not offered, and when both are locked there is no
        // menu at all: a control that cannot change anything is noise.
        var themeLocked = brand.ThemeLock is not null;
        var accentLocked = brand.AccentLock is not null;
        // The Style is never locked, so there is always a menu.

        var triggerIcons = string.Concat(Modes.Select(m =>
            $"<span data-theme-icon=\"{m.Mode}\" style=\"display: none\">{TriggerIcon(m.Mode)}</span>"));

        var modes = string.Concat(Modes.Select(m =>
            $"<button type=\"button\" class=\"theme-menu__mode\" data-theme-mode=\"{m.Mode}\" aria-pressed=\"false\">"
            + m.Icon
            + "<span class=\"theme-menu__mode-text\">"
            + $"<span class=\"theme-menu__mode-name\">{m.Label}</span>"
            + $"<span class=\"theme-menu__mode-hint\"{(m.Mode == "system" ? " data-theme-syshint" : "")}>{m.Hint}</span>"
            + "</span>"
            + $"<span class=\"theme-menu__check\" style=\"display: none\">{CheckIcon}</span></button>"));

        // The brand's own accent comes first under the brand's name, as it
        // does in the application's menu.
        var swatches = (brand.HasBrandAccent ? [("brand", brand.Name)] : Array.Empty<(string, string)>())
            .Concat(Accents);
        var accents = string.Concat(swatches.Select(a =>
            $"<button type=\"button\" class=\"theme-menu__accent\" data-theme-accent=\"{a.Item1}\" "
            + $"style=\"background: var(--accent-dot-{a.Item1})\" title=\"{SiteExport.Escape(a.Item2)}\" aria-label=\"{SiteExport.Escape(a.Item2)}\" aria-pressed=\"false\"></button>"));
        var themeSection = themeLocked ? "" : $"""
        <p class="theme-menu__heading">Theme</p>
        <div class="theme-menu__modes">{modes}</div>
        """;
        // Minimal or Glass (0.8.1), as in the app's menu, with Reduce Motion
        // shown under it only while Glass is chosen (the script shows it).
        // It stills what Glass moves in a site as in the app: this menu
        // opening out of its button and the sidebar folding into its own
        // (2026-09-29; before that a site had neither, and the switch did
        // nothing).
        var styles = string.Concat(Styles.Select(st =>
            $"<button type=\"button\" class=\"theme-menu__mode\" data-theme-style=\"{st.Value}\" aria-pressed=\"false\">"
            + "<span class=\"theme-menu__mode-text\">"
            + $"<span class=\"theme-menu__mode-name\">{st.Label}</span>"
            + $"<span class=\"theme-menu__mode-hint\">{st.Hint}</span>"
            + "</span>"
            + $"<span class=\"theme-menu__check\" style=\"display: none\">{CheckIcon}</span></button>"));
        var styleSection = $"""
        <p class="theme-menu__heading">Style</p>
        <div class="theme-menu__modes">{styles}<button type="button" role="switch" aria-checked="false" class="theme-menu__switch" data-theme-motion style="display: none"><span class="theme-menu__mode-text"><span class="theme-menu__mode-name">Reduce Motion</span><span class="theme-menu__mode-hint">No animations when panels open and close</span></span><span class="switch" aria-hidden="true"><span class="switch__knob"></span></span></button></div>
        """;
        var accentSection = accentLocked ? "" : $"""
        <p class="theme-menu__heading">Accent Color</p>
        <div class="theme-menu__accents">{accents}</div>
        """;

        return $"""
        <div class="theme-menu">
        <button type="button" class="theme-toggle" data-theme-trigger aria-haspopup="true" aria-expanded="false" title="Appearance" aria-label="Appearance">{triggerIcons}</button>
        <div class="theme-menu__panel" role="dialog" aria-label="Appearance" data-theme-panel hidden>
        <button type="button" class="popover__close" aria-label="Close" data-theme-close>{CloseIcon}</button>
        {themeSection}{styleSection}{accentSection}
        </div></div>
        """;
    }

    /// <summary>
    /// The trigger shows what is in force, not what is chosen: on "system"
    /// that is whichever the operating system currently resolves to, which is
    /// why the script swaps between these rather than showing one.
    /// </summary>
    /// <summary>The two styles, labeled as in <c>theme.ts</c>'s STYLES; "flat" is Minimal's stored name.</summary>
    private static readonly (string Value, string Label, string Hint)[] Styles =
    [
        ("flat", "Minimal", "Solid surfaces, as Tesria has always looked"),
        ("glass", "Glass", "Frosted bars and buttons, like tesria.com"),
    ];

    private static string TriggerIcon(string mode) => Modes.First(m => m.Mode == mode).Icon.Replace("width=\"17\" height=\"17\"", "width=\"19\" height=\"19\"");

    private static readonly string CheckIcon = Svg(15, """<path d="m5 12.5 4.5 4.5L19 7.5" />""");

    private static readonly string CloseIcon = Svg(18, """<path d="M6 6l12 12M18 6L6 18" />""", stroke: 2);

    /// <summary>
    /// The one script an export carries. It applies the stored theme and
    /// accent before first paint (without it the file renders light for a
    /// frame and then flips), then drives the appearance menu, the
    /// full-width toggle, animations, Expand blocks, code blocks' Copy,
    /// hiding the sidebar and the sidebar's page filter, with Glass's
    /// motion for the menu and the sidebar.
    /// Marked <c>data-export-keep</c>, which is how the capture knows to keep
    /// it when it strips the application's own scripts.
    ///
    /// <para>The storage keys and the attribute logic are the same ones
    /// <c>theme.ts</c> and <c>index.html</c> use, so a reader's choice in an
    /// exported site and their choice in the app do not fight; they are
    /// different origins, so they are simply independent.</para>
    ///
    /// <para><c>style.display</c> rather than the <c>hidden</c> attribute
    /// throughout: <c>.theme-menu__check</c> sets <c>display</c> in the
    /// stylesheet, which beats <c>[hidden]</c>, and a half-working toggle is
    /// worse than none.</para>
    /// </summary>
    public static string ThemeScript() =>
        """
        <script data-export-keep>
        (function () {
          var d = document, root = d.documentElement;
          var THEME = 'tesria-theme', ACCENT = 'tesria-accent', DEFAULT_ACCENT = 'blue';
          // The instance's branding at export time (dev-plan 13.1): a theme
          // or accent held for everyone, and the accent a new reader gets.
          var THEME_LOCK = root.getAttribute('data-theme-lock');
          var ACCENT_LOCK = root.getAttribute('data-accent-lock');
          var ACCENT_DEFAULT = root.getAttribute('data-accent-default') || DEFAULT_ACCENT;
          var WIDTH = 'tesria-export-width';
          function get(k) { try { return localStorage.getItem(k) } catch (e) { return null } }
          function set(k, v) { try { v === null ? localStorage.removeItem(k) : localStorage.setItem(k, v) } catch (e) {} }

          // Before first paint.
          var stored = THEME_LOCK || get(THEME);
          if (stored === 'light' || stored === 'dark') root.setAttribute('data-theme', stored);
          var startAccent = ACCENT_LOCK || get(ACCENT) || ACCENT_DEFAULT;
          if (startAccent !== DEFAULT_ACCENT) root.setAttribute('data-accent', startAccent);
          // Minimal or Glass (0.8.1): the reader's choice, else the look the
          // site was exported in (data-style-default), else Minimal. Stored
          // as the app stores it ('glass', or 'flat' for Minimal), except
          // that Minimal is stored too: here it can be a choice against a
          // Glass default, not only the absence of one.
          var STYLE = 'tesria-style', MOTION = 'tesria-reduce-motion';
          var STYLE_DEFAULT = root.getAttribute('data-style-default') === 'glass' ? 'glass' : 'flat';
          function styleNow() { var v = get(STYLE); return v === 'glass' || v === 'flat' ? v : STYLE_DEFAULT }
          function applyStyle(v) { if (v === 'glass') root.setAttribute('data-style', 'glass'); else root.removeAttribute('data-style') }
          function applyMotion(on) { if (on) root.setAttribute('data-motion', 'reduce'); else root.removeAttribute('data-motion') }
          applyStyle(styleNow());
          applyMotion(get(MOTION) === '1');

          // Glass's motion (2026-09-29), as the app has it: the appearance
          // menu opens out of its button and closes back into it
          // (popoverMotion.ts), and the sidebar folds into its show button
          // and unfolds from it (SpacePage.tsx). The keyframes, durations and
          // easings are copied from those two files (SiteExportTests checks
          // they still match). Minimal switches instantly, and so does
          // anyone who asks for reduced motion: the switch in the menu, or
          // the system (motionReduced() in theme.ts).
          function moving() {
            return root.getAttribute('data-style') === 'glass'
              && typeof Element.prototype.animate === 'function'
              && root.getAttribute('data-motion') !== 'reduce'
              && !matchMedia('(prefers-reduced-motion: reduce)').matches;
          }

          // The sidebar, hidden or shown: the app's own key, so the choice
          // follows the reader from page to page as the theme does.
          var SIDEBAR = 'tesria-sidebar-collapsed';
          function sidebarParts() {
            var panel = d.getElementById('site-pages');
            return panel && {
              panel: panel, layout: panel.parentElement,
              rail: d.querySelector('[data-sidebar-rail]'),
            };
          }
          function applySidebar(collapsed) {
            var s = sidebarParts();
            if (!s) return;
            s.layout.classList.toggle('space-layout--collapsed', collapsed);
            if (collapsed) s.panel.setAttribute('hidden', ''); else s.panel.removeAttribute('hidden');
            if (s.rail) s.rail.style.display = collapsed ? '' : 'none';
          }
          // Called from just after the sidebar's markup, so a hidden sidebar
          // is never drawn first.
          window.__tesriaSidebar = function () { applySidebar(get(SIDEBAR) === '1') };
          // Windows draws Segoe UI low in a button (index.css nudges it up).
          if (/Windows/.test(navigator.userAgent)) root.setAttribute('data-os', 'windows');

          // Width is a class on an element rather than an attribute on <html>,
          // so it cannot be applied before the body exists. It is applied in
          // syncWidth() below, which runs as soon as the DOM is ready.
          function widthEl() { return d.querySelector('[data-export-width]') }

          function syncWidth() {
            var el = widthEl();
            if (!el) return;
            var stored = get(WIDTH);
            // No stored choice means the page keeps the width it was given in
            // the wiki, which is the class already on it.
            if (stored === 'full') el.classList.add('page-wrap--full');
            else if (stored === 'normal') el.classList.remove('page-wrap--full');
            var full = el.classList.contains('page-wrap--full');
            d.querySelectorAll('[data-width-label]').forEach(function (label) {
              label.style.display =
                label.getAttribute('data-width-label') === (full ? 'full' : 'normal') ? '' : 'none';
            });
          }

          function systemTheme() { return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light' }
          function mode() { if (THEME_LOCK) return THEME_LOCK; var v = get(THEME); return (v === 'light' || v === 'dark') ? v : 'system' }
          function accent() { return ACCENT_LOCK || get(ACCENT) || ACCENT_DEFAULT }

          function applyTheme(next) {
            if (next === 'system') root.removeAttribute('data-theme');
            else root.setAttribute('data-theme', next);
          }
          function applyAccent(next) {
            if (next === DEFAULT_ACCENT) root.removeAttribute('data-accent');
            else root.setAttribute('data-accent', next);
          }

          function show(el, on) { if (el) el.style.display = on ? '' : 'none' }

          function sync() {
            var m = mode(), a = accent(), sys = systemTheme();
            d.querySelectorAll('[data-theme-mode]').forEach(function (b) {
              var on = b.getAttribute('data-theme-mode') === m;
              b.classList.toggle('is-active', on);
              b.setAttribute('aria-pressed', on ? 'true' : 'false');
              show(b.querySelector('.theme-menu__check'), on);
            });
            d.querySelectorAll('[data-theme-accent]').forEach(function (b) {
              var on = b.getAttribute('data-theme-accent') === a;
              b.classList.toggle('is-active', on);
              b.setAttribute('aria-pressed', on ? 'true' : 'false');
            });
            var st = styleNow(), reduce = get(MOTION) === '1';
            d.querySelectorAll('[data-theme-style]').forEach(function (b) {
              var on = b.getAttribute('data-theme-style') === st;
              b.classList.toggle('is-active', on);
              b.setAttribute('aria-pressed', on ? 'true' : 'false');
              show(b.querySelector('.theme-menu__check'), on);
            });
            d.querySelectorAll('[data-theme-motion]').forEach(function (b) {
              show(b, st === 'glass');
              b.setAttribute('aria-checked', reduce ? 'true' : 'false');
              var sw = b.querySelector('.switch');
              if (sw) sw.classList.toggle('is-on', reduce);
            });
            d.querySelectorAll('[data-theme-icon]').forEach(function (i) {
              show(i, i.getAttribute('data-theme-icon') === m);
            });
            d.querySelectorAll('[data-theme-syshint]').forEach(function (s) {
              s.textContent = 'Follows your operating system (' + sys + ')';
            });
            var trigger = d.querySelector('[data-theme-trigger]');
            if (trigger) {
              var label = m === 'system' ? 'Appearance: system (currently ' + sys + ')' : 'Appearance: ' + m;
              trigger.title = label;
              trigger.setAttribute('aria-label', label);
            }
          }

          // The appearance menu, opened and closed. In Glass, closing draws
          // the panel's far sides in until only a button-sized circle is left
          // under the button, which slides up onto it as it fades; opening
          // runs that backwards and settles with a slight overshoot
          // (popoverMotion.ts). The panel stays until the closing motion ends.
          var PANEL_CLIP = 'inset(0 0 0 0 round 14px)', closing = null;
          function toButton(panel) {
            var button = panel.previousElementSibling;
            if (!button) return null;
            var p = panel.getBoundingClientRect(), b = button.getBoundingClientRect();
            var r = b.height / 2;
            var cx = Math.min(Math.max(b.left + b.width / 2 - p.left, r), p.width - r);
            return {
              clip: 'inset(0 ' + (p.width - cx - r) + 'px ' + (p.height - 2 * r) + 'px ' + (cx - r) + 'px round ' + r + 'px)',
              lift: p.top - b.top,
            };
          }
          function menuOpen() {
            var panel = d.querySelector('[data-theme-panel]');
            return !!panel && !panel.hidden && !closing;
          }
          function open() {
            var panel = d.querySelector('[data-theme-panel]'), trigger = d.querySelector('[data-theme-trigger]');
            if (!panel) return;
            if (closing) { closing.cancel(); closing = null }
            panel.hidden = false;
            if (trigger) { trigger.setAttribute('aria-expanded', 'true'); trigger.classList.add('is-open') }
            var to = moving() ? toButton(panel) : null;
            if (!to) return;
            panel.animate([
              { clipPath: to.clip, transform: 'translateY(' + (-to.lift) + 'px)', opacity: 0.2 },
              { clipPath: to.clip, transform: 'none', opacity: 1, offset: 0.25 },
              { clipPath: PANEL_CLIP, transform: 'scale(1.01)', offset: 0.8 },
              { clipPath: PANEL_CLIP, transform: 'none', opacity: 1 },
            ], { duration: 380, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' });
          }
          function close() {
            var panel = d.querySelector('[data-theme-panel]'), trigger = d.querySelector('[data-theme-trigger]');
            if (trigger) { trigger.setAttribute('aria-expanded', 'false'); trigger.classList.remove('is-open') }
            if (!panel || panel.hidden || closing) return;
            var to = moving() ? toButton(panel) : null;
            if (!to) { panel.hidden = true; return }
            var a = panel.animate([
              { clipPath: PANEL_CLIP, transform: 'none', opacity: 1 },
              { clipPath: to.clip, transform: 'none', opacity: 1, offset: 0.7 },
              { clipPath: to.clip, transform: 'translateY(' + (-to.lift) + 'px)', opacity: 0 },
            ], { duration: 300, easing: 'cubic-bezier(0.55, 0, 0.6, 1)', fill: 'forwards' });
            closing = a;
            // A browser can slow or pause animations (a background tab), and
            // then the panel would linger: it goes by the clock too.
            function done() { if (closing !== a) return; closing = null; panel.hidden = true; a.cancel() }
            a.onfinish = done;
            setTimeout(done, 450);
          }

          // Kept for the markup shipped before the menu existed, and because
          // it is a reasonable thing for someone to call from their own page.
          window.__tesriaTheme = function (next) {
            set(THEME, next === 'system' ? null : next);
            applyTheme(next);
            sync();
          };

          function wire() {
            var trigger = d.querySelector('[data-theme-trigger]'), panel = d.querySelector('[data-theme-panel]');
            if (trigger && panel) {
              trigger.addEventListener('click', function (e) {
                e.stopPropagation();
                if (menuOpen()) close(); else open();
              });
              d.addEventListener('click', function (e) {
                if (menuOpen() && !panel.contains(e.target) && !trigger.contains(e.target)) close();
              });
              d.addEventListener('keydown', function (e) { if (e.key === 'Escape') close() });
            }
            d.querySelectorAll('[data-export-width-toggle]').forEach(function (b) {
              b.addEventListener('click', function () {
                var el = widthEl();
                if (!el) return;
                set(WIDTH, el.classList.contains('page-wrap--full') ? 'normal' : 'full');
                syncWidth();
              });
            });
            d.querySelectorAll('[data-theme-close]').forEach(function (b) { b.addEventListener('click', close) });
            d.querySelectorAll('[data-theme-mode]').forEach(function (b) {
              b.addEventListener('click', function () {
                var next = b.getAttribute('data-theme-mode');
                set(THEME, next === 'system' ? null : next);
                applyTheme(next);
                sync();
              });
            });
            d.querySelectorAll('[data-theme-accent]').forEach(function (b) {
              b.addEventListener('click', function () {
                var next = b.getAttribute('data-theme-accent');
                // Stored even when it is blue: with a brand default, "blue"
                // is a choice, not the absence of one.
                set(ACCENT, next);
                applyAccent(next);
                sync();
              });
            });
            d.querySelectorAll('[data-theme-style]').forEach(function (b) {
              b.addEventListener('click', function () {
                var next = b.getAttribute('data-theme-style');
                set(STYLE, next);
                applyStyle(next);
                sync();
              });
            });
            d.querySelectorAll('[data-theme-motion]').forEach(function (b) {
              b.addEventListener('click', function () {
                var on = get(MOTION) !== '1';
                set(MOTION, on ? '1' : null);
                applyMotion(on);
                sync();
              });
            });
            // The trigger and the "system" hint both show what the OS is
            // resolving to, so both have to follow it changing.
            matchMedia('(prefers-color-scheme: dark)').addEventListener('change', sync);
            sync();
            syncWidth();
            wireAnimations();
            wireExpands();
            wireCopy();
            wireTreeFilter();
            wireTreeScroll();
            wireSidebar();
            wireSiteMenu();
            wireScrollbars();
          }

          // Hiding and showing the sidebar (SpacePage.tsx). In Glass, hiding
          // draws the panel's right and bottom edges in to its top-left
          // corner, where the show button appears, leaving a circle the
          // button's size (a clip, so the corner keeps its shape rather than
          // being squashed); the button then bounces in while the page's
          // column slides over. Showing opens the panel back out of it.
          function wireSidebar() {
            var s = sidebarParts();
            if (!s) return;
            applySidebar(get(SIDEBAR) === '1');
            var SIDE_CLIP = 'inset(0 0 0 0 round 18px)', shrink = null, timer = 0;
            function clipToButton(panel) {
              var r = panel.getBoundingClientRect();
              var size = parseFloat(getComputedStyle(root).getPropertyValue('--ctl-page')) || 38;
              return 'inset(0 ' + (r.width - size) + 'px ' + (r.height - size) + 'px 0 round ' + (size / 2) + 'px)';
            }
            function flip(collapsed) {
              set(SIDEBAR, collapsed ? '1' : null);
              applySidebar(collapsed);
            }
            function hide() {
              if (shrink) return;
              if (!moving()) { flip(true); return }
              var a = s.panel.animate([
                // The contents fade in the last stretch, so the circle hands
                // over to the button, not a sliver of the space's icon.
                { clipPath: SIDE_CLIP, opacity: 1 },
                { opacity: 1, offset: 0.55 },
                { clipPath: clipToButton(s.panel), opacity: 0.1 },
              ], { duration: 260, easing: 'cubic-bezier(0.55, 0, 0.8, 0.2)', fill: 'forwards' });
              shrink = a;
              a.onfinish = function () {
                shrink = null;
                s.layout.classList.add('is-moving');
                clearTimeout(timer);
                timer = setTimeout(function () { s.layout.classList.remove('is-moving') }, 340);
                flip(true);
                a.cancel();
                var b = s.rail && s.rail.querySelector('[data-sidebar-toggle]');
                if (b) b.animate([
                  // It takes over from the panel's last circle at the same size.
                  { transform: 'scale(1)', opacity: 0.2 },
                  { transform: 'scale(1.14)', opacity: 1, offset: 0.45 },
                  { transform: 'scale(0.95)', offset: 0.75 },
                  { transform: 'scale(1)' },
                ], { duration: 440, easing: 'ease-out' });
              };
            }
            function show() {
              flip(false);
              placeTree();
              if (!moving()) return;
              s.panel.animate([
                { clipPath: clipToButton(s.panel), transform: 'none' },
                { clipPath: SIDE_CLIP, transform: 'scale(1.012)', offset: 0.78 },
                { clipPath: SIDE_CLIP, transform: 'none' },
              ], { duration: 420, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' });
            }
            d.querySelectorAll('[data-sidebar-toggle]').forEach(function (b) {
              b.addEventListener('click', function () {
                if (s.panel.hasAttribute('hidden')) show(); else hide();
              });
            });
          }

          // Glass (0.8.1): a scroller's thin scrollbar shows while the mouse
          // is in it or it scrolls (data-sb), as scrollbars.ts does in the
          // app; flipping overflow for one layout makes Chrome repaint it.
          // A touch screen draws its own, so this is for a mouse only.
          function wireScrollbars() {
            var fine = matchMedia('(hover: hover) and (pointer: fine)');
            var hovered = [], timers = new Map();
            function active() { return fine.matches && root.getAttribute('data-style') === 'glass' }
            function refresh(el) { el.classList.add('sb-refresh'); void el.offsetWidth; el.classList.remove('sb-refresh') }
            function update(el) {
              var show = hovered.indexOf(el) >= 0 || timers.has(el);
              if (show === el.hasAttribute('data-sb')) return;
              if (show) el.setAttribute('data-sb', ''); else el.removeAttribute('data-sb');
              refresh(el);
            }
            function scrollers(t) {
              var out = [];
              for (var el = t instanceof Element ? t : null; el && el !== d.body; el = el.parentElement) {
                var cs = getComputedStyle(el);
                if ((/auto|scroll/.test(cs.overflowY) && el.scrollHeight > el.clientHeight)
                  || (/auto|scroll/.test(cs.overflowX) && el.scrollWidth > el.clientWidth)) out.push(el);
              }
              return out;
            }
            d.addEventListener('pointerover', function (e) {
              if (e.pointerType !== 'mouse' || !active()) return;
              var now = scrollers(e.target);
              hovered.slice().forEach(function (el) { if (now.indexOf(el) < 0) { hovered.splice(hovered.indexOf(el), 1); update(el) } });
              now.forEach(function (el) { if (hovered.indexOf(el) < 0) { hovered.push(el); update(el) } });
            }, { passive: true });
            d.addEventListener('scroll', function (e) {
              if (!active() || !(e.target instanceof Element) || e.target === root) return;
              var el = e.target;
              clearTimeout(timers.get(el));
              timers.set(el, setTimeout(function () { timers.delete(el); update(el) }, 900));
              update(el);
            }, { capture: true, passive: true });
          }

          // Filtering the sidebar's pages as you type (dev-plan 15.9): the
          // same rule as treeFilter.ts in the app. A page shows when its
          // title or number contains what was typed, with its parents,
          // dimmed, for context; case and accents are ignored. The filter
          // is kept for this browser tab, so it is still there when the
          // page you chose has loaded.
          function wireTreeFilter() {
            var input = d.querySelector('.tree-filter__input');
            if (!input) return;
            var none = d.querySelector('.tree-filter__none');
            var norm = function (s) { return (s || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase(); };
            var rows = [].slice.call(d.querySelectorAll('.tree .tree__link')).map(function (a) {
              var marker = a.querySelector('.tree__marker'), title = a.querySelector('.tree__title');
              var text = title ? title.textContent : a.textContent;
              return { a: a, title: title, text: text, depth: Number(a.getAttribute('data-depth')) || 0,
                hay: norm((marker ? marker.textContent + ' ' : '') + text) };
            });
            function mark(row, q) {
              if (!row.title) return;
              var at = q ? row.text.toLowerCase().indexOf(q) : -1;
              row.title.textContent = '';
              if (at < 0) { row.title.textContent = row.text; return; }
              var m = d.createElement('mark');
              m.className = 'tree__match';
              m.textContent = row.text.slice(at, at + q.length);
              row.title.appendChild(d.createTextNode(row.text.slice(0, at)));
              row.title.appendChild(m);
              row.title.appendChild(d.createTextNode(row.text.slice(at + q.length)));
            }
            // Whether the pages under each match show too, remembered in
            // this browser like the app's own toggle.
            var toggle = d.querySelector('.tree-filter__children');
            var KEY = 'tesria-tree-filter-children', withChildren = true;
            try { withChildren = localStorage.getItem(KEY) !== '0'; } catch (e) { /* on */ }
            function showToggle() {
              if (!toggle) return;
              toggle.setAttribute('aria-pressed', withChildren ? 'true' : 'false');
              toggle.classList.toggle('is-on', withChildren);
            }
            showToggle();
            if (toggle) toggle.addEventListener('click', function () {
              withChildren = !withChildren;
              try { if (withChildren) localStorage.removeItem(KEY); else localStorage.setItem(KEY, '0'); } catch (e) { /* this page only */ }
              showToggle();
              apply();
            });
            var first = null;
            function apply() {
              var raw = input.value.trim(), q = norm(raw), shown = {}, match = {}, any = false;
              first = null;
              var parent = {};
              rows.forEach(function (r, i) {
                if (!q || r.hay.indexOf(q) < 0) return;
                match[i] = shown[i] = true;
                if (!first) first = r;
                for (var j = i - 1, depth = r.depth; j >= 0 && depth > 0; j--) {
                  if (rows[j].depth < depth) { shown[j] = parent[j] = true; depth = rows[j].depth; }
                }
                if (withChildren) {
                  for (var k = i + 1; k < rows.length && rows[k].depth > r.depth; k++) shown[k] = true;
                }
              });
              rows.forEach(function (r, i) {
                var show = !q || shown[i];
                r.a.style.display = show ? '' : 'none';
                r.a.classList.toggle('tree__link--context', !!q && !!parent[i] && !match[i]);
                mark(r, q ? raw.toLowerCase() : '');
                if (show) any = true;
              });
              if (none) none.style.display = q && !any ? '' : 'none';
            }
            var FILTER = 'tesria-tree-filter';
            function remember() {
              try { if (input.value) sessionStorage.setItem(FILTER, input.value); else sessionStorage.removeItem(FILTER); } catch (e) { /* this page only */ }
            }
            // The clear button, as in the app (0.8.1): the stylesheet hides
            // the browser's own, and a phone shows none (found 2026-10-01 on
            // the docs site, which had neither).
            var clear = d.querySelector('.tree-filter__clear');
            function showClear() { if (clear) clear.style.display = input.value ? '' : 'none'; }
            function reset() { input.value = ''; remember(); apply(); showClear(); }
            if (clear) clear.addEventListener('click', function () { reset(); input.focus(); });
            try { input.value = sessionStorage.getItem(FILTER) || ''; } catch (e) { /* empty */ }
            if (input.value) apply();
            showClear();
            input.addEventListener('input', function () { remember(); apply(); showClear(); });
            input.addEventListener('keydown', function (e) {
              if (e.key === 'Escape') reset();
              if (e.key === 'Enter' && first) { e.preventDefault(); location.href = first.a.href; }
            });
          }

          // The sidebar keeps its place (2026-09-24). Every page of
          // an exported site is its own file, so each choice in the tree
          // loaded a fresh sidebar scrolled to the top, where the app, which
          // never reloads, keeps its place. The tree's scroll is kept for this
          // browser tab and put back on the next page; and the page now open
          // is scrolled into view if it is not, which is also where a reader
          // who arrives from elsewhere starts. On a phone the tree is hidden
          // until the menu opens it, and a hidden tree cannot scroll, so the
          // menu places it again when it opens.
          var placeTree = function () {};
          function wireTreeScroll() {
            var tree = d.querySelector('.sidebar .tree');
            if (!tree) return;
            var KEY = 'tesria-tree-scroll';
            placeTree = function () {
              if (!tree.offsetParent) return;
              var saved = null;
              try { saved = sessionStorage.getItem(KEY); } catch (e) { /* not kept */ }
              if (saved !== null) tree.scrollTop = Number(saved) || 0;
              var current = tree.querySelector('.tree__link.is-active');
              if (current) {
                var box = tree.getBoundingClientRect(), row = current.getBoundingClientRect();
                if (row.top < box.top || row.bottom > box.bottom)
                  tree.scrollTop += row.top - box.top - (box.height - row.height) / 2;
              }
            };
            placeTree();
            addEventListener('pagehide', function () {
              // Only a tree on screen has a position worth keeping: a closed
              // phone menu reads 0, which would lose the reader's place.
              if (!tree.offsetParent) return;
              try { sessionStorage.setItem(KEY, String(Math.round(tree.scrollTop))); } catch (e) { /* not kept */ }
            });
          }

          // The phone menu: the Pages button shows the page tree in place of
          // the page, as the app's own menu does; choosing a page, Escape, or
          // the button again closes it, back where the reader was.
          function wireSiteMenu() {
            var button = d.querySelector('.site-menu'), sidebar = d.getElementById('site-pages');
            if (!button || !sidebar) { if (button) button.hidden = true; return; }
            var readingAt = 0;
            function set(open) {
              if (open === d.body.classList.contains('site-menu-open')) return;
              if (open) readingAt = scrollY;
              d.body.classList.toggle('site-menu-open', open);
              button.setAttribute('aria-expanded', open ? 'true' : 'false');
              // A cross while it is open, as in the app (2026-09-24), in the top bar, which
              // stays on screen however far the list has scrolled.
              button.textContent = open ? '\u2715' : '\u2630';
              button.setAttribute('aria-label', open ? 'Close Pages' : 'Pages');
              if (open) {
                // The tree now scrolls with the page: bring the open page's
                // entry into view rather than restoring the tree's own scroll.
                var current = sidebar.querySelector('.tree__link.is-active');
                if (current) current.scrollIntoView({ block: 'center' }); else scrollTo(0, 0);
              } else {
                scrollTo(0, readingAt);
              }
            }
            button.addEventListener('click', function () { set(!d.body.classList.contains('site-menu-open')); });
            addEventListener('keydown', function (e) { if (e.key === 'Escape') set(false); });
            sidebar.addEventListener('click', function (e) { if (e.target.closest && e.target.closest('a')) set(false); });
          }

          // Expand blocks are captured closed, with their body hidden, and
          // their toggle was the application's. Without this every answer on
          // an exported FAQ stayed shut (found 2026-09-23).
          function wireExpands() {
            d.querySelectorAll('.expand').forEach(function (box) {
              var body = box.querySelector(':scope > .expand__body');
              if (!body) return;
              var toggle = box.querySelector(':scope > .expand__header .expand__toggle');
              function flip() {
                var open = body.hasAttribute('hidden');
                if (open) body.removeAttribute('hidden'); else body.setAttribute('hidden', '');
                box.classList.toggle('is-open', open);
                if (toggle) toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
              }
              box.querySelectorAll(':scope > .expand__header button').forEach(function (b) { b.addEventListener('click', flip) });
            });
          }

          // A code block's Copy button, which was the application's too.
          function wireCopy() {
            d.querySelectorAll('.code-block__copy').forEach(function (b) {
              var code = b.closest('.code-block');
              code = code && code.querySelector('pre code');
              if (!code || !navigator.clipboard) { b.style.display = 'none'; return; }
              b.addEventListener('click', function () {
                navigator.clipboard.writeText(code.textContent || '').then(function () {
                  b.textContent = 'Copied!';
                  setTimeout(function () { b.textContent = 'Copy' }, 1500);
                }, function () {});
              });
            });
          }

          // Videos shown as animations (dev-plan 10.5 step 2): the pause
          // button, and paused from the start for a reader whose system asks
          // for reduced motion. The application's component does this in the
          // wiki; none of it survives a capture, so this does it here, from
          // the same data attributes.
          var PLAY = '<svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5v14l12-7z" fill="currentColor"/></svg>';
          var PAUSE = '<svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5h4v14H7zM13 5h4v14h-4z" fill="currentColor"/></svg>';
          function wireAnimations() {
            var reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
            d.querySelectorAll('[data-animation]').forEach(function (box) {
              var v = box.querySelector('video'), b = box.querySelector('[data-animation-toggle]');
              if (!v) return;
              v.muted = true;
              function show() {
                if (!b) return;
                b.setAttribute('aria-pressed', v.paused ? 'true' : 'false');
                b.setAttribute('aria-label', v.paused ? 'Play the Animation' : 'Pause the Animation');
                b.innerHTML = v.paused ? PLAY : PAUSE;
              }
              if (reduce) { v.removeAttribute('autoplay'); v.pause(); }
              else { var started = v.play(); if (started && started.catch) started.catch(function () {}); }
              if (b) b.addEventListener('click', function () { if (v.paused) v.play(); else v.pause(); });
              v.addEventListener('play', show);
              v.addEventListener('pause', show);
              show();
            });
          }

          if (d.readyState === 'loading') d.addEventListener('DOMContentLoaded', wire);
          else wire();
        })();
        </script>
        """;
}
