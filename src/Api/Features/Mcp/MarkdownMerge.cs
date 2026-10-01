using System.Text.Json.Nodes;
using Tesria.Api.Features.Export;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// Writes an assistant's Markdown back onto a page without flattening the
/// parts it did not touch (T5-003).
///
/// An assistant reads a page as Markdown (get_page), changes a few words,
/// and sends the whole Markdown back (update_page). Markdown cannot say
/// everything a page holds: a table of contents comes back as a list of
/// links, page properties as a plain table, a mention as "@name", a status
/// as code. Converting the whole Markdown therefore rewrote every rich block
/// on the page for a two-word edit (41 highlighted changes).
///
/// So the Markdown is compared with the page block by block. Each top-level
/// block of the current page is written as get_page wrote it, and both that
/// and the incoming Markdown are converted and written out again, so the two
/// sides are compared in the same, plain form. Where the assistant's Markdown
/// still says exactly what an existing block said, that block is kept as it
/// is, with everything Markdown could not carry; the rest is taken from the
/// Markdown. A block the assistant changed is still converted from its
/// Markdown, and loses what Markdown cannot say; the docs say so.
/// </summary>
public static class MarkdownMerge
{
    /// <summary>Beyond this many block comparisons the page is simply converted: the merge is a courtesy, not worth a stall.</summary>
    private const long MaxComparisons = 4_000_000;

    /// <param name="currentJson">The page as it is now.</param>
    /// <param name="currentMarkdown">
    /// Each of its top-level blocks as get_page wrote it
    /// (<see cref="ProseMirrorRenderer.ToMarkdownBlocks"/>), one per block.
    /// </param>
    /// <param name="incoming">The Markdown the assistant sent.</param>
    /// <returns>The document to store.</returns>
    public static string Merge(string currentJson, IReadOnlyList<string> currentMarkdown, string incoming)
    {
        // A live block's snapshot, between get_page's markers, stands for that
        // live block unchanged: its results may have moved on since the read,
        // and then its Markdown no longer matched and it was rebuilt from the
        // snapshot as a plain table (t5-R02, the 0.8.3 retest). Each marked
        // span becomes one placeholder paragraph, and each old live block the
        // same key, so they match whatever the results were.
        incoming = LiveSpan.Replace(incoming, "\n\n" + LivePlaceholder + "\n\n");
        var incomingBlocks = new JsonArray();
        foreach (var block in MarkdownToProseMirror.ConvertBlocks(incoming)) incomingBlocks.Add(block?.DeepClone());
        var oldBlocks = TopLevel(currentJson);
        if (oldBlocks is null || oldBlocks.Count == 0 || oldBlocks.Count != currentMarkdown.Count)
            return Doc(WithoutPlaceholders(incomingBlocks));

        // What each old block becomes after a trip through Markdown, as one
        // or more plain blocks, in the form the incoming side is compared in.
        var a = new List<(int Owner, string Key)>();
        for (var i = 0; i < oldBlocks.Count; i++)
        {
            if (oldBlocks[i]?["type"]?.GetValue<string>() == "dynamicBlock") { a.Add((i, LivePlaceholder)); continue; }
            foreach (var block in MarkdownToProseMirror.ConvertBlocks(currentMarkdown[i]))
                a.Add((i, Key(block!)));
        }
        var b = incomingBlocks.Select(block => Key(block!)).ToList();
        if (a.Count == 0 || b.Count == 0 || (long)a.Count * b.Count > MaxComparisons)
            return Doc(WithoutPlaceholders(incomingBlocks));

        var matchOfB = LongestCommonSubsequence(a.Select(x => x.Key).ToList(), b);

        // An old block is kept when every plain block it became is matched,
        // in a row, by the incoming side: it was sent back unchanged.
        var firstB = new int[oldBlocks.Count];
        var kept = new bool[oldBlocks.Count];
        var parts = new int[oldBlocks.Count];
        var matchedParts = new int[oldBlocks.Count];
        var lastB = new int[oldBlocks.Count];
        Array.Fill(firstB, -1);
        Array.Fill(lastB, -1);
        foreach (var (owner, _) in a) parts[owner]++;
        var consecutive = Enumerable.Repeat(true, oldBlocks.Count).ToArray();
        for (var j = 0; j < b.Count; j++)
        {
            if (matchOfB[j] is not { } ai) continue;
            var owner = a[ai].Owner;
            if (firstB[owner] < 0) firstB[owner] = j;
            else if (lastB[owner] != j - 1) consecutive[owner] = false;
            lastB[owner] = j;
            matchedParts[owner]++;
        }
        for (var i = 0; i < oldBlocks.Count; i++)
            kept[i] = parts[i] > 0 && matchedParts[i] == parts[i] && consecutive[i];

        // Blocks that write no Markdown at all (an empty paragraph between
        // two others, say) were never seen by the assistant, so they cannot
        // have been removed on purpose: they stay after the block before
        // them if that one was kept, or else before the block after them.
        // Only when both neighbors went do they go too.
        var before = new Dictionary<int, List<int>>();
        var after = new Dictionary<int, List<int>>();
        for (var i = 0; i < oldBlocks.Count; i++)
        {
            if (parts[i] > 0) continue;
            var previous = i - 1;
            while (previous >= 0 && parts[previous] == 0) previous--;
            var next = i + 1;
            while (next < oldBlocks.Count && parts[next] == 0) next++;
            if (previous >= 0 && kept[previous]) Attach(after, previous, i);
            else if (next < oldBlocks.Count && kept[next]) Attach(before, next, i);
        }

        static void Attach(Dictionary<int, List<int>> to, int owner, int block)
        {
            if (!to.TryGetValue(owner, out var list)) to[owner] = list = [];
            list.Add(block);
        }

        var output = new JsonArray();
        for (var j = 0; j < b.Count; j++)
        {
            if (matchOfB[j] is { } ai && kept[a[ai].Owner])
            {
                var owner = a[ai].Owner;
                if (firstB[owner] != j) continue;
                if (before.TryGetValue(owner, out var ahead))
                    foreach (var i in ahead) output.Add(oldBlocks[i]!.DeepClone());
                output.Add(oldBlocks[owner]!.DeepClone());
                if (after.TryGetValue(owner, out var behind))
                    foreach (var i in behind) output.Add(oldBlocks[i]!.DeepClone());
                continue;
            }
            // A placeholder with no live block to stand for (the assistant
            // copied the markers) writes nothing.
            if (b[j] == LivePlaceholder) continue;
            output.Add(incomingBlocks[j]!.DeepClone());
        }
        return Doc(output);
    }

