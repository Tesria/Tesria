using Tesria.Api.Features.Blocks;

namespace Tesria.Api.Features.Export;

/// <summary>
/// "This page, as this caller would see it" — the dynamic blocks resolved
/// with the caller's own permissions and handed to the renderer in document
/// order. Shared by the export endpoint and the MCP <c>get_page</c> tool so
/// an assistant reads exactly what the export would say (dev-plan 8.4,
/// decision 4).
/// </summary>
public static class PageSnapshots
{
    /// <summary>One result per dynamic block in document order; null where a block failed, so one bad block never loses the page.</summary>
    public static async Task<List<BlockResult?>> BlocksAsync(
        Guid hostPageId, string contentJson, IDynamicBlockService blocks, CancellationToken ct)
    {
        var results = new List<BlockResult?>();
        foreach (var placement in DynamicBlocks.Collect(contentJson))
        {
            try { results.Add(await blocks.RenderAsync(hostPageId, placement.Kind, placement.Params, ct)); }
            catch (BlockParamException) { results.Add(null); }
        }
        return results;
    }
}
