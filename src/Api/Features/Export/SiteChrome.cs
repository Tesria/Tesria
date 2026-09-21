using System.Text;
using Tesria.Api.Domain;

namespace Tesria.Api.Features.Export;

/// <summary>
/// The application's furniture around an exported page: the top bar with the
/// brand and the appearance menu, and (for a site) the space sidebar with its
/// icon, name and page tree. Owner's request, 2026-09-20: "make the export
/// html look more like the real product".
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
public static class SiteChrome
{
    /// <summary>
    /// The name and mark in the top-left of the bar.
    ///
    /// <para><b>The seam for instance branding.</b> The owner's plan is that an
    /// instance can replace both with its own, and that an export carries
    /// them. The name half already works: it is the instance name, which an
    /// administrator sets in Administration → Settings and which defaults to
    /// "Tesria". The mark is the built-in one until there is somewhere to
    /// upload a replacement; when that lands it needs to set
    /// <see cref="LogoPath"/> and copy the file into the export beside the
    /// stylesheet. Nothing else here has to change.</para>
    /// </summary>
    /// <param name="Name">The wordmark. The instance name.</param>
    /// <param name="LogoPath">
    /// A site-relative path to an uploaded mark (e.g. <c>assets/brand.webp</c>),
    /// or null for the built-in Tesria mark. Always null today.
    /// </param>
    public record Brand(string Name, string? LogoPath = null);

    /// <summary>What the sidebar needs to know about the space it is showing.</summary>
    /// <param name="IconPath">
    /// A site-relative path to the space's uploaded icon, or null when it has
    /// none and the tile is drawn from its key.
    /// </param>
    /// <summary>
    /// No tile colour: the application's per-space colour is deliberately not
    /// carried into an export, because an export is one space and the colour
    /// only means something in a list of them. See <see cref="SpaceIcon"/>.
    /// </summary>
    public record SpaceHead(
        string Key, string Name, bool IsPublic,
        SpaceIconKind IconKind, string? IconValue, string? IconPath);

    public static SpaceHead HeadOf(Space space, string? iconPath) =>
        new(space.Key, space.Name, space.IsPublic, space.IconKind, space.IconValue, iconPath);

    /* ---- the pieces ------------------------------------------------------ */

    /// <summary>
    /// The top bar: the brand on the left, the appearance menu on the right.
    /// The brand links home, which for a site is its index and for a
    /// single-page file is nowhere, so it is rendered as plain text there
    /// rather than as a link that goes nowhere.
    /// </summary>
    public static string Topbar(Brand brand, string? homeHref, string currentPath = "")
    {
        var mark = brand.LogoPath is null
            ? $"<span class=\"brand__mark\">{BrandMark}</span>"
            : $"<span class=\"brand__mark\"><img src=\"{SiteExport.Escape(SiteExport.Relative(currentPath, brand.LogoPath))}\" alt=\"\" width=\"20\" height=\"20\" /></span>";
        var word = $"<span class=\"brand__word\">{SiteExport.Escape(brand.Name)}</span>";
        var inner = mark + word;
        var home = homeHref is null
            ? $"<span class=\"brand\">{inner}</span>"
            : $"<a class=\"brand\" href=\"{SiteExport.Escape(homeHref)}\">{inner}</a>";

        return $"""<header class="topbar">{home}<div class="topbar__right">{ThemeMenu()}</div></header>""";
    }

    /// <summary>
    /// The space sidebar: the icon, key and name, then the page tree under a
    /// "Pages" heading. The same three bands the application shows, minus the
    /// parts that only mean something signed in (+ New page, Space settings),
    /// which is also what a reader of a public space sees.
    /// </summary>
    public static string Sidebar(SpaceHead space, IReadOnlyList<SiteExport.Placed> pages, string currentPath)
    {
        var badge = space.IsPublic
            ? """<span class="badge badge--public" title="Readable by anyone on the internet">public</span>"""
            : "";
        return $"""
        <aside class="sidebar">
        <div class="sidebar__top"><div class="sidebar__head">{SpaceIcon(space, 32, currentPath)}<div>
        <div class="sidebar__key">{SiteExport.Escape(space.Key)}{badge}</div>
        <div class="sidebar__name">{SiteExport.Escape(space.Name)}</div>
        </div></div></div>
        <div class="tree-section">
        <div class="tree-section__heading"><span>{PagesIcon} Pages</span></div>
        {Tree(pages, currentPath)}
        </div>
        </aside>
        """;
    }

    /// <summary>
    /// The page tree, flattened with the same indentation the application
    /// uses (8px plus 14px a level, set inline there and so inline here).
    /// </summary>
    public static string Tree(IReadOnlyList<SiteExport.Placed> pages, string currentPath)
    {
        if (pages.Count == 0) return """<p class="muted small">No pages yet.</p>""";
        var sb = new StringBuilder("""<nav class="tree">""");
        foreach (var page in pages)
        {
            var current = page.Path == currentPath;
            var cls = current ? "tree__link is-active" : "tree__link";
            var aria = current ? " aria-current=\"page\"" : "";
            sb.Append($"<a class=\"{cls}\"{aria} style=\"padding-left: {8 + page.Depth * 14}px\" ")
              .Append($"href=\"{SiteExport.Relative(currentPath, page.Path)}\">{SiteExport.Escape(page.Title)}</a>");
        }
        return sb.Append("</nav>").ToString();
    }

