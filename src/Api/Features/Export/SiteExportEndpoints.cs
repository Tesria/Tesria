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
/// the *site* rather than the pages, where files go, what links mean once
/// pages are files, and which pages are in it at all.
/// </summary>
public static class SiteExportEndpoints
{
    /// <summary>Above this a space needs a job with progress, which is not built until something needs it.</summary>
    private const int MaxPages = 300;

    /// <summary>Where a space's uploaded icon lands in the site. Always webp, as stored.</summary>
    private const string SpaceIconAsset = "assets/space-icon.webp";

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
    /// <param name="progress">
    /// An id the page made up, to read how far along the export is from
    /// <c>/api/export-progress/{id}</c> while it waits (dev-plan 20.1).
    /// </param>
    private static async Task<IResult> ExportSite(
        string key, string? audience, string? progress, ExportProgress tracker, AppDbContext db, IPermissionService perms,
        Infrastructure.Export.IRenderTokens renderTokens, IPdfRenderer renderer,
        IAttachmentStorage storage, IProfileMediaService media, ISiteSettingsService settings, IConfiguration config,
        CurrentUser current, IWebHostEnvironment env, Infrastructure.Branding.IBrandAssets brandAssets, CancellationToken ct)
    {
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant(), ct);
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        // Turned off for this space (dev-plan 12.3), for everyone.
        if (!SpaceExports.Allows(space, ExportFormat.Site)) return SpaceExports.Refused(space, ExportFormat.Site);

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
        var report = tracker.Start(current.Id, progress);
        try
        {
            report.Stage("Checking which pages go in", 0);
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
            var siteSettings = await settings.GetAsync(ct);
            var instanceName = siteSettings.InstanceName;
            var footer = Footer(instanceName);
            var css = await StylesheetAsync(env, ct);

            // The branding as it is now, with its files under assets/ (dev-plan
            // 13.1). The name in the bar is the brand name, or Tesria; the
            // instance name titles the pages and signs the footer.
            var packed = await BrandExport.PackAsync(siteSettings, brandAssets, env, inline: false, ct);
            var brand = packed.Brand;

            // A space with an uploaded icon needs that file in the site: the
            // sidebar cannot reach back to the instance for it.
            var icon = space.IconKind == SpaceIconKind.Image
                ? media.OpenRead(media.KeyFor(ProfileMediaKind.SpaceIcon, space.Id))
                : null;
            var head = SiteChrome.HeadOf(space, icon is null ? null : SpaceIconAsset);

            // Attachments referenced by the pages that are actually in the site.
            var assets = await AssetsAsync(db, placed.Select(p => p.Id).ToList(), ct);

            var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteTextAsync(zip, "assets/site.css", css, ct);
                foreach (var (path, bytes) in packed.Files)
                {
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    await target.WriteAsync(bytes, ct);
                }
                await WriteTextAsync(zip, "index.html",
                    SiteExport.Index(space, placed, css, brand, head, footer), ct);
                await WriteTextAsync(zip, "404.html",
                    SiteExport.NotFound(space, css, brand, head, placed, footer), ct);

                if (icon is not null)
                {
                    await using (icon)
                    {
                        var entry = zip.CreateEntry(SpaceIconAsset, CompressionLevel.Optimal);
                        await using var target = entry.Open();
                        await icon.CopyToAsync(target, ct);
                    }
                }

                report.Stage("Capturing pages", placed.Count);
                foreach (var page in placed)
                {
                    report.Working(page.Title);
                    var url = $"{origin}/export/pages/{page.Id}?chrome=site";
                    // Not inlined: the site ships real files under assets/, which
                    // keeps each page small and lets a browser cache an image once
                    // rather than once per page that shows it.
                    var captured = await renderer.CaptureAsync(
                        url, token, "html", page.Title, ct, inlineAssets: false);
                    report.Step();
                    if (captured is null) continue; // one page failing must not lose the rest

                    var html = Encoding.UTF8.GetString(captured);
                    html = SiteExport.RewriteLinks(html, page.Path, pagePaths, assets.Names);
                    html = SiteChrome.ApplyToDocument(html, brand,
                        Infrastructure.Branding.BrandTitle.Format(instanceName, space.Name, page.Title), page.Path);
                    html = InjectSiteChrome(html, placed, page, brand, head, footer);
                    await WriteTextAsync(zip, $"{page.Path}/index.html", html, ct);
                }

                report.Stage("Copying files", assets.Names.Count);
                foreach (var (id, name) in assets.Names)
                {
                    report.Working(name[33..]);
                    report.Step();
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
        finally
        {
            report.Finish();
        }
    }

    /// <summary>Every page the audience may see, as a tree, in tree order.</summary>
    private static async Task<List<SiteExport.PageNode>> VisiblePagesAsync(
        AppDbContext db, IPermissionService reader, Guid spaceId, CancellationToken ct)
    {
        var rows = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == spaceId)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.ParentPageId, p.Emoji })
            .ToListAsync(ct);

        var allowed = new HashSet<Guid>();
        foreach (var row in rows)
            if (await reader.CanViewPageAsync(row.Id)) allowed.Add(row.Id);

        List<SiteExport.PageNode> Build(Guid? parent) =>
        [
            .. rows
                .Where(r => r.ParentPageId == parent && allowed.Contains(r.Id))
                .Select(r => new SiteExport.PageNode(r.Id, r.Title, Build(r.Id), r.Emoji)),
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
            .Select(r => new SiteExport.PageNode(r.Id, r.Title, Build(r.Id), r.Emoji))
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
    internal static async Task<string> StylesheetAsync(IWebHostEnvironment env, CancellationToken ct)
    {
        var assets = Path.Combine(env.WebRootPath ?? "", "assets");
        if (!Directory.Exists(assets)) return "";
        var css = Directory.EnumerateFiles(assets, "index-*.css").OrderBy(f => f).FirstOrDefault();
        return css is null ? "" : await System.IO.File.ReadAllTextAsync(css, ct);
    }

    /// <summary>
    /// The line at the bottom of every page: where and when, and which Tesria
    /// (dev-plan 16.1), with the date the US way. An instance still called
    /// Tesria is not named twice.
    /// </summary>
    internal static string Footer(string instanceName, DateTimeOffset? at = null)
    {
        var date = (at ?? DateTimeOffset.UtcNow).ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var version = $"Tesria {Infrastructure.Versioning.AppVersion.Current}";
        var text = instanceName == "Tesria"
            ? $"Exported on {date} from {version}."
            : $"Exported from {instanceName} on {date}, with {version}.";
        var escaped = SiteExport.Escape(text);
        return $"""<footer class="site-foot" title="{escaped}">{escaped}</footer>""";
    }

    /// <summary>
    /// Wraps a captured page in the application's chrome: the top bar, the
    /// space sidebar with its page tree, and the footer. The capture is the
    /// page; this is the product around it.
    /// </summary>
    private static string InjectSiteChrome(
        string html, IReadOnlyList<SiteExport.Placed> pages, SiteExport.Placed page,
        SiteChrome.Brand brand, SiteChrome.SpaceHead head, string footer)
    {
        var inHead = SiteChrome.ThemeScript()
            + $"<link rel=\"stylesheet\" href=\"{SiteExport.Relative(page.Path, "assets/site.css")}\" />";
        html = html.Replace("</head>", inHead + "</head>");

        // The captured document is `<div class="export">…</div>` and nothing
        // else. It becomes the content column of the app's own two-column
        // layout, with the bar above it and the sidebar beside it, so the
        // stylesheet the site already ships lays it out with no new rules.
        var before = SiteChrome.Topbar(brand, SiteExport.Root(page.Path), page.Path)
            + "<div class=\"space-layout space-layout--export\">"
            + SiteChrome.Sidebar(head, pages, page.Path)
            + "<section class=\"space-content\">";
        html = html.Replace("<div class=\"export\">", before + "<div class=\"export export--site\">");
        return html.Replace("</body>", footer + "</section></div></body>");
    }

    private static async Task WriteTextAsync(ZipArchive zip, string path, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
    }
}
