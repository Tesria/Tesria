using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// Children display: the host page's live children, to a depth. The
/// reference kind: built together with the mechanism so the contract is
/// proven against something real. It is deliberately the simplest one, and
/// it still has to get permission filtering right: a hidden child hides its
/// whole subtree, the same rule the page tree applies.
/// </summary>
public sealed class ChildrenBlock : IDynamicBlockKind
{
    public string Kind => "children";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var depth = ctx.Int("depth", 1, 1, 3);
        var sort = ctx.Enum("sort", "position", "position", "title", "updated");

        // One query for the whole space, then walk in memory: a tree of any
        // depth is one round trip, and the visibility check runs per page
        // anyway. The soft-delete filter and Status keep drafts and trash out.
        var pages = await ctx.Db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == ctx.Host.SpaceId && p.Status == PageStatus.Current)
            .Select(p => new { p.Id, p.Title, p.Position, p.ParentPageId, p.UpdatedAt })
            .ToListAsync(ct);
        var byParent = pages.ToLookup(p => p.ParentPageId);
        var spaceKey = ctx.Host.Space!.Key;

        async Task<List<BlockItem>> Build(Guid parentId, int level)
        {
            var siblings = sort switch
            {
                "title" => byParent[parentId].OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase),
                "updated" => byParent[parentId].OrderByDescending(p => p.UpdatedAt),
                _ => byParent[parentId].OrderBy(p => p.Position).ThenBy(p => p.Title),
            };
            // No limit on children: a page's children are a bounded set the
            // author controls; int.MaxValue keeps the helper's filtering.
            var visible = await ctx.VisibleAsync(siblings, p => p.Id, int.MaxValue, ct);
            var items = new List<BlockItem>(visible.Count);
            foreach (var p in visible)
            {
                var children = level < depth ? await Build(p.Id, level + 1) : null;
                items.Add(new BlockItem(p.Title, ctx.HrefFor(spaceKey, p.Id), Children: children is { Count: > 0 } ? children : null));
            }
            return items;
        }

        var result = await Build(ctx.Host.Id, 1);
        return BlockResult.List(Kind, result, empty: "This page has no sub-pages.");
    }
}
