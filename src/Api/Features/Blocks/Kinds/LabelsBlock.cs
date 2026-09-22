using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// Labels: this page's, the space's most-used, or the ones that co-occur
/// with this page's. Counts are over *visible* pages only: a label whose
/// only pages are restricted must not appear at all, and a count that
/// included them would leak how many there are.
/// </summary>
public sealed class LabelsBlock : IDynamicBlockKind
{
    public string Kind => "labels";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var mode = ctx.Enum("mode", "page", "page", "popular", "related");
        var limit = ctx.Int("limit", 20, 1, 100);

        if (mode == "page")
        {
            var names = await ctx.Db.PageLabels.AsNoTracking()
                .Where(pl => pl.PageId == ctx.Host.Id)
                .Select(pl => pl.Label!.Name)
                .OrderBy(n => n)
                .ToListAsync(ct);
            return BlockResult.List(Kind,
                names.Take(limit).Select(n => new BlockItem(n, $"/labels/{Uri.EscapeDataString(n)}")).ToList(),
                empty: "This page has no labels.");
        }

        // Both remaining modes count labels across the space, so they need the
        // same visible-page set first.
        var candidates = await PageScope.CandidatesAsync(ctx, "space", ct);
        var visibleIds = (await ctx.VisibleAsync(candidates, c => c.Id, int.MaxValue, ct))
            .Select(c => c.Id).ToHashSet();

        var links = await ctx.Db.PageLabels.AsNoTracking()
            .Where(pl => visibleIds.Contains(pl.PageId))
            .Select(pl => new { pl.PageId, Name = pl.Label!.Name })
            .ToListAsync(ct);

        IEnumerable<IGrouping<string, string>> groups;
        string empty;
        if (mode == "popular")
        {
            groups = links.GroupBy(l => l.Name, l => l.Name);
            empty = "No labels are in use here yet.";
        }
        else
        {
            var own = links.Where(l => l.PageId == ctx.Host.Id).Select(l => l.Name).ToHashSet();
            var neighbours = links.Where(l => own.Contains(l.Name)).Select(l => l.PageId).ToHashSet();
            // Labels sharing a page with one of ours, minus our own.
            groups = links.Where(l => neighbours.Contains(l.PageId) && !own.Contains(l.Name))
                .GroupBy(l => l.Name, l => l.Name);
            empty = own.Count == 0 ? "This page has no labels to relate to." : "No related labels yet.";
        }

        var items = groups
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(g => new BlockItem(g.Key, $"/labels/{Uri.EscapeDataString(g.Key)}",
                Subtitle: g.Count() == 1 ? "1 page" : $"{g.Count()} pages"))
            .ToList();

        return BlockResult.List(Kind, items, empty: empty);
    }
}
