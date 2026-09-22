using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks.Kinds;

/// <summary>
/// The host page's attachments. Host-anchored, so the caller's right to see
/// them was settled by the right to see the host: no second filter.
/// </summary>
public sealed class AttachmentsBlock : IDynamicBlockKind
{
    public string Kind => "attachments";

    public async Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct)
    {
        var rows = await ctx.Db.Attachments.AsNoTracking()
            .Where(a => a.PageId == ctx.Host.Id)
            .OrderBy(a => a.Filename)
            .Select(a => new { a.Id, a.Filename, a.Size, a.CreatedAt, a.UploadedBy })
            .ToListAsync(ct);

        var items = rows.Select(a => new BlockItem(a.Filename,
            Cells: new Dictionary<string, BlockCell>
            {
                // The API path, not an app route: a download is a file, and it
                // is the same URL the attachments panel uses.
                ["name"] = BlockCell.Of(a.Filename, $"/api/attachments/{a.Id}/download"),
                ["size"] = BlockCell.Of(HumanSize(a.Size)),
                ["who"] = a.UploadedBy is null ? BlockCell.Of("") : BlockCell.By(BlockContext.UserOf(a.UploadedBy)),
                ["when"] = BlockCell.On(a.CreatedAt),
            })).ToList();

        return BlockResult.Table(Kind,
            [new("name", "File"), new("size", "Size"), new("who", "Uploaded by"), new("when", "When")],
            items, empty: "This page has no attachments.");
    }

    private static string HumanSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
