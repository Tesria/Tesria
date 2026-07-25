using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ConfluenceClone.Api.Features.Export;

/// <summary>
/// Renders a stored ProseMirror/TipTap document (the JSON we persist per page
/// version) to HTML or Markdown for export. Covers the node and mark types
/// produced by the editor's StarterKit; unknown nodes degrade to their text
/// content rather than failing the export.
/// </summary>
public static class ProseMirrorRenderer
{
    public static string ToHtml(string contentJson)
    {
        if (!TryParse(contentJson, out var root)) return string.Empty;
        var sb = new StringBuilder();
        RenderHtmlChildren(root, sb);
        return sb.ToString();
    }

    public static string ToMarkdown(string contentJson)
    {
        if (!TryParse(contentJson, out var root)) return string.Empty;
        var sb = new StringBuilder();
        RenderMarkdownChildren(root, sb, listDepth: 0);
        return sb.ToString().TrimEnd() + "\n";
    }

    private static bool TryParse(string contentJson, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(contentJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    private static string TypeOf(JsonElement node) =>
        node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "" : "";

    private static string? Attr(JsonElement node, string name) =>
        node.TryGetProperty("attrs", out var attrs)
        && attrs.ValueKind == JsonValueKind.Object
        && attrs.TryGetProperty(name, out var v)
        && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    private static bool BoolAttr(JsonElement node, string name) =>
        node.TryGetProperty("attrs", out var attrs)
        && attrs.ValueKind == JsonValueKind.Object
        && attrs.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.True;

    // -- HTML -----------------------------------------------------------------

    private static void RenderHtmlChildren(JsonElement node, StringBuilder sb)
    {
        foreach (var child in Children(node)) RenderHtml(child, sb);
    }

    private static void RenderHtml(JsonElement node, StringBuilder sb)
    {
        switch (TypeOf(node))
        {
            case "text":
                sb.Append(ApplyHtmlMarks(node));
                break;
            case "paragraph":
                sb.Append("<p>"); RenderHtmlChildren(node, sb); sb.Append("</p>\n");
                break;
            case "heading":
                var level = Attr(node, "level") ?? "1";
                sb.Append($"<h{level}>"); RenderHtmlChildren(node, sb); sb.Append($"</h{level}>\n");
                break;
            case "bulletList":
                sb.Append("<ul>\n"); RenderHtmlChildren(node, sb); sb.Append("</ul>\n");
                break;
            case "orderedList":
                sb.Append("<ol>\n"); RenderHtmlChildren(node, sb); sb.Append("</ol>\n");
                break;
            case "listItem":
                sb.Append("<li>"); RenderHtmlChildren(node, sb); sb.Append("</li>\n");
                break;
            case "blockquote":
                sb.Append("<blockquote>\n"); RenderHtmlChildren(node, sb); sb.Append("</blockquote>\n");
                break;
            case "codeBlock":
                var lang = Attr(node, "language");
                sb.Append(lang is null ? "<pre><code>" : $"<pre><code class=\"language-{Escape(lang)}\">");
                sb.Append(Escape(PlainText(node)));
                sb.Append("</code></pre>\n");
                break;
            case "horizontalRule":
                sb.Append("<hr />\n");
                break;
            case "hardBreak":
                sb.Append("<br />");
                break;
            case "table":
                sb.Append("<table>\n"); RenderHtmlChildren(node, sb); sb.Append("</table>\n");
                break;
            case "tableRow":
                sb.Append("<tr>\n"); RenderHtmlChildren(node, sb); sb.Append("</tr>\n");
                break;
            case "tableHeader":
                sb.Append("<th>"); RenderHtmlChildren(node, sb); sb.Append("</th>\n");
                break;
            case "tableCell":
                sb.Append("<td>"); RenderHtmlChildren(node, sb); sb.Append("</td>\n");
                break;
            case "taskList":
                sb.Append("<ul data-type=\"taskList\">\n"); RenderHtmlChildren(node, sb); sb.Append("</ul>\n");
                break;
            case "taskItem":
                sb.Append("<li><label><input type=\"checkbox\" disabled");
                if (BoolAttr(node, "checked")) sb.Append(" checked");
                sb.Append(" /></label><div>");
                RenderHtmlChildren(node, sb);
                sb.Append("</div></li>\n");
                break;
            default:
                RenderHtmlChildren(node, sb);
                break;
        }
    }

    private static string ApplyHtmlMarks(JsonElement textNode)
    {
        var text = Escape(textNode.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "");
        if (!textNode.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array)
            return text;

        foreach (var mark in marks.EnumerateArray())
        {
            text = TypeOf(mark) switch
            {
                "bold" => $"<strong>{text}</strong>",
                "italic" => $"<em>{text}</em>",
                "strike" => $"<s>{text}</s>",
                "code" => $"<code>{text}</code>",
                "link" => $"<a href=\"{Escape(Attr(mark, "href") ?? "#")}\" rel=\"noreferrer\">{text}</a>",
                _ => text,
            };
        }
        return text;
    }

    // -- Markdown -------------------------------------------------------------

    private static void RenderMarkdownChildren(JsonElement node, StringBuilder sb, int listDepth)
    {
        foreach (var child in Children(node)) RenderMarkdown(child, sb, listDepth);
    }

    private static void RenderMarkdown(JsonElement node, StringBuilder sb, int listDepth)
    {
        switch (TypeOf(node))
        {
            case "text":
                sb.Append(ApplyMarkdownMarks(node));
                break;
            case "paragraph":
                RenderMarkdownChildren(node, sb, listDepth);
                sb.Append("\n\n");
                break;
            case "heading":
                var level = int.TryParse(Attr(node, "level"), out var l) ? Math.Clamp(l, 1, 6) : 1;
                sb.Append(new string('#', level)).Append(' ');
                RenderMarkdownChildren(node, sb, listDepth);
                sb.Append("\n\n");
                break;
            case "bulletList":
            case "orderedList":
                RenderMarkdownList(node, sb, listDepth, ordered: TypeOf(node) == "orderedList");
                break;
            case "blockquote":
                var inner = new StringBuilder();
                RenderMarkdownChildren(node, inner, listDepth);
                foreach (var line in inner.ToString().TrimEnd().Split('\n'))
                    sb.Append("> ").Append(line).Append('\n');
                sb.Append('\n');
                break;
            case "codeBlock":
                sb.Append("```").Append(Attr(node, "language") ?? "").Append('\n');
                sb.Append(PlainText(node).TrimEnd()).Append("\n```\n\n");
                break;
            case "horizontalRule":
                sb.Append("---\n\n");
                break;
            case "hardBreak":
                sb.Append("  \n");
                break;
            case "table":
                RenderMarkdownTable(node, sb);
                break;
            case "taskList":
                RenderMarkdownTaskList(node, sb, listDepth);
                break;
            default:
                RenderMarkdownChildren(node, sb, listDepth);
                break;
        }
    }

    /// <summary>A minimal, non-aligned GFM pipe table.</summary>
    private static void RenderMarkdownTable(JsonElement tableNode, StringBuilder sb)
    {
        var rows = Children(tableNode)
            .Select(row => Children(row)
                .Select(cell => EscapeTablePipes(PlainText(cell).Trim().Replace('\n', ' ')))
                .ToList())
            .ToList();
        if (rows.Count == 0) return;

        var columnCount = rows.Max(r => r.Count);
        void WriteRow(List<string> cells)
        {
            sb.Append('|');
            for (var i = 0; i < columnCount; i++)
                sb.Append(' ').Append(i < cells.Count ? cells[i] : "").Append(" |");
            sb.Append('\n');
        }

        WriteRow(rows[0]);
        sb.Append('|');
        for (var i = 0; i < columnCount; i++) sb.Append(" --- |");
        sb.Append('\n');
        for (var i = 1; i < rows.Count; i++) WriteRow(rows[i]);
        sb.Append('\n');
    }

    private static string EscapeTablePipes(string text) => text.Replace("|", "\\|");

    private static void RenderMarkdownTaskList(JsonElement listNode, StringBuilder sb, int depth)
    {
        var indent = new string(' ', depth * 2);
        foreach (var item in Children(listNode))
        {
            var marker = BoolAttr(item, "checked") ? "- [x] " : "- [ ] ";
            var itemText = new StringBuilder();
            RenderMarkdownChildren(item, itemText, depth + 1);

            var lines = itemText.ToString().TrimEnd().Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == 0) sb.Append(indent).Append(marker).Append(lines[i]).Append('\n');
                else if (lines[i].Length > 0) sb.Append(indent).Append("  ").Append(lines[i]).Append('\n');
            }
        }
        if (depth == 0) sb.Append('\n');
    }

