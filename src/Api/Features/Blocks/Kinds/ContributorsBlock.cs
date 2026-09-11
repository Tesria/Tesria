using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// Who has edited this page (or this subtree), most edits first. Aggregates
/// over the *visible* pages only: a contributor whose only edits are on a
/// restricted page must not appear, and neither must their edit count.
/// </summary>
public sealed class ContributorsBlock : IDynamicBlockKind
{
    public string Kind => "contributors";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var scope = ctx.Enum("scope", "page", "page", "tree");

        List<Guid> pageIds;
        if (scope == "page")
        {
            pageIds = [ctx.Host.Id];
        }
        else
        {
            var candidates = await PageScope.CandidatesAsync(ctx, "tree", ct);
            var visible = await ctx.VisibleAsync(candidates, c => c.Id, int.MaxValue, ct);
            pageIds = visible.Select(v => v.Id).ToList();
        }

        var versions = await ctx.Db.PageVersions.AsNoTracking()
            .Where(v => pageIds.Contains(v.PageId))
            .Select(v => new { v.AuthorId, v.Author })
            .ToListAsync(ct);

        var items = versions
            .Where(v => v.Author is not null)
            .GroupBy(v => v.AuthorId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.First().Author!.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new BlockItem(g.First().Author!.DisplayName,
                Subtitle: g.Count() == 1 ? "1 edit" : $"{g.Count()} edits"))
            .ToList();

        return BlockResult.List(Kind, items, empty: "Nobody has edited this yet.");
    }
}
