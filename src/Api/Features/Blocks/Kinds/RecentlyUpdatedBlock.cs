using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>Recently updated: the pages that changed most recently, and who changed them.</summary>
public sealed class RecentlyUpdatedBlock : IDynamicBlockKind
{
    public string Kind => "recently-updated";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var scope = ctx.Enum("scope", "space", "space", "tree");
        var limit = ctx.Int("limit", 10, 1, 50);

        // Candidates are already newest-first; VisibleAsync stops at `limit`
        // *visible* rows, so a run of restricted pages does not shorten the
        // list — it just means looking further down.
        var candidates = await PageScope.CandidatesAsync(ctx, scope, ct);
        var visible = await ctx.VisibleAsync(candidates, c => c.Id, limit, ct);

        var ids = visible.Select(v => v.Id).ToList();
        var authors = await ctx.Db.Pages.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.CurrentVersion != null)
            .Select(p => new { p.Id, p.CurrentVersion!.Author })
            .ToListAsync(ct);
        var authorByPage = authors.Where(a => a.Author is not null).ToDictionary(a => a.Id, a => a.Author!);

        var items = visible.Select(v => new BlockItem(v.Title, ctx.HrefFor(v.SpaceKey, v.Id),
            Cells: new Dictionary<string, BlockCell>
            {
                ["title"] = BlockCell.Of(v.Title, ctx.HrefFor(v.SpaceKey, v.Id)),
                ["who"] = authorByPage.TryGetValue(v.Id, out var author) ? BlockCell.By(BlockContext.UserOf(author)) : BlockCell.Of(""),
                ["when"] = BlockCell.On(v.UpdatedAt),
            })).ToList();

        return BlockResult.Table(Kind,
            [new("title", "Page"), new("who", "Updated by"), new("when", "When")],
            items, empty: "Nothing has been updated here yet.");
    }
}
