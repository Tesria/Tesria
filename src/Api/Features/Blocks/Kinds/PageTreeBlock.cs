using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// A page tree, rooted at the host or at the space. Same filtering as
/// <c>/api/pages/tree</c>: a hidden page takes its subtree with it, because
/// restrictions are inherited.
/// </summary>
public sealed class PageTreeBlock : IDynamicBlockKind
{
    public string Kind => "page-tree";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var root = ctx.Enum("root", "host", "host", "space");
        var depth = ctx.Int("depth", 3, 1, 6);

        var pages = await ctx.Db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == ctx.Host.SpaceId && p.Status == PageStatus.Current)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.ParentPageId })
            .ToListAsync(ct);
        var byParent = pages.ToLookup(p => p.ParentPageId);
        var spaceKey = ctx.Host.Space!.Key;

        async Task<List<BlockItem>> Build(Guid? parentId, int level)
        {
            var visible = await ctx.VisibleAsync(byParent[parentId], p => p.Id, int.MaxValue, ct);
            var items = new List<BlockItem>(visible.Count);
            foreach (var p in visible)
            {
                var children = level < depth ? await Build(p.Id, level + 1) : null;
                items.Add(new BlockItem(p.Title, ctx.HrefFor(spaceKey, p.Id), Children: children is { Count: > 0 } ? children : null));
            }
            return items;
        }

        var tree = await Build(root == "space" ? null : ctx.Host.Id, 1);
        return BlockResult.List(Kind, tree, empty: root == "space" ? "This space has no pages." : "This page has no sub-pages.");
    }
}
