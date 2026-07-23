using System.Text.RegularExpressions;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Labels;

public static partial class LabelEndpoints
{
    public record AddLabelRequest(string Name);
    public record LabelResponse(Guid Id, string Name);
    public record LabelUsageResponse(Guid Id, string Name, int PageCount);
    public record LabelledPageResponse(Guid PageId, Guid SpaceId, string SpaceKey, string Title);

    // 1–50 chars: letters, digits, dash, underscore, dot. Stored lower-cased.
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,49}$")]
    private static partial Regex NamePattern();

    public static IEndpointRouteBuilder MapLabelEndpoints(this IEndpointRouteBuilder routes)
    {
        var labels = routes.MapGroup("/labels").WithTags("Labels").RequireAuthorization();
        labels.MapGet("/", ListAll);
        labels.MapGet("/{name}/pages", PagesForLabel);

        var pageScoped = routes.MapGroup("/pages/{pageId:guid}/labels")
            .WithTags("Labels").RequireAuthorization();
        pageScoped.MapGet("/", ListForPage);
        pageScoped.MapPost("/", AddToPage);
        pageScoped.MapDelete("/{name}", RemoveFromPage);

        return routes;
    }

    /// <summary>All labels in use, with how many (live) pages carry each.</summary>
    private static async Task<IResult> ListAll(AppDbContext db)
    {
        // Counting through Page applies the soft-delete filter, so trashed pages
        // don't inflate the counts.
        var rows = await db.PageLabels
            .Where(pl => db.Pages.Any(p => p.Id == pl.PageId))
            .GroupBy(pl => new { pl.LabelId, pl.Label!.Name })
            .Select(g => new LabelUsageResponse(g.Key.LabelId, g.Key.Name, g.Count()))
            .ToListAsync();
        return Results.Ok(rows.OrderBy(r => r.Name));
    }

    private static async Task<IResult> ListForPage(Guid pageId, AppDbContext db)
    {
        if (!await db.Pages.AnyAsync(p => p.Id == pageId)) return Results.NotFound();
        var labels = await db.PageLabels
            .Where(pl => pl.PageId == pageId)
            .Select(pl => new LabelResponse(pl.LabelId, pl.Label!.Name))
            .ToListAsync();
        return Results.Ok(labels.OrderBy(l => l.Name));
    }

    private static async Task<IResult> AddToPage(
        Guid pageId, AddLabelRequest req, AppDbContext db, CurrentUser current)
    {
        var name = (req.Name ?? "").Trim().ToLowerInvariant();
        if (!NamePattern().IsMatch(name))
            return Results.ValidationProblem(Error("name",
                "Labels are 1–50 characters: lower-case letters, digits, dot, dash or underscore, starting with a letter or digit."));

        if (!await db.Pages.AnyAsync(p => p.Id == pageId)) return Results.NotFound();

        // Create the label on first use, then attach it to the page.
        var label = await db.Labels.FirstOrDefaultAsync(l => l.Name == name);
        if (label is null)
        {
            label = new Label { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
            db.Labels.Add(label);
        }

        var already = await db.PageLabels.AnyAsync(pl => pl.PageId == pageId && pl.LabelId == label.Id);
        if (!already)
        {
            db.PageLabels.Add(new PageLabel
            {
                PageId = pageId,
                LabelId = label.Id,
                AddedById = current.RequireId(),
                AddedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return Results.Ok(new LabelResponse(label.Id, label.Name));
    }

    private static async Task<IResult> RemoveFromPage(Guid pageId, string name, AppDbContext db)
    {
        var normalized = (name ?? "").Trim().ToLowerInvariant();
        var link = await db.PageLabels
            .FirstOrDefaultAsync(pl => pl.PageId == pageId && pl.Label!.Name == normalized);
        if (link is null) return Results.NotFound();

        db.PageLabels.Remove(link);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Live pages carrying a label — the "browse by tag" view.</summary>
    private static async Task<IResult> PagesForLabel(string name, AppDbContext db)
    {
        var normalized = (name ?? "").Trim().ToLowerInvariant();
        var pages = await db.PageLabels
            .Where(pl => pl.Label!.Name == normalized)
            // Join through Pages so the soft-delete filter excludes trashed pages.
            .Join(db.Pages, pl => pl.PageId, p => p.Id, (pl, p) => p)
            .Select(p => new LabelledPageResponse(p.Id, p.SpaceId, p.Space!.Key, p.Title))
            .ToListAsync();
        return Results.Ok(pages.OrderBy(p => p.Title));
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
