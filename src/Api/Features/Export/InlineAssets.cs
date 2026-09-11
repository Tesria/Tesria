using System.Text.Json;
using System.Text.Json.Nodes;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Rewrites a document's image sources to <c>data:</c> URIs before export.
///
/// An exported page referencing <c>/api/attachments/{id}/download</c> shows
/// broken images the moment it leaves the app — the URL is relative, and it
/// needs a session even when made absolute. Inlining is what makes "export"
/// mean "a file you can keep", and it is also what lets the PDF renderer run
/// with its network switched off entirely.
///
/// Bounded on purpose: a page of photographs would otherwise produce a file
/// nobody can email. Past the budget, images keep their URL and simply do
/// not render outside the app — the same as before.
/// </summary>
public static class InlineAssets
{
    /// <summary>Per-image and whole-document ceilings. Generous for diagrams and screenshots, not for a photo album.</summary>
    public const long MaxImageBytes = 4 * 1024 * 1024;
    public const long MaxTotalBytes = 20 * 1024 * 1024;

    public static async Task<string> InlineImagesAsync(
        string contentJson, AppDbContext db, IAttachmentStorage storage, CancellationToken ct)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(contentJson); }
        catch (JsonException) { return contentJson; }
        if (root is null) return contentJson;

        var images = new List<JsonObject>();
        Collect(root, images);
        if (images.Count == 0) return contentJson;

        // One lookup for every attachment the page references, rather than
        // one per image.
        var ids = images
            .Select(i => AttachmentIdOf(i["attrs"]?["src"]?.GetValue<string>()))
            .OfType<Guid>()
            .Distinct()
            .ToList();
        if (ids.Count == 0) return contentJson;

        var rows = await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.StorageKey, a.ContentType, a.Size })
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);

        var budget = MaxTotalBytes;
        foreach (var image in images)
        {
            var id = AttachmentIdOf(image["attrs"]?["src"]?.GetValue<string>());
            if (id is null || !byId.TryGetValue(id.Value, out var row)) continue;
            if (row.Size > MaxImageBytes || row.Size > budget) continue;
            // Only real image types: a data: URI of something else would be a
            // broken <img> at best.
            if (!row.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;

            await using var stream = storage.OpenRead(row.StorageKey);
            if (stream is null) continue;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            if (buffer.Length > MaxImageBytes || buffer.Length > budget) continue;

            budget -= buffer.Length;
            image["attrs"]!["src"] = $"data:{row.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
        }

        return root.ToJsonString();
    }

    private static void Collect(JsonNode node, List<JsonObject> found)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array) if (child is not null) Collect(child, found);
                break;
            case JsonObject obj:
                if (obj["type"]?.GetValue<string>() == "image" && obj["attrs"] is JsonObject) found.Add(obj);
                if (obj["content"] is { } content) Collect(content, found);
                break;
        }
    }

    /// <summary>The attachment id in <c>/api/attachments/{id}/download</c>, or null for any other source.</summary>
    private static Guid? AttachmentIdOf(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        var parts = src.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(parts, "attachments");
        return index >= 0 && index + 1 < parts.Length && Guid.TryParse(parts[index + 1], out var id) ? id : null;
    }
}
