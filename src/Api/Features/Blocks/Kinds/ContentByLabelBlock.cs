using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>Content by label: pages carrying one or more labels.</summary>
public sealed class ContentByLabelBlock : IDynamicBlockKind
{
    public string Kind => "content-by-label";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        // Labels are stored lower-cased (LabelEndpoints), so match that here.
        var wanted = ctx.Required("labels")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (wanted.Count == 0) throw new BlockParamException("labels", "Name at least one label.");
        var match = ctx.Enum("match", "any", "any", "all");
        var scope = ctx.Enum("scope", "space", "space", "all");
        var limit = ctx.Int("limit", 25, 1, 100);

        var links = await ctx.Db.PageLabels.AsNoTracking()
            .Where(pl => wanted.Contains(pl.Label!.Name))
            // Join through Pages so the soft-delete filter excludes trashed pages.
            .Join(ctx.Db.Pages, pl => pl.PageId, p => p.Id, (pl, p) => new { p.Id, p.SpaceId, SpaceKey = p.Space!.Key, p.Title, p.Status, LabelName = pl.Label!.Name })
            .Where(x => x.Status == PageStatus.Current)
            .ToListAsync(ct);

        var pages = links
            .Where(x => scope == "all" || x.SpaceId == ctx.Host.SpaceId)
            .GroupBy(x => new { x.Id, x.SpaceKey, x.Title })
            .Where(g => match == "any" || wanted.All(w => g.Any(x => x.LabelName == w)))
            .OrderBy(g => g.Key.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var visible = await ctx.VisibleAsync(pages, g => g.Key.Id, limit, ct);
        var items = visible
            .Select(g => new BlockItem(g.Key.Title, ctx.HrefFor(g.Key.SpaceKey, g.Key.Id),
                Subtitle: string.Join(", ", g.Select(x => x.LabelName).Distinct().OrderBy(n => n))))
            .ToList();

        return BlockResult.List(Kind, items,
            empty: $"No pages are labeled {string.Join(match == "all" ? " and " : " or ", wanted)}.");
    }
}