    /// <summary>A marked live block's span in Markdown from get_page.</summary>
    private static readonly System.Text.RegularExpressions.Regex LiveSpan = new(
        @"<!--\s*tesria-live\b[^>]*-->[\s\S]*?<!--\s*/tesria-live\s*-->",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>What a live block's span is compared as. Not something anyone writes.</summary>
    private const string LivePlaceholder = "TESRIALIVEBLOCKPLACEHOLDER";

    private static JsonArray WithoutPlaceholders(JsonArray blocks)
    {
        var kept = new JsonArray();
        foreach (var block in blocks)
            if (block is not null && Key(block) != LivePlaceholder) kept.Add(block.DeepClone());
        return kept;
    }

    /// <summary>A block's plain Markdown, the form both sides are compared in.</summary>
    private static string Key(JsonNode block) =>
        ProseMirrorRenderer.ToMarkdown(new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray(block.DeepClone()),
        }.ToJsonString()).TrimEnd();

    private static JsonArray? TopLevel(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject doc && doc["content"] is JsonArray content ? content : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string Doc(JsonArray blocks)
    {
        var content = new JsonArray();
        foreach (var block in blocks) content.Add(block?.DeepClone());
        // ProseMirror's schema requires at least one block.
        if (content.Count == 0)
            content.Add(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray() });
        return new JsonObject { ["type"] = "doc", ["content"] = content }.ToJsonString();
    }

    /// <summary>For each item of <paramref name="b"/>, the index in <paramref name="a"/> it is paired with, if any.</summary>
    private static int?[] LongestCommonSubsequence(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var lengths = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lengths[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var match = new int?[b.Count];
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal)) { match[y] = x; x++; y++; }
            else if (lengths[x + 1, y] >= lengths[x, y + 1]) x++;
            else y++;
        }
        return match;
    }
}
