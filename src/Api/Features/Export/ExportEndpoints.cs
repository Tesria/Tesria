using System.Net;
using System.Text;
using Tesria.Api.Infrastructure;
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
        Guid id, string? format, AppDbContext db, IPermissionService perms)
    {
        var page = await db.Pages.AsNoTracking()
            .Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page?.CurrentVersion is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();

        var content = page.CurrentVersion.ContentJson;
        var safeName = SafeFileName(page.Title);

        return (format ?? "markdown").ToLowerInvariant() switch
        {
            "md" or "markdown" => File(
                $"# {page.Title}\n\n{ProseMirrorRenderer.ToMarkdown(content)}",
                "text/markdown", $"{safeName}.md"),

            "html" => File(
                HtmlDocument(page.Title, ProseMirrorRenderer.ToHtml(content)),
                "text/html", $"{safeName}.html"),

            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["format"] = ["Supported formats are 'markdown' and 'html'."],
            }),
        };
    }

    private static IResult File(string body, string contentType, string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(body), contentType, fileName);

    /// <summary>Wraps rendered content in a minimal, print-friendly HTML document.</summary>
    private static string HtmlDocument(string title, string bodyHtml)
    {
        var escapedTitle = WebUtility.HtmlEncode(title);
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