    /// <summary>
    /// A space's icon: an uploaded picture, a chosen emoji, or the key's first
    /// letter on a coloured tile. Mirrors <c>SpaceIcon.tsx</c>, including its
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
        // application it is one of twelve colours picked per space. The owner's
        // reasoning (2026-09-20): those colours exist to tell spaces apart in a
        // list, and an export is one space by definition, so the colour carries
        // no information there and may as well look like the rest of the
        // product. It follows the reader's accent and light/dark with it, which
        // is why these are the tokens rather than the hex they resolve to;
        // --on-primary is the letter's colour for the same reason a filled
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

    /// <summary>Tesria's mark, from <c>BrandMark.tsx</c>.</summary>
    private static readonly string BrandMark = Svg(20,
        """<path d="M12 3l8 4.5-8 4.5-8-4.5L12 3z" /><path d="M4 12l8 4.5 8-4.5M4 16.5L12 21l8-4.5" />""");

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

    /// <summary>The six accents, from <c>theme.ts</c>.</summary>
    private static readonly (string Name, string Label)[] Accents =
    [
        ("blue", "Blue"), ("teal", "Teal"), ("green", "Green"),
        ("purple", "Purple"), ("orange", "Orange"), ("magenta", "Magenta"),
    ];

    /// <summary>
    /// The appearance menu, markup-identical to <c>ThemeToggle.tsx</c>. The
    /// React component's behaviour cannot survive a capture (every script but
    /// the theme script is stripped), so <see cref="ThemeScript"/> drives this
    /// markup instead, through the <c>data-theme-*</c> hooks.
    /// </summary>
    private static string ThemeMenu()
    {
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

        var accents = string.Concat(Accents.Select(a =>
            $"<button type=\"button\" class=\"theme-menu__accent\" data-theme-accent=\"{a.Name}\" "
            + $"style=\"background: var(--accent-dot-{a.Name})\" title=\"{a.Label}\" aria-label=\"{a.Label}\" aria-pressed=\"false\"></button>"));

        return $"""
        <div class="theme-menu">
        <button type="button" class="theme-toggle" data-theme-trigger aria-haspopup="true" aria-expanded="false" title="Appearance" aria-label="Appearance">{triggerIcons}</button>
        <div class="theme-menu__panel" role="dialog" aria-label="Appearance" data-theme-panel hidden>
        <button type="button" class="popover__close" aria-label="Close" data-theme-close>{CloseIcon}</button>
        <p class="theme-menu__heading">Theme</p>
        <div class="theme-menu__modes">{modes}</div>
        <p class="theme-menu__heading">Accent colour</p>
        <div class="theme-menu__accents">{accents}</div>
        </div></div>
        """;
    }

    /// <summary>
    /// The trigger shows what is in force, not what is chosen: on "system"
    /// that is whichever the operating system currently resolves to, which is
    /// why the script swaps between these rather than showing one.
    /// </summary>
    private static string TriggerIcon(string mode) => Modes.First(m => m.Mode == mode).Icon.Replace("width=\"17\" height=\"17\"", "width=\"19\" height=\"19\"");

    private static readonly string CheckIcon = Svg(15, """<path d="m5 12.5 4.5 4.5L19 7.5" />""");

    private static readonly string CloseIcon = Svg(18, """<path d="M6 6l12 12M18 6L6 18" />""", stroke: 2);

    /// <summary>
    /// The one script an export carries. It applies the stored theme and
    /// accent before first paint (without it the file renders light for a
    /// frame and then flips) and drives the appearance menu afterwards.
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
          function get(k) { try { return localStorage.getItem(k) } catch (e) { return null } }
          function set(k, v) { try { v === null ? localStorage.removeItem(k) : localStorage.setItem(k, v) } catch (e) {} }

          // Before first paint.
          var stored = get(THEME);
          if (stored === 'light' || stored === 'dark') root.setAttribute('data-theme', stored);
          var storedAccent = get(ACCENT);
          if (storedAccent) root.setAttribute('data-accent', storedAccent);

          function systemTheme() { return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light' }
          function mode() { var v = get(THEME); return (v === 'light' || v === 'dark') ? v : 'system' }
          function accent() { return get(ACCENT) || DEFAULT_ACCENT }

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

          function close() {
            var panel = d.querySelector('[data-theme-panel]'), trigger = d.querySelector('[data-theme-trigger]');
            if (panel) panel.hidden = true;
            if (trigger) { trigger.setAttribute('aria-expanded', 'false'); trigger.classList.remove('is-open') }
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
                var opening = panel.hidden;
                panel.hidden = !opening;
                trigger.setAttribute('aria-expanded', opening ? 'true' : 'false');
                trigger.classList.toggle('is-open', opening);
              });
              d.addEventListener('click', function (e) {
                if (!panel.hidden && !panel.contains(e.target) && !trigger.contains(e.target)) close();
              });
              d.addEventListener('keydown', function (e) { if (e.key === 'Escape') close() });
            }
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
                set(ACCENT, next === DEFAULT_ACCENT ? null : next);
                applyAccent(next);
                sync();
              });
            });
            // The trigger and the "system" hint both show what the OS is
            // resolving to, so both have to follow it changing.
            matchMedia('(prefers-color-scheme: dark)').addEventListener('change', sync);
            sync();
          }

          if (d.readyState === 'loading') d.addEventListener('DOMContentLoaded', wire);
          else wire();
        })();
        </script>
        """;
}
