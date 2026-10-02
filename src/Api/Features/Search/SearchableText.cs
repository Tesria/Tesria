using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tesria.Api.Features.Search;

/// <summary>
/// The words of a page as a reader sees them, for the search index and its
/// snippets (<c>Page.SearchText</c>). The title is not included: it has a
/// column of its own, which the index weighs above the body (<see cref="Bm25"/>).
///
/// <para>Blocks (paragraphs, headings, table cells, list items, lines of
/// code) are separated by a line break, so a snippet can show where one ends
/// and the next begins, and the text inside a block is joined exactly as
/// written: "see Label pages." and not "see Label pages ." (T9-016). Chips
/// count as text, as the page shows them: a mention as @Name, a status by its
/// word, a date as "14 Oct 2026" (T5-026). A Mermaid diagram counts by the
/// words in its boxes and arrows rather than its source (t6-021).</para>
/// </summary>
public static partial class SearchableText
{
    /// <summary>
    /// Bumped whenever what this produces changes, so the migrate step
    /// re-extracts every page once (<see cref="SearchTextBackfill"/>).
    /// 1: 0.9 (dev-plan 23.1), title left out, blocks separated, chips and diagrams as shown.
    /// </summary>
    public const int Version = 1;

    /// <summary>The plain text of a stored document, block by block; empty when it is not JSON.</summary>
    public static string Extract(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            var writer = new Writer();
            writer.Walk(doc.RootElement);
            // Slashes are kept, so a snippet shows a path or an address as
            // written; the index reads them as spaces (SearchIndex.Fold).
            return writer.ToString();
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();

        public override string ToString() => _sb.ToString().TrimEnd('\n');

        /// <summary>Ends the current block, once, however many blocks end together.</summary>
        private void Break()
        {
            while (_sb.Length > 0 && _sb[^1] == ' ') _sb.Length--;
            if (_sb.Length > 0 && _sb[^1] != '\n') _sb.Append('\n');
        }

        private void Block(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0) return;
            Break();
            _sb.Append(trimmed);
            Break();
        }

        public void Walk(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            switch (TypeOf(node))
            {
                case "text":
                    if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        _sb.Append(text.GetString());
                    return;
                case "hardBreak":
                    Break();
                    return;
                case "mention":
                    _sb.Append('@').Append(Attr(node, "label") is { Length: > 0 } label ? label.Trim() : "Unknown user");
                    return;
                case "status":
                    _sb.Append(Attr(node, "text") is { Length: > 0 } status ? status.Trim() : "STATUS");
                    return;
                case "date":
                    _sb.Append(DateText(Attr(node, "date")));
                    return;
                case "codeBlock":
                    var source = Children(node).Aggregate(new StringBuilder(), (sb, child) =>
                        child.ValueKind == JsonValueKind.Object && child.TryGetProperty("text", out var t)
                        && t.ValueKind == JsonValueKind.String ? sb.Append(t.GetString()) : sb).ToString();
                    var lines = string.Equals(Attr(node, "language"), "mermaid", StringComparison.OrdinalIgnoreCase)
                        ? MermaidLabels(source)
                        : source.Split('\n');
                    foreach (var line in lines) Block(line);
                    return;
                case "expand":
                    if (Attr(node, "title") is { } expandTitle) Block(expandTitle);
                    break;
                case "image":
                    if (Attr(node, "caption") is { } caption) Block(caption);
                    return;
                // Nothing a reader reads as words: generated, drawn or a file.
                case "math" or "chart" or "dynamicBlock" or "tableOfContents" or "attachmentBlock"
                    or "embed" or "smartLink" or "smartLinkInline" or "horizontalRule":
                    return;
            }

            var isTextBlock = TypeOf(node) is "paragraph" or "heading";
            if (isTextBlock) Break();
            foreach (var child in Children(node)) Walk(child);
            Break();
        }
    }

    /// <summary>
    /// The words a reader sees in a Mermaid diagram: what is inside its boxes
    /// (<c>A[Laptop]</c>, <c>B(Sync service)</c>, <c>C[(Storage)]</c>), on
    /// its arrows (<c>-->|change|</c>), in quotes, after a colon (a sequence
    /// diagram's messages, a timeline's events) and its titles and sections.
    /// The keywords, node ids and arrows are syntax and are left out. A
    /// heuristic rather than a parser: a diagram it does not understand
    /// contributes fewer words, never its source.
    /// </summary>
    public static IEnumerable<string> MermaidLabels(string source)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal) || MermaidStyling().IsMatch(line)) continue;

            var heading = MermaidHeading().Match(line);
            if (heading.Success)
            {
                yield return heading.Groups["text"].Value;
                continue;
            }

            // In reading order: quoted text first claims its stretch of the
            // line, then arrow labels, then shapes, each blanked out for the
            // next so nothing is read twice.
            var labels = new List<(int At, string Text)>();
            var rest = line;
            foreach (var pattern in new[] { MermaidQuoted(), MermaidEdgeLabel(), MermaidShape() })
            {
                foreach (Match m in pattern.Matches(rest))
                    labels.Add((m.Groups["text"].Index, m.Groups["text"].Value));
                rest = pattern.Replace(rest, m => new string(' ', m.Length));
            }
            if (labels.Count > 0)
            {
                foreach (var label in labels.OrderBy(l => l.At)) yield return label.Text;
                continue;
            }

            // "2026 : Version 2" (a timeline), "Alice->>Bob: Hello" (a
            // message): the words either side, except an arrow's ends.
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var before = line[..colon];
            if (!before.Contains('>') && !before.Contains("--", StringComparison.Ordinal)) yield return before;
            yield return line[(colon + 1)..];
        }
    }

    /// <summary>Lines that only style or arrange a diagram, and declarations that repeat an id.</summary>
    [GeneratedRegex(@"^(?:classDef|class|style|linkStyle|click|direction|participant|actor|dateFormat|axisFormat|tickInterval|excludes|includes|todayMarker)\b")]
    private static partial Regex MermaidStyling();

    [GeneratedRegex(@"^(?:title|section|accTitle\s*:|accDescr\s*:)\s+(?<text>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MermaidHeading();

    [GeneratedRegex("\"(?<text>[^\"]+)\"")]
    private static partial Regex MermaidQuoted();

    [GeneratedRegex(@"\|(?<text>[^|]+)\|")]
    private static partial Regex MermaidEdgeLabel();

    /// <summary>The innermost text of a shape: [x], (x), {x}, [(x)], ((x)), {{x}} and the like.</summary>
    [GeneratedRegex(@"[\[\(\{>]+(?<text>[^\[\]\(\)\{\}]*[^\[\]\(\)\{\}\s][^\[\]\(\)\{\}]*)[\]\)\}]+")]
    private static partial Regex MermaidShape();

    /// <summary>"14 Oct 2026", as an export writes it; the stored value when it is not a real date.</summary>
    private static string DateText(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : value ?? "";

    private static string TypeOf(JsonElement node) =>
        node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    private static string? Attr(JsonElement node, string name) =>
        node.TryGetProperty("attrs", out var attrs)
        && attrs.ValueKind == JsonValueKind.Object
        && attrs.TryGetProperty(name, out var v)
        && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;
}
