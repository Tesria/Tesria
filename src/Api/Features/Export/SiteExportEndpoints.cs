using System.IO.Compression;
using System.Text;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Publishing a space as a static site (dev-plan 12.2).
///
/// Every page is captured from its own render route, exactly as a single-page
/// HTML export is, so a site looks like the wiki for the same reason a PDF
/// does: it is the same rendering. What this file adds is everything that is
/// the *site* rather than the pages — where files go, what links mean once
/// pages are files, and which pages are in it at all.
/// </summary>
public static class SiteExportEndpoints
{
    /// <summary>Above this a space needs a job with progress, which is not built until something needs it.</summary>
    private const int MaxPages = 300;

    public static IEndpointRouteBuilder MapSiteExportEndpoints(this IEndpointRouteBuilder routes)
    {
        // GET, not POST: building a site reads pages and changes nothing, it
        // is the same verb the single-page export uses, and a read-only API
        // token should be able to do it. The design said POST; this is the
        // same request with the audience in the query string.
        routes.MapGet("/spaces/{key}/export/site", ExportSite)
            .WithTags("Export")
            .RequirePermission(InstancePermissions.PagesExport);
        return routes;
    }

    /// <param name="audience">
    /// "anonymous" renders as a reader with no account, which is the default
    /// and the leak-proof one: a documentation site built that way cannot
    /// contain a page the public could not already read, whatever the
    /// exporter's own access. "me" renders as the caller, for a site that
    /// will live behind somebody else's access control.
    /// </param>
    private static async Task<IResult> ExportSite(
        string key, string? audience, AppDbContext db, IPermissionService perms,
        Infrastructure.Export.IRenderTokens renderTokens, IPdfRenderer renderer,
        IAttachmentStorage storage, ISiteSettingsService settings, IConfiguration config,
        CurrentUser current, IWebHostEnvironment env, CancellationToken ct)
    {
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant(), ct);
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();

        var anonymous = !string.Equals(audience, "me", StringComparison.OrdinalIgnoreCase);

