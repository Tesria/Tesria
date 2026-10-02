using System.Text;
using System.Text.Json;

namespace Tesria.Api.Features.Pages;

/// <summary>
/// The two things everything writing a page needs: a validated document and
/// the search text derived from it. Shared by <see cref="PageEndpoints"/>
/// and <see cref="PageWriter"/> so a page written through any door is
/// indexed the same way.
/// </summary>
public static class PageContent
{
    /// <summary>An empty ProseMirror document; used when a page is created without content.</summary>
    public const string EmptyDoc = """{"type":"doc","content":[]}""";

    /// <summary>
    /// Tracked-change marks (dev-plan 8.6). They belong to a live draft, never
    /// to a stored version, and they are stripped here rather than trusted to
    /// be accepted first: see <see cref="StripExternalMarks"/>.
    /// </summary>
    private static readonly string[] ExternalMarks = ["externalInsert", "externalDelete"];

    public static bool TryNormalize(string? input, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            normalized = EmptyDoc;
            return true;
        }
        try
        {
            using var parsed = JsonDocument.Parse(input);
            // Every door into a page version comes through here, which is why
            // the strip lives here and not in one of the callers.
            //
            // Almost no document has these marks, and a page save is hot, so
            // the walk is skipped unless the name appears in the raw text at
            // all. A substring that is not there cannot be a mark type that
            // is; the check can only be falsely positive, never negative, and
            // a false positive just does the walk it would have done anyway.
            normalized = MayHaveExternalMarks(input)
                         && StripExternalMarks(parsed.RootElement, out var stripped)
                         && stripped is not null
                ? stripped
                : input;
            return true;
        }
        catch (JsonException)
        {
            normalized = EmptyDoc;
            return false;
        }
    }

    /// <summary>
    /// Why a document is not one the editor can show, or null when it is
    /// (T5-023). A document the editor cannot load showed as an empty page,
    /// and the next Update from the editor saved it empty, so whatever the
    /// caller sent was lost without a word. This checks what makes the
    /// editor refuse a whole document: the root is a <c>doc</c>, every
    /// element and mark is one the editor has, text is text, and content
    /// and marks are lists. It does not check which element may hold which;
    /// the editor is forgiving about that. The names are the ones pack import
    /// checks too (<see cref="Export.EditorSchema"/>, t6-015), which a web test
    /// keeps in step with the editor.
    /// </summary>
    public static string? Problem(string normalized)
    {
        try
        {
            using var parsed = JsonDocument.Parse(normalized);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != "doc")
                return "Content must be a document: {\"type\":\"doc\",\"content\":[...]}.";
            return NodeProblem(root, Export.EditorSchema.NodeTypes, Export.EditorSchema.MarkTypes, depth: 0);
        }
        catch (JsonException)
        {
            return "Content must be valid JSON.";
        }
    }

    private static string? NodeProblem(JsonElement node, IReadOnlySet<string> nodes, IReadOnlySet<string> marks, int depth)
    {
        // Deeper than any page anyone has written; also keeps a hostile
        // document from walking the stack.
        if (depth > 200) return "Content is nested too deeply.";
        if (node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            return "Every element in the content needs a \"type\".";
        var type = typeElement.GetString()!;
        if (!nodes.Contains(type)) return $"Content has an element Tesria does not know: \"{type}\".";
        if (type == "doc" && depth > 0) return "Content has a document inside a document.";
        if (type == "text"
            && (!node.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String || text.GetString()!.Length == 0))
            return "Every text element needs a non-empty \"text\".";

        if (node.TryGetProperty("marks", out var markList))
        {
            if (markList.ValueKind != JsonValueKind.Array) return $"The marks of a \"{type}\" must be a list.";
            foreach (var mark in markList.EnumerateArray())
            {
                if (mark.ValueKind != JsonValueKind.Object
                    || !mark.TryGetProperty("type", out var markType) || markType.ValueKind != JsonValueKind.String)
                    return "Every mark in the content needs a \"type\".";
                if (!marks.Contains(markType.GetString()!))
                    return $"Content has a mark Tesria does not know: \"{markType.GetString()}\".";
            }
        }

        var childTypes = new List<string>();
        if (node.TryGetProperty("content", out var content))
        {
            if (content.ValueKind != JsonValueKind.Array) return $"The content of a \"{type}\" must be a list.";
            foreach (var child in content.EnumerateArray())
            {
                if (NodeProblem(child, nodes, marks, depth + 1) is { } problem) return problem;
                childTypes.Add(child.GetProperty("type").GetString()!);
            }
        }
        // Where each element may go, not only its name (t5-R05). An empty
        // text block is fine; an empty list or table is not.
        // A document with no blocks at all is left alone: the editor gives it
        // an empty paragraph, and scripts have always been able to send one.
        if (type == "doc" && childTypes.Count == 0) return null;
        return Export.EditorSchema.ChildrenProblem(type, childTypes);
    }

    private static bool MayHaveExternalMarks(string json) =>
        json.Contains("external", StringComparison.Ordinal);

    /// <summary>
    /// Removes <c>externalInsert</c> and <c>externalDelete</c> marks from a
    /// document, keeping the text they were on.
    ///
    /// <para>These marks say "an API or MCP write changed this and a human has
    /// not decided yet". A published version is a decision, so it cannot carry
    /// them. The editor normally resolves them before publishing
    /// (<c>acceptExternalEdits</c>), but a client that forgot, an older client,
    /// or a script that copied a draft out of the collaboration document would
    /// otherwise store them, and they would then be invisible highlighting on
    /// a real page forever.</para>
    ///
    /// <para>The text is kept on purpose, including text marked
    /// <c>externalDelete</c>. Stripping here is a safety net, not a merge: it
    /// cannot know whether the human meant to accept, and silently deleting
    /// their words would be the worse guess. What the marks meant is preserved
    /// in the draft until somebody resolves it there.</para>
    ///
    /// <returns>True when anything was removed, with the rewritten JSON.</returns>
    /// </summary>
    public static bool StripExternalMarks(JsonElement root, out string? rewritten)
    {
        var changed = false;
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Copy(root, writer, ref changed);
        }
        rewritten = changed ? Encoding.UTF8.GetString(buffer.ToArray()) : null;
        return changed;
    }

    private static void Copy(JsonElement element, Utf8JsonWriter writer, ref bool changed)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("marks") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var kept = property.Value.EnumerateArray().Where(IsKeptMark).ToList();
                        if (kept.Count != property.Value.GetArrayLength()) changed = true;
                        // A node with no marks left drops the property rather
                        // than carrying an empty array, which is what the
                        // editor itself produces for unmarked text.
                        if (kept.Count == 0) continue;
                        writer.WritePropertyName("marks");
                        writer.WriteStartArray();
                        foreach (var mark in kept) mark.WriteTo(writer);
                        writer.WriteEndArray();
                        continue;
                    }
                    writer.WritePropertyName(property.Name);
                    Copy(property.Value, writer, ref changed);
                }
                writer.WriteEndObject();
                return;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Copy(item, writer, ref changed);
                writer.WriteEndArray();
                return;

            default:
                element.WriteTo(writer);
                return;
        }
    }

    private static bool IsKeptMark(JsonElement mark) =>
        !(mark.ValueKind == JsonValueKind.Object
          && mark.TryGetProperty("type", out var type)
          && type.ValueKind == JsonValueKind.String
          && ExternalMarks.Contains(type.GetString()));

    /// <summary>
    /// Search text for a page: the words of its content, block by block
    /// (<see cref="Search.SearchableText"/>). The title is indexed from its
    /// own column, weighted above the body (dev-plan 23.1).
    /// </summary>
    public static string BuildSearchText(string contentJson) => Search.SearchableText.Extract(contentJson);
}
