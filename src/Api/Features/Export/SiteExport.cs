using System.Text;
using System.Text.RegularExpressions;
using Tesria.Api.Domain;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Turning a space into a static site (dev-plan 12.2): the naming, the link
/// rewriting and the small amount of HTML that is the site rather than the
/// pages.
///
/// Separated from the endpoint because all of it is decisions about shape
/// rather than about HTTP, and because every one of them is worth a test.
/// </summary>
public static partial class SiteExport
{
    /// <summary>One page's place in the exported site.</summary>
    /// <param name="Path">Directory path from the site root, e.g. <c>getting-started/install</c>.</param>
    public record Placed(Guid Id, string Title, string Path, int Depth, Guid? ParentId);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();

    /// <summary>
    /// A page title as a directory name. Lower case, words joined by hyphens,
    /// anything else dropped; a title with nothing usable in it (an emoji, a
    /// title in a script this cannot transliterate) falls back to "page", and
    /// the sibling de-duplication below keeps those apart.
    /// </summary>
    public static string Slug(string title)
    {
        var slug = NotSlug().Replace(title.ToLowerInvariant(), "-").Trim('-');
        // Long slugs make long paths, and some filesystems still care.
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "page" : slug;
    }

    /// <summary>
    /// Lays the tree out as directories, mirroring it, de-duplicating names
    /// among siblings only: two pages called "Overview" under different
    /// parents are not in conflict, and giving them different names would be
    /// surprising.
    /// </summary>
    public static List<Placed> Place(IReadOnlyList<PageNode> tree)
    {
        var placed = new List<Placed>();

        void Walk(IReadOnlyList<PageNode> nodes, string prefix, int depth, Guid? parentId)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                var slug = Slug(node.Title);
                var unique = slug;
                for (var n = 2; !used.Add(unique); n++) unique = $"{slug}-{n}";

                var path = prefix.Length == 0 ? unique : $"{prefix}/{unique}";
                placed.Add(new Placed(node.Id, node.Title, path, depth, parentId));
                if (node.Children.Count > 0) Walk(node.Children, path, depth + 1, node.Id);
            }
        }

        Walk(tree, "", 0, null);
        return placed;
    }

    /// <summary>A page in the tree, as this file needs it.</summary>
    public record PageNode(Guid Id, string Title, IReadOnlyList<PageNode> Children);

    /// <summary>
    /// Rewrites what the app's own URLs mean once the pages are files.
    ///
    /// Three kinds of reference are rewritten, and everything else is left
    /// exactly as it was: an external link is still an external link.
    ///
    /// * A link to a page in this export becomes a relative path to it,
    ///   keeping any heading anchor.
    /// * A link to a page that is *not* in the export (restricted, another
    ///   space, trashed) stops being a link: a static site that 404s on its
    ///   own navigation is worse than one that plainly says the page is not
    ///   here.
    /// * An attachment URL becomes the file beside the page.
    /// </summary>
    public static string RewriteLinks(
        string html, string fromPath, IReadOnlyDictionary<Guid, string> pagePaths,
        IReadOnlyDictionary<Guid, string> assetNames)
    {
        // Page links: /spaces/KEY/pages/{id}[#anchor]
        html = PageLink().Replace(html, match =>
        {
            var id = Guid.Parse(match.Groups["id"].Value);
            var anchor = match.Groups["anchor"].Value;
            if (pagePaths.TryGetValue(id, out var target))
                return Relative(fromPath, target) + anchor;
            // Nothing to point at. The href is emptied rather than removed so
            // the surrounding markup stays valid; the class is what the
            // stylesheet uses to grey it out and the title says why.
            return "#";
        });

        // Attachments: /api/attachments/{id}/download (and the inline form).
        html = AttachmentLink().Replace(html, match =>
        {
            var id = Guid.Parse(match.Groups["id"].Value);
            return assetNames.TryGetValue(id, out var name)
                ? Relative(fromPath, "assets/" + name)
                : match.Value;
        });

        return html;
    }

    [GeneratedRegex(@"/spaces/[A-Za-z0-9]+/pages/(?<id>[0-9a-fA-F-]{36})(?<anchor>#[^""'\s]*)?")]
    private static partial Regex PageLink();

    [GeneratedRegex(@"/api/attachments/(?<id>[0-9a-fA-F-]{36})/download")]
    private static partial Regex AttachmentLink();

    /// <summary>
    /// A path from one page's directory to somewhere else in the site. Every
    /// page is served as <c>&lt;path&gt;/index.html</c>, so a page at depth
    /// two is two levels from the root.
    /// </summary>
    public static string Relative(string fromPath, string toPath)
    {
        var up = fromPath.Length == 0 ? 0 : fromPath.Split('/').Length;
        var prefix = up == 0 ? "./" : string.Concat(Enumerable.Repeat("../", up));
        return prefix + toPath + (toPath.StartsWith("assets/", StringComparison.Ordinal) ? "" : "/");
    }

    /// <summary>
    /// The site root as seen from a page, for the brand link in the top bar.
    /// Not <see cref="Relative"/> with an empty target: that appends a slash
    /// to a prefix which already ends in one.
    /// </summary>
    public static string Root(string fromPath) =>
        fromPath.Length == 0
            ? "./"
            : string.Concat(Enumerable.Repeat("../", fromPath.Split('/').Length));

    public static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// The site's front page: what the space is, and the whole tree. Not a
    /// page of the wiki, so it is built here rather than captured.
    /// </summary>
    /// <summary>
    /// The site's front page: the space, its description, and its pages. It
    /// is the only page of a site that is not a capture, because there is no
    /// page in the wiki for it to be a capture of.
    /// </summary>
    public static string Index(
        Space space, IReadOnlyList<Placed> pages, string css,
        SiteChrome.Brand brand, SiteChrome.SpaceHead head, string footer)
    {
        var body = new StringBuilder();
        body.Append($"<h1>{Escape(space.Name)}</h1>");
        if (!string.IsNullOrWhiteSpace(space.Description))
            body.Append($"<p class=\"site-lede\">{Escape(space.Description)}</p>");
        return Shell(space.Name, body.ToString(), css, brand, head, pages, footer, "");
    }

    public static string NotFound(
        Space space, string css, SiteChrome.Brand brand, SiteChrome.SpaceHead head,
        IReadOnlyList<Placed> pages, string footer) =>
        Shell($"Not found \u00b7 {space.Name}",
            "<h1>Not found</h1><p class=\"site-lede\">That page is not part of this site.</p>",
            // No current page: nothing in the tree is marked, which is honest
            // for a page that is not in the site.
            css, brand, head, pages, footer, "", homeHref: "/");

    /// <summary>
    /// The page frame shared by the index and the 404. A captured page brings
    /// its own document, so this is only for the pages the export writes.
    /// These two are also the reason the chrome is built in
    /// <see cref="SiteChrome"/> rather than rendered in the SPA: they need
    /// exactly the same chrome and neither of them is a capture.
    /// </summary>
    private static string Shell(
        string title, string body, string css, SiteChrome.Brand brand, SiteChrome.SpaceHead head,
        IReadOnlyList<Placed> pages, string footer, string currentPath, string? homeHref = null)
    {
        var stylesheet = Relative(currentPath, "assets/site.css");
        return $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{Escape(title)}</title>
        <link rel="stylesheet" href="{stylesheet}" />
        {SiteChrome.ThemeScript()}
        </head>
        <body>
        {SiteChrome.Topbar(brand, homeHref)}
        <div class="space-layout space-layout--export">
        {SiteChrome.Sidebar(head, pages, currentPath)}
        <section class="space-content">
        <div class="export export--site">
        <div class="page-wrap" data-export-width><article class="paper paper--export">
        {body}
        </article></div>
        {footer}
        </div>
        </section>
        </div>
        </body>
        </html>
        """;
    }
}
