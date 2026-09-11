using System.Net;
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
    /// Exports a page as Markdown or standalone HTML. The HTML variant is
    /// print-ready, so "Print → Save as PDF" in the browser produces a PDF
    /// without shipping a headless-browser dependency in the image.
    /// </summary>
    private static async Task<IResult> ExportPage(
        Guid id, string? format, AppDbContext db, IPermissionService perms,
        IDynamicBlockService blocks, ISiteSettingsService settings, IConfiguration config, CancellationToken ct)
    {
        var page = await db.Pages.AsNoTracking()
            .Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page?.CurrentVersion is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();

        var content = page.CurrentVersion.ContentJson;
        var safeName = SafeFileName(page.Title);

        // Dynamic blocks are snapshotted now, as this caller, with this
        // caller's permissions (architecture.md, "Dynamic blocks", decision 5).
        var snapshot = await SnapshotBlocksAsync(page.Id, content, blocks, ct);
        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);

        return (format ?? "markdown").ToLowerInvariant() switch
        {
            "md" or "markdown" => File(
                $"# {page.Title}\n\n{ProseMirrorRenderer.ToMarkdown(content, snapshot, baseUrl)}",
                "text/markdown", $"{safeName}.md"),

            "html" => File(
                HtmlDocument(page.Title, ProseMirrorRenderer.ToHtml(content, snapshot, baseUrl, out var usedMermaid), usedMermaid),
                "text/html", $"{safeName}.html"),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["format"] = ["Supported formats are 'markdown' and 'html'."],
            }),
        };
    }

    /// <summary>
    /// One result per dynamic block in document order (null where a block
    /// failed or is unknown — the renderer draws a placeholder rather than
    /// failing the export). A parameter error in one block must not lose the
    /// rest of the page.
    /// </summary>
    private static async Task<List<BlockResult?>> SnapshotBlocksAsync(
        Guid hostId, string content, IDynamicBlockService blocks, CancellationToken ct)
    {
        var results = new List<BlockResult?>();
        foreach (var placement in DynamicBlocks.Collect(content))
        {
            try { results.Add(await blocks.RenderAsync(hostId, placement.Kind, placement.Params, ct)); }
            catch (BlockParamException) { results.Add(null); }
        }
        return results;
    }

    private static IResult File(string body, string contentType, string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(body), contentType, fileName);

    /// <summary>Wraps rendered content in a minimal, print-friendly HTML document.</summary>

    private const string MermaidScript =
        "<script type=\"module\">\n"
        + "import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';\n"
        + "mermaid.initialize({ startOnLoad: true, securityLevel: 'strict' });\n"
        + "</script>";

    /// <param name="withMermaid">
    /// Adds the one thing in an exported file that reaches the network: a
    /// Mermaid renderer from a CDN, included only when the page actually has
    /// a diagram (dev-plan Phase 7 Wave F).
    ///
    /// The trade is deliberate — the diagram *source* is already in the file
    /// as a readable &lt;pre&gt;, so a reader who is offline, who blocks the
    /// script, or who prints before it runs still sees the diagram's text.
    /// Nothing about the reader or the document is sent; it is a script
    /// fetch. Inlining Mermaid instead would add ~500KB to every exported
    /// file containing a diagram.
    /// </param>
    private static string HtmlDocument(string title, string bodyHtml, bool withMermaid = false)
    {
        var escapedTitle = WebUtility.HtmlEncode(title);
        var mermaidScript = withMermaid ? MermaidScript : "";
        // $$ raises the interpolation delimiter to {{ }} so the CSS braces below
        // are treated as literal text.
        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8" />
        <title>{{escapedTitle}}</title>
        <style>
          body { font: 16px/1.6 -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                 color: #172b4d; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; }
          h1, h2, h3 { line-height: 1.25; }
          pre { background: #f4f5f7; padding: 0.75rem; border-radius: 6px; overflow-x: auto; }
          code { background: #f4f5f7; padding: 0.1em 0.3em; border-radius: 4px; }
          pre code { background: none; padding: 0; }
          blockquote { border-left: 3px solid #e4e6eb; margin-left: 0; padding-left: 1rem; color: #6b778c; }
          @media print { body { margin: 0; max-width: none; } }
        </style>
        </head>
        <body>
        <h1>{{escapedTitle}}</h1>
        {{bodyHtml}}
        {{mermaidScript}}
        </body>
        </html>
        """;
    }

    /// <summary>Makes a page title safe to use as a download filename.</summary>
    private static string SafeFileName(string title)
    {
        var cleaned = new string(title
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "page" : cleaned;
    }
}