        // An anonymous site of a space nobody can read anonymously would be an
        // empty site, and silently shipping an empty zip is worse than saying
        // so (dev-plan 12.2).
        if (anonymous && !await perms.IsPubliclyViewableSpaceAsync(space.Id))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["audience"] =
                [
                    "This space is not public, so an anonymous site would be empty. Publish it first "
                    + "(Administration → Spaces), or export it for yourself instead.",
                ],
            });

        // Checked after the request is: "this space is not public" is
        // something the caller can act on, and a missing renderer is not, so
        // the actionable answer goes first.
        if (!renderer.Available || !renderTokens.IsConfigured)
            return Results.Problem(
                detail: "This instance has no export renderer configured, so a site cannot be built.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        // Which pages are in the site is decided by the audience, not by the
        // exporter: the whole point of the anonymous default is that it cannot
        // include something the public could not already read.
        var reader = anonymous ? perms.AsAnonymous() : perms;
        var pages = await VisiblePagesAsync(db, reader, space.Id, ct);
        if (pages.Count > MaxPages)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pages"] = [$"This space has {pages.Count} pages and the limit is {MaxPages}."],
            });

        var placed = SiteExport.Place(pages);
        var pagePaths = placed.ToDictionary(p => p.Id, p => p.Path);
        var token = renderTokens.IssueForSpace(space.Id, anonymous || !current.IsAuthenticated ? null : current.RequireId());
        var origin = ExportEndpoints.RenderOrigin(config);
        var instanceName = (await settings.GetAsync(ct)).InstanceName;
        var footer = Footer(instanceName);
        var css = await StylesheetAsync(env, ct);
        var themeScript = ThemeScript();

        // Attachments referenced by the pages that are actually in the site.
        var assets = await AssetsAsync(db, placed.Select(p => p.Id).ToList(), ct);

        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteTextAsync(zip, "assets/site.css", css, ct);
            await WriteTextAsync(zip, "index.html",
                SiteExport.Index(space, placed, css, themeScript, footer), ct);
            await WriteTextAsync(zip, "404.html",
                SiteExport.NotFound(space, css, themeScript, footer), ct);

            foreach (var page in placed)
            {
                var url = $"{origin}/export/pages/{page.Id}?chrome=site";
                // Not inlined: the site ships real files under assets/, which
                // keeps each page small and lets a browser cache an image once
                // rather than once per page that shows it.
                var captured = await renderer.CaptureAsync(
                    url, token, "html", page.Title, ct, inlineAssets: false);
                if (captured is null) continue; // one page failing must not lose the rest

                var html = Encoding.UTF8.GetString(captured);
                html = SiteExport.RewriteLinks(html, page.Path, pagePaths, assets.Names);
                html = InjectSiteChrome(html, placed, page, footer, themeScript);
                await WriteTextAsync(zip, $"{page.Path}/index.html", html, ct);
            }

            foreach (var (id, name) in assets.Names)
            {
                if (!assets.Keys.TryGetValue(id, out var storageKey)) continue;
                await using var bytes = storage.OpenRead(storageKey);
                if (bytes is null) continue;
                var entry = zip.CreateEntry($"assets/{name}", CompressionLevel.Optimal);
                await using var target = entry.Open();
                await bytes.CopyToAsync(target, ct);
            }
        }

        buffer.Position = 0;
        return Results.File(buffer, "application/zip", $"{space.Key.ToLowerInvariant()}-site.zip");
    }

    /// <summary>Every page the audience may see, as a tree, in tree order.</summary>
    private static async Task<List<SiteExport.PageNode>> VisiblePagesAsync(
        AppDbContext db, IPermissionService reader, Guid spaceId, CancellationToken ct)
    {
        var rows = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == spaceId)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.ParentPageId })
            .ToListAsync(ct);

        var allowed = new HashSet<Guid>();
        foreach (var row in rows)
            if (await reader.CanViewPageAsync(row.Id)) allowed.Add(row.Id);

        List<SiteExport.PageNode> Build(Guid? parent) =>
        [
            .. rows
                .Where(r => r.ParentPageId == parent && allowed.Contains(r.Id))
                .Select(r => new SiteExport.PageNode(r.Id, r.Title, Build(r.Id))),
        ];

        // A page whose parent is hidden would otherwise vanish with it; it is
        // lifted to the root instead, because the reader may see it.
        var tree = Build(null);
        var placedIds = new HashSet<Guid>();
        void Collect(IReadOnlyList<SiteExport.PageNode> nodes)
        {
            foreach (var n in nodes) { placedIds.Add(n.Id); Collect(n.Children); }
        }
        Collect(tree);
        var orphans = rows
            .Where(r => allowed.Contains(r.Id) && !placedIds.Contains(r.Id))
            .Select(r => new SiteExport.PageNode(r.Id, r.Title, Build(r.Id)))
            .ToList();
        return [.. tree, .. orphans];
    }

    private record Assets(Dictionary<Guid, string> Names, Dictionary<Guid, string> Keys);

    /// <summary>
    /// Attachment files, named by id and original filename so two files called
    /// "diagram.png" cannot collide.
    /// </summary>
    private static async Task<Assets> AssetsAsync(AppDbContext db, List<Guid> pageIds, CancellationToken ct)
    {
        var rows = await db.Attachments.AsNoTracking()
            .Where(a => pageIds.Contains(a.PageId))
            .Select(a => new { a.Id, a.Filename, a.StorageKey })
            .ToListAsync(ct);

        var names = new Dictionary<Guid, string>();
        var keys = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            var safe = new string(row.Filename.Select(c =>
                char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray());
            names[row.Id] = $"{row.Id:N}-{safe}";
            keys[row.Id] = row.StorageKey;
        }
        return new Assets(names, keys);
    }

    /// <summary>
    /// The compiled stylesheet, verbatim. The site is meant to look like the
    /// app, and the way to guarantee that is to ship the app's own CSS rather
    /// than a version of it maintained separately.
    /// </summary>
    private static async Task<string> StylesheetAsync(IWebHostEnvironment env, CancellationToken ct)
    {
        var assets = Path.Combine(env.WebRootPath ?? "", "assets");
        if (!Directory.Exists(assets)) return "";
        var css = Directory.EnumerateFiles(assets, "index-*.css").OrderBy(f => f).FirstOrDefault();
        return css is null ? "" : await System.IO.File.ReadAllTextAsync(css, ct);
    }

    /// <summary>
    /// The app's own theme script, so an exported site keeps light, dark and
    /// system and the accent colours, with the reader's choice in their own
    /// browser (owner's request, 2026-09-20). It is the only script in the
    /// output, and it is marked so the capture keeps it.
    /// </summary>
    private static string ThemeScript() =>
        """
        <script data-export-keep>
        (function () {
          try {
            var t = localStorage.getItem('tesria-theme');
            if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t);
            var a = localStorage.getItem('tesria-accent');
            if (a) document.documentElement.setAttribute('data-accent', a);
          } catch (e) { /* a browser with storage blocked still renders */ }
          window.__tesriaTheme = function (next) {
            try {
              if (next === 'system') localStorage.removeItem('tesria-theme');
              else localStorage.setItem('tesria-theme', next);
            } catch (e) { /* as above */ }
            if (next === 'system') document.documentElement.removeAttribute('data-theme');
            else document.documentElement.setAttribute('data-theme', next);
          };
        })();
        </script>
        """;

    private static string Footer(string instanceName) =>
        $"""<footer class="site-foot">Exported from {SiteExport.Escape(instanceName)} on {DateTimeOffset.UtcNow:d MMMM yyyy}.</footer>""";

    /// <summary>
    /// Adds the navigation, the footer and the theme control to a captured
    /// page. The capture is the page; this is the site around it.
    /// </summary>
    private static string InjectSiteChrome(
        string html, IReadOnlyList<SiteExport.Placed> pages, SiteExport.Placed page,
        string footer, string themeScript)
    {
        var nav = SiteExport.Nav(pages, page.Path);
        var toggle = """
        <div class="site-theme">
          <button type="button" onclick="__tesriaTheme('light')">Light</button>
          <button type="button" onclick="__tesriaTheme('dark')">Dark</button>
          <button type="button" onclick="__tesriaTheme('system')">System</button>
        </div>
        """;
        var head = themeScript + $"<link rel=\"stylesheet\" href=\"{SiteExport.Relative(page.Path, "assets/site.css")}\" />";
        html = html.Replace("</head>", head + "</head>");

        // The navigation goes *inside* the export container, which is the grid
        // that puts it in the left column; as a sibling it stacked above the
        // page instead. The theme control is fixed-position and belongs
        // outside it.
        html = html.Replace("<div class=\"export\">", $"{toggle}<div class=\"export export--site\">{nav}");
        return html.Replace("</body>", footer + "</body>");
    }

    private static async Task WriteTextAsync(ZipArchive zip, string path, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
    }
}