    private static void RenderMarkdownList(JsonElement listNode, StringBuilder sb, int depth, bool ordered)
    {
        var indent = new string(' ', depth * 2);
        var index = 1;
        foreach (var item in Children(listNode))
        {
            var marker = ordered ? $"{index++}. " : "- ";
            var itemText = new StringBuilder();
            RenderMarkdownChildren(item, itemText, depth + 1);

            var lines = itemText.ToString().TrimEnd().Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == 0) sb.Append(indent).Append(marker).Append(lines[i]).Append('\n');
                else if (lines[i].Length > 0) sb.Append(indent).Append("  ").Append(lines[i]).Append('\n');
            }
        }
        if (depth == 0) sb.Append('\n');
    }

    private static string ApplyMarkdownMarks(JsonElement textNode)
    {
        var text = textNode.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (!textNode.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array)
            return text;

        foreach (var mark in marks.EnumerateArray())
        {
            text = TypeOf(mark) switch
            {
                "bold" => $"**{text}**",
                "italic" => $"*{text}*",
                "strike" => $"~~{text}~~",
                "code" => $"`{text}`",
                "link" => $"[{text}]({Attr(mark, "href") ?? "#"})",
                _ => text,
            };
        }
        return text;
    }

    // -- shared ---------------------------------------------------------------

    /// <summary>Concatenated text of a node's descendants (used for code blocks).</summary>
    private static string PlainText(JsonElement node)
    {
        var sb = new StringBuilder();
        Walk(node);
        return sb.ToString();

        void Walk(JsonElement n)
        {
            if (TypeOf(n) == "text" && n.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            foreach (var c in Children(n)) Walk(c);
        }
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
