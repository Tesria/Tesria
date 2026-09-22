using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Turning a document from a pack into the document it must be on this
/// instance (dev-plan 8.5).
///
/// <para>Every id inside a pack belongs to the instance that wrote it, so an
/// import mints new ones and then has to go back through the content and say
/// the same things about the new ids. That is this class, and it is pure on
/// purpose: it is the part of an import that is easy to get subtly wrong and
/// hard to notice, so it is the part that gets fixture tests.</para>
///
/// <para>The governing rule is that a rewrite never invents a fact. Where the
/// pack refers to something the target does not have, the text a person wrote
/// survives and the machine-readable half is dropped, because a mention that
/// still reads as a name is worth more than one pointing at a stranger who
/// happens to hold that id here.</para>
/// </summary>
public static partial class PackRewriter
{
    /// <summary>
    /// What an import learned while it created rows, in the form the content
    /// needs it: old id to new id, plus the key the space landed under.
    /// </summary>
    public sealed record Maps(
        string TargetSpaceKey,
        IReadOnlyDictionary<Guid, Guid> Pages,
        IReadOnlyDictionary<Guid, Guid> Attachments,
        IReadOnlyDictionary<Guid, Guid> Comments)
    {
        public static Maps Empty(string key) => new(key, new Dictionary<Guid, Guid>(),
            new Dictionary<Guid, Guid>(), new Dictionary<Guid, Guid>());

        /// <summary>A page id as it is on the target, or null if it did not travel.</summary>
        public Guid? Page(Guid? old) =>
            old is { } id && Pages.TryGetValue(id, out var mapped) ? mapped : null;
    }

    /// <summary>
    /// Rewrites one stored document. The input is not modified: a pack may be
    /// read once and imported into several versions of the same page, and a
    /// rewriter that edited its argument would corrupt the second one.
    /// </summary>
    public static JsonNode Rewrite(JsonNode document, Maps maps)
    {
        var copy = JsonNode.Parse(document.ToJsonString())!;
        Walk(copy, maps);
        return copy;
    }

    /// <summary>
    /// Rewrites a plain-text body: comments, where someone may well have
    /// pasted a link to a page that is travelling with this one.
    /// </summary>
    public static string RewriteText(string text, Maps maps) => Urls(text, maps);

    private static void Walk(JsonNode? node, Maps maps)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array) Walk(item, maps);
                return;

            case JsonObject obj:
                var type = obj["type"]?.GetValue<string>();
                if (type is not null) Node(obj, type, maps);

                // Marks are rewritten from the node holding them, because a
                // comment mark can be dropped and a mark cannot drop itself.
                if (obj["marks"] is JsonArray marks) Marks(obj, marks, maps);

                foreach (var pair in obj.ToList())
                {
                    if (pair.Key == "marks") continue;
                    if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        // Any string anywhere: an href, an image src, a
                        // dynamic block's serialised params. Walking every
                        // string rather than an allow-list of attributes means
                        // a node type added later is rewritten too, which is
                        // the failure this would otherwise have every time
                        // somebody adds one.
                        var rewritten = Urls(text, maps);
                        if (rewritten != text) obj[pair.Key] = rewritten;
                    }
                    else Walk(pair.Value, maps);
                }
                return;
        }
    }

    private static void Node(JsonObject obj, string type, Maps maps)
    {
        if (obj["attrs"] is not JsonObject attrs) return;

        switch (type)
        {
            // The label is the mention; the id was only ever how the server
            // decided whom to notify. Keeping it would aim a notification at
            // whoever holds that id on this instance.
            case "mention":
                if (attrs.ContainsKey("userId")) attrs["userId"] = null;
                return;

            case "taskItem":
                if (attrs.ContainsKey("assigneeId")) attrs["assigneeId"] = null;
                return;

            // The question a dynamic block asks can name a page by id.
            case "dynamicBlock":
                if (attrs["params"] is JsonObject parameters
                    && parameters["page"] is JsonValue raw
                    && raw.TryGetValue<string>(out var text)
                    && Guid.TryParse(text, out var page)
                    && maps.Pages.TryGetValue(page, out var mapped))
                    parameters["page"] = mapped.ToString();
                return;
        }
    }

    private static void Marks(JsonObject owner, JsonArray marks, Maps maps)
    {
        var kept = new JsonArray();
        foreach (var mark in marks.ToList())
        {
            if (mark is not JsonObject obj) continue;
            marks.Remove(mark);

            if (obj["type"]?.GetValue<string>() == "comment")
            {
                var raw = obj["attrs"]?["commentId"];
                var id = raw is JsonValue value && value.TryGetValue<string>(out var text)
                    && Guid.TryParse(text, out var parsed) ? parsed : (Guid?)null;

                // A highlight with nothing behind it is worse than no
                // highlight: it colours the text and then says nothing when
                // it is clicked. The words stay; only the mark goes.
                if (id is null || !maps.Comments.TryGetValue(id.Value, out var comment)) continue;
                obj["attrs"]!["commentId"] = comment.ToString();
            }
            else Walk(obj, maps);

            kept.Add(obj);
        }

        // Dropped to nothing: the key goes too, so a document that gained
        // and lost a comment is byte-identical to one that never had it. The
        // pack's whole diffability rests on small habits like this one.
        if (kept.Count == 0) owner.Remove("marks");
        else owner["marks"] = kept;
    }

    /// <summary>
    /// The two link shapes this app writes into documents. A link to a page
    /// that is not in the pack is deliberately left alone: on a re-import into
    /// the same instance it still resolves, and a link that 404s honestly is
    /// better than one quietly pointed somewhere else. This is the opposite of
    /// the site export's rule (12.2), where the destination genuinely is not
    /// there and "#" is the honest answer.
    /// </summary>
    private static string Urls(string text, Maps maps)
    {
        var result = PageLink().Replace(text, match =>
        {
            var id = Guid.Parse(match.Groups["id"].Value);
            return maps.Pages.TryGetValue(id, out var mapped)
                ? $"/spaces/{maps.TargetSpaceKey}/pages/{mapped}{match.Groups["anchor"].Value}"
                : match.Value;
        });

        return AttachmentLink().Replace(result, match =>
        {
            var id = Guid.Parse(match.Groups["id"].Value);
            return maps.Attachments.TryGetValue(id, out var mapped)
                ? $"/api/attachments/{mapped}/download"
                : match.Value;
        });
    }

    [GeneratedRegex(@"/spaces/[A-Za-z0-9]+/pages/(?<id>[0-9a-fA-F-]{36})(?<anchor>#[^""'\s]*)?")]
    private static partial Regex PageLink();

    [GeneratedRegex(@"/api/attachments/(?<id>[0-9a-fA-F-]{36})/download")]
    private static partial Regex AttachmentLink();
}
