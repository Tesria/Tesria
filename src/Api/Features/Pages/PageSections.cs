using System.Text.Json;
using System.Text.Json.Nodes;
using Tesria.Api.Features.Export;

namespace Tesria.Api.Features.Pages;

/// <summary>
/// One section of a page, addressed by the heading anchor Phase 7 Wave A
/// already derives (<c>HeadingAnchors</c>).
///
/// So an assistant can read the part it needs rather than the whole page:
/// fetching 5,000 words to answer a question about one heading is how a
/// context window gets spent. The anchors are the same ones a
/// <c>#fragment</c> link uses, so "the section this link points at" and
/// "the section to fetch" are the same thing.
/// </summary>
public static class PageSections
{
    public sealed record Heading(string Id, string Text, int Level);

    /// <summary>The page's headings, in document order, the map an assistant picks a section from.</summary>
    public static List<Heading> Outline(string contentJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            return HeadingAnchors.Collect(doc.RootElement)
                .Select(a => new Heading(a.Id, a.Text, a.Level))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The heading with this anchor plus everything under it, as its own
    /// document: up to the next heading of the same or a higher level, so
    /// "Deployment" brings its sub-headings with it but stops at the next
    /// peer.
    ///
    /// Top-level blocks only. A heading nested inside a panel, an expand or
    /// a layout column is part of that block's content, and slicing a
    /// document mid-container would produce something that is not a
    /// document. Those headings still appear in the outline; asking for one
    /// returns null rather than something malformed.
    /// </summary>
    public static string? Extract(string contentJson, string anchorId)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(contentJson); }
        catch (JsonException) { return null; }
        if (root?["content"] is not JsonArray blocks) return null;

        // Re-derive the ids over the whole document so duplicates are
        // numbered exactly as the anchors and the table of contents number
        // them: a section id must mean the same thing everywhere.
        using var doc = JsonDocument.Parse(contentJson);
        var anchors = HeadingAnchors.Collect(doc.RootElement);
        var wanted = anchors.FirstOrDefault(a => a.Id == anchorId);
        if (wanted is null) return null;

        // Walk the top level, tracking which heading each one is, so the
        // match is by id rather than by text (two headings can share text).
        var seenHeadings = 0;
        var startIndex = -1;
        var startLevel = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i]?["type"]?.GetValue<string>() != "heading") continue;
            // Only top-level headings advance a position we can slice at; the
            // anchor list includes nested ones, so match on identity instead.
            var id = IdOfNthTopLevelHeading(anchors, blocks, i);
            seenHeadings++;
            if (id != anchorId) continue;
            startIndex = i;
            startLevel = Level(blocks[i]);
            break;
        }
        if (startIndex < 0) return null;

        var slice = new JsonArray();
        for (var i = startIndex; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (i > startIndex && block?["type"]?.GetValue<string>() == "heading" && Level(block) <= startLevel) break;
            if (block is not null) slice.Add(block.DeepClone());
        }
        _ = seenHeadings;
        return new JsonObject { ["type"] = "doc", ["content"] = slice }.ToJsonString();
    }

    private static int Level(JsonNode? heading)
    {
        var raw = heading?["attrs"]?["level"];
        return raw is not null && int.TryParse(raw.ToString(), out var level) ? Math.Clamp(level, 1, 6) : 1;
    }

    /// <summary>
    /// The anchor id of the top-level heading at <paramref name="index"/>.
    /// The anchor list is in document order over *all* headings, so this
    /// counts how many headings (at any depth) precede this block.
    /// </summary>
    private static string? IdOfNthTopLevelHeading(
        IReadOnlyList<HeadingAnchors.Anchor> anchors, JsonArray blocks, int index)
    {
        var before = 0;
        for (var i = 0; i < index; i++) before += CountHeadings(blocks[i]);
        return before < anchors.Count ? anchors[before].Id : null;
    }

    private static int CountHeadings(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                return array.Sum(CountHeadings);
            case JsonObject obj:
                var own = obj["type"]?.GetValue<string>() == "heading" ? 1 : 0;
                return own + CountHeadings(obj["content"]);
            default:
                return 0;
        }
    }
}
