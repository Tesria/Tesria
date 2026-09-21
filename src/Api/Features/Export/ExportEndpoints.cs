using System.Text;
using Tesria.Api.Features.Blocks;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Export;

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/pages/{id:guid}/export", ExportPage)
            .WithTags("Export").AllowAnonymous(); // dev-plan 5.2: readers may take their docs with them
        return routes;
    }

    /// <summary>
    /// Exports a page as Markdown, as one HTML file, or as a PDF.
    ///
    /// Markdown is rendered from the document; HTML and PDF are captured from
    /// the page's own render route by the sidecar, so what comes out is the
    /// rendering a reader sees rather than a second renderer's impression of
    /// it (dev-plan 12.1).
    /// </summary>
    private static async Task<IResult> ExportPage(
        Guid id, string? format, AppDbContext db, IPermissionService perms,
        Infrastructure.Permissions.IInstancePermissions rights, Infrastructure.Auth.CurrentUser current,
        IDynamicBlockService blocks, ISiteSettingsService settings, IConfiguration config,
        Infrastructure.Export.IRenderTokens renderTokens, IPdfRenderer pdf, CancellationToken ct)
    {
        var page = await db.Pages.AsNoTracking()
            .Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page?.CurrentVersion is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();

        // Signed in: the caller's own right. Anonymous: whatever the built-in
        // User role holds, so a reader is never more privileged than a member
        // (dev-plan 11.1).
        var mayExport = current.IsAuthenticated
            ? await rights.HasAsync(Infrastructure.Permissions.InstancePermissions.PagesExport)
            : await rights.AnonymousHasAsync(Infrastructure.Permissions.InstancePermissions.PagesExport);
        if (!mayExport) return Results.Forbid();

        var safeName = SafeFileName(page.Title);
        var wanted = (format ?? "markdown").ToLowerInvariant();

        if (wanted is "md" or "markdown")
        {
            // Markdown keeps its attachment URLs: a data: URI is unreadable in
            // a text file, which is rather the point of Markdown. It is also
            // the one format that is genuinely a different document rather
            // than a picture of this one, which is why it is still rendered
            // rather than captured.
            var snapshot = await PageSnapshots.BlocksAsync(page.Id, page.CurrentVersion.ContentJson, blocks, ct);
            var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
            var markdown = $"# {page.Title}\n\n{ProseMirrorRenderer.ToMarkdown(page.CurrentVersion.ContentJson, snapshot, baseUrl)}";
            return Results.File(Encoding.UTF8.GetBytes(markdown), "text/markdown", $"{safeName}.md");
        }

        if (wanted is not ("html" or "pdf"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["format"] = ["Supported formats are 'markdown', 'html' and 'pdf'."],
            });

        var brand = new SiteChrome.Brand((await settings.GetAsync(ct)).InstanceName);
        return await CaptureAsync(page.Id, page.Title, safeName, wanted, brand, current, renderTokens, pdf, config, ct);
    }

    /// <summary>
    /// PDF and single-file HTML are photographs of the page's own render
    /// route, taken by the sidecar (dev-plan 12.1). Nothing here builds a
    /// document: the app says which page, and hands over a token that lets
    /// the sidecar's browser read it as this caller and nothing more.
    /// </summary>
    private static async Task<IResult> CaptureAsync(
        Guid pageId, string title, string safeName, string format, SiteChrome.Brand brand,
        Infrastructure.Auth.CurrentUser current, Infrastructure.Export.IRenderTokens renderTokens,
        IPdfRenderer pdf, IConfiguration config, CancellationToken ct)
    {
        if (!pdf.Available || !renderTokens.IsConfigured)
            return Results.Problem(
                detail: "This instance has no export renderer configured. Export as Markdown instead.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        var token = renderTokens.IssueForPage(pageId, current.IsAuthenticated ? current.RequireId() : null);
        // `chrome=page` tells the render route this is a file somebody will
        // open in a browser rather than paper, so it keeps the reader's theme
        // instead of forcing light. A PDF asks for no chrome and stays paper.
        var url = $"{RenderOrigin(config)}/export/pages/{pageId}" + (format == "html" ? "?chrome=page" : "");
        var bytes = await pdf.CaptureAsync(url, token, format, title, ct);

        if (bytes is null)
            return Results.Problem(
                detail: "The export renderer did not answer. Export as Markdown instead, or try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        if (format != "html") return Results.File(bytes, "application/pdf", $"{safeName}.pdf");

        // The top bar, so a single-file export looks like the product it came
        // from (owner's request, 2026-09-20). No sidebar: one page has no
        // tree to show, and the brand links nowhere because there is nowhere
        // in a single file to go.
        var html = Encoding.UTF8.GetString(bytes);
        html = html.Replace("</head>", SiteChrome.ThemeScript() + "</head>");
        html = html.Replace(
            "<div class=\"export\">",
            SiteChrome.Topbar(brand, homeHref: null) + "<div class=\"export export--file\">");
        return Results.File(Encoding.UTF8.GetBytes(html), "text/html", $"{safeName}.html");
    }

    /// <summary>
    /// Where the sidecar reaches this app. Inside the compose network that is
    /// the service name, not the public address: a capture must not depend on
    /// the instance being reachable from the internet, or on its TLS.
    /// </summary>
    internal static string RenderOrigin(IConfiguration config) =>
        (config["Pdf:AppOrigin"] ?? "http://app:8080").TrimEnd('/');

    /// <summary>Makes a page title safe to use as a download filename.</summary>
    private static string SafeFileName(string title)
    {
        var cleaned = new string(title
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "page" : cleaned;
    }
}
