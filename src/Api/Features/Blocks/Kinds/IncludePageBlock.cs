using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// Include page (and Excerpt include, which differs only in taking one
/// marked-off piece of the source rather than all of it).
///
/// A page the caller cannot view is reported as "nothing to show", never as
/// an error naming the page: an error would tell them the page exists,
/// which is exactly what the 404 masking rule elsewhere prevents. The
/// included content's own dynamic blocks are rendered as placeholders on
/// both sides (architecture.md, decision 6), so an include of an include
/// cannot recurse.
/// </summary>
public class IncludePageBlock : IDynamicBlockKind
{
    public virtual string Kind => "include-page";

    /// <summary>Null to include the whole page; a node type to include the first one of those.</summary>
    protected virtual string? FragmentNodeType => null;

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var raw = ctx.Required("page");
        if (!Guid.TryParse(raw, out var pageId))
            throw new BlockParamException("page", "Choose a page to include.");

        const string missing = "Nothing to show: the page is missing, or you cannot see it.";
        if (pageId == ctx.Host.Id)
            return BlockResult.DocumentOf(Kind, null, "A page cannot include itself.");
        if (!await ctx.Perms.CanViewPageAsync(pageId))
            return BlockResult.DocumentOf(Kind, null, missing);

        var content = await ctx.Db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId)
            .Select(p => p.CurrentVersion!.ContentJson)
            .FirstOrDefaultAsync(ct);
        if (content is null) return BlockResult.DocumentOf(Kind, null, missing);

        if (FragmentNodeType is null) return BlockResult.DocumentOf(Kind, content, missing);

        var fragment = BlockDocuments.FirstNodeAsDocument(content, FragmentNodeType);
        return BlockResult.DocumentOf(Kind, fragment, "That page has no excerpt to include.");
    }
}

/// <summary>Excerpt include: the first <c>excerpt</c> block of the chosen page.</summary>
public sealed class ExcerptIncludeBlock : IncludePageBlock
{
    public override string Kind => "excerpt-include";
    protected override string? FragmentNodeType => "excerpt";
}
