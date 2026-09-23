using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// Page properties report: one row per labeled page, one column per
/// property key found on those pages. The keys come from the pages
/// themselves (the union of every visible page's first-column keys, in the
/// order they are first seen) so adding a property to a page adds a column
/// to the report without touching the report.
/// </summary>
public sealed class PagePropertiesReportBlock : IDynamicBlockKind
{
    public string Kind => "page-properties-report";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var wanted = ctx.Required("labels")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (wanted.Count == 0) throw new BlockParamException("labels", "Name at least one label.");
        var limit = ctx.Int("limit", 25, 1, 100);

        var labeled = await ctx.Db.PageLabels.AsNoTracking()
            .Where(pl => wanted.Contains(pl.Label!.Name))
            .Join(ctx.Db.Pages, pl => pl.PageId, p => p.Id, (pl, p) => p)
            .Where(p => p.Status == PageStatus.Current)
            .Select(p => new { p.Id, SpaceKey = p.Space!.Key, p.Title, Content = p.CurrentVersion!.ContentJson })
            .Distinct()
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

        var visible = await ctx.VisibleAsync(labeled, p => p.Id, limit, ct);

        var columns = new List<BlockColumn> { new("page", "Page") };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<BlockItem>();
        foreach (var page in visible)
        {
            var properties = BlockDocuments.Properties(page.Content);
            if (properties.Count == 0) continue;

            var cells = new Dictionary<string, BlockCell>
            {
                ["page"] = BlockCell.Of(page.Title, ctx.HrefFor(page.SpaceKey, page.Id)),
            };
            foreach (var property in properties)
            {
                if (seen.Add(property.Key)) columns.Add(new BlockColumn(property.Key, property.Key));
                cells[property.Key] = BlockCell.Of(property.Value);
            }
            items.Add(new BlockItem(page.Title, ctx.HrefFor(page.SpaceKey, page.Id), Cells: cells));
        }

        return BlockResult.Table(Kind, columns, items,
            empty: "No labeled page has a page-properties table yet.");
    }
}
