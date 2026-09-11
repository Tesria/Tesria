using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>The host page's version history — cheap, because versions already exist.</summary>
public sealed class ChangeHistoryBlock : IDynamicBlockKind
{
    public string Kind => "change-history";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var limit = ctx.Int("limit", 10, 1, 50);

        var rows = await ctx.Db.PageVersions.AsNoTracking()
            .Where(v => v.PageId == ctx.Host.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Take(limit)
            .Select(v => new { v.VersionNumber, v.CreatedAt, v.ChangeComment, v.Author })
            .ToListAsync(ct);

        var items = rows.Select(v => new BlockItem($"v{v.VersionNumber}",
            Cells: new Dictionary<string, BlockCell>
            {
                ["version"] = BlockCell.Of($"v{v.VersionNumber}"),
                ["who"] = v.Author is null ? BlockCell.Of("") : BlockCell.By(BlockContext.UserOf(v.Author)),
                ["when"] = BlockCell.On(v.CreatedAt),
                ["comment"] = BlockCell.Of(v.ChangeComment ?? ""),
            })).ToList();

        return BlockResult.Table(Kind,
            [new("version", "Version"), new("who", "By"), new("when", "When"), new("comment", "What changed")],
            items, empty: "This page has no history yet.");
    }
}
