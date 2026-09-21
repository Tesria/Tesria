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

    /// <summary>Search text for a page: its title plus the plain text of its content.</summary>
    public static string BuildSearchText(string title, string contentJson) =>
        NormalizeForSearch($"{title} {ExtractPlainText(contentJson)}".Trim());

    /// <summary>
    /// Postgres's tsvector parser treats "word/word" as a single compound
    /// lexeme instead of splitting it, which makes each half unsearchable on
    /// its own. Replacing slashes with spaces before indexing lets
    /// to_tsvector tokenize both halves normally.
    /// </summary>
    private static string NormalizeForSearch(string text) => text.Replace('/', ' ');

    /// <summary>Concatenated text of every text node in a stored document.</summary>
    public static string ExtractPlainText(string contentJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            var sb = new StringBuilder();
            Walk(doc.RootElement, sb);
            return sb.ToString();
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        static void Walk(JsonElement el, StringBuilder sb)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    if (el.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        sb.Append(text.GetString()).Append(' ');
                    if (el.TryGetProperty("content", out var content))
                        Walk(content, sb);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                        Walk(item, sb);
                    break;
            }
        }
    }
}
