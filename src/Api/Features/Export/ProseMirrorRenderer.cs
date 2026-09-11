using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Tesria.Api.Features.Export;

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
        RenderHtmlChildren(root, sb, new Ctx(root));
        return sb.ToString();
    }

    public static string ToMarkdown(string contentJson)
    {
        if (!TryParse(contentJson, out var root)) return string.Empty;
        var sb = new StringBuilder();
        RenderMarkdownChildren(root, sb, listDepth: 0, new Ctx(root));
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Per-document state: the heading anchors (dev-plan Phase 7 Wave A),
    /// handed out in document order as headings are rendered — the same
    /// order <see cref="HeadingAnchors.Collect"/> walked — so the nth
    /// heading gets the nth id, and a table of contents lists them all.
    /// </summary>
    private sealed class Ctx(JsonElement root)
    {
        public IReadOnlyList<HeadingAnchors.Anchor> Anchors { get; } = HeadingAnchors.Collect(root);
        private int _next;
        public HeadingAnchors.Anchor? NextHeading() => _next < Anchors.Count ? Anchors[_next++] : null;

        /// <summary>
        /// Markdown gets explicit <c>&lt;a id&gt;</c> anchors only when the
        /// document links to its own headings — GitHub's auto-generated ids
        /// use a different rule, and the anchors are clutter otherwise.
        /// </summary>
        public bool MarkdownNeedsAnchors { get; } = LinksToHeadings(root);

        private static bool LinksToHeadings(JsonElement node)
        {
            if (TypeOf(node) == "tableOfContents") return true;
            if (node.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
                foreach (var mark in marks.EnumerateArray())
                    if (TypeOf(mark) == "link" && (Attr(mark, "href") ?? "").StartsWith('#')) return true;
            foreach (var child in Children(node))
                if (LinksToHeadings(child)) return true;
            return false;
        }
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

    private static void RenderHtmlChildren(JsonElement node, StringBuilder sb, Ctx ctx)
    {
        foreach (var child in Children(node)) RenderHtml(child, sb, ctx);
    }

    private static void RenderHtml(JsonElement node, StringBuilder sb, Ctx ctx)
    {
        switch (TypeOf(node))
        {
            case "text":
                sb.Append(ApplyHtmlMarks(node));
                break;
            case "paragraph":
                var pStyle = BlockStyle(node);
                sb.Append(pStyle is null ? "<p>" : $"<p style=\"{pStyle}\">");
                RenderHtmlChildren(node, sb, ctx); sb.Append("</p>\n");
                break;
            case "heading":
                var level = Attr(node, "level") ?? "1";
                var hStyle = BlockStyle(node);
                var anchor = ctx.NextHeading();
                sb.Append($"<h{level}");
                if (anchor is not null) sb.Append($" id=\"{Escape(anchor.Id)}\"");
                if (hStyle is not null) sb.Append($" style=\"{hStyle}\"");
                sb.Append('>');
                RenderHtmlChildren(node, sb, ctx); sb.Append($"</h{level}>\n");
                break;
            case "tableOfContents":
                RenderHtmlToc(ctx, sb);
                break;
            case "expand":
                // <details> is the one collapsible element HTML has; open by
                // default so a printed or scripted-off copy still shows it all.
                sb.Append("<details open><summary>").Append(Escape(Attr(node, "title") ?? "")).Append("</summary>\n");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append("</details>\n");
                break;
            case "status":
                var (statusBg, statusInk) = StatusColors[StatusColorOf(node)];
                sb.Append($"<span data-status=\"{StatusColorOf(node)}\" style=\"display: inline-block; padding: 0 0.4em; border-radius: 3px; ")
                  .Append($"font-size: 0.75em; font-weight: 700; text-transform: uppercase; background: {statusBg}; color: {statusInk}\">")
                  .Append(Escape(StatusText(node))).Append("</span>");
                break;
            case "date":
                var iso = IsoDate(node);
                if (iso is null) sb.Append(Escape(Attr(node, "date") ?? ""));
                else sb.Append($"<time datetime=\"{iso.Value:yyyy-MM-dd}\">{DateText(iso.Value)}</time>");
                break;
            case "decision":
                sb.Append("<div data-type=\"decision\" style=\"border: 1px solid #e4e6eb; background: #f4f5f7; border-radius: 6px; padding: 12px 16px; margin: 16px 0\">\n");
                sb.Append("<strong>Decision</strong>\n");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append("</div>\n");
                break;
            case "layoutSection":
                sb.Append($"<div data-type=\"layout-section\" data-width=\"{LayoutWidthOf(node)}\" style=\"display: flex; gap: 20px; margin: 16px 0\">\n");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append("</div>\n");
                break;
            case "layoutColumn":
                sb.Append($"<div data-type=\"layout-column\" style=\"flex: {ColumnWeight(node)} 1 0%; min-width: 0\">\n");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append("</div>\n");
                break;
            case "bulletList":
                sb.Append("<ul>\n"); RenderHtmlChildren(node, sb, ctx); sb.Append("</ul>\n");
                break;
            case "orderedList":
                sb.Append("<ol>\n"); RenderHtmlChildren(node, sb, ctx); sb.Append("</ol>\n");
                break;
            case "listItem":
                sb.Append("<li>"); RenderHtmlChildren(node, sb, ctx); sb.Append("</li>\n");
                break;
            case "blockquote":
                sb.Append("<blockquote>\n"); RenderHtmlChildren(node, sb, ctx); sb.Append("</blockquote>\n");
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
            case "image":
                var src = Attr(node, "src") ?? "";
                var alt = Attr(node, "alt");
                var imgTitle = Attr(node, "title");
                var imgStyle = ImageStyle(node);
                sb.Append($"<img src=\"{Escape(src)}\"");
                if (alt is not null) sb.Append($" alt=\"{Escape(alt)}\"");
                if (imgTitle is not null) sb.Append($" title=\"{Escape(imgTitle)}\"");
                if (imgStyle is not null) sb.Append($" style=\"{imgStyle}\"");
                sb.Append(" />\n");
                break;
            case "table":
                var tableStyle = TableStyle(node);
                sb.Append(tableStyle is null ? "<table>\n" : $"<table style=\"{tableStyle}\">\n");
                RenderHtmlChildren(node, sb, ctx); sb.Append("</table>\n");
                break;
            case "tableRow":
                sb.Append("<tr>\n"); RenderHtmlChildren(node, sb, ctx); sb.Append("</tr>\n");
                break;
            case "tableHeader":
            case "tableCell":
                var cellTag = TypeOf(node) == "tableHeader" ? "th" : "td";
                var cellBg = CellBackgroundStyle(node);
                sb.Append(cellBg is null ? $"<{cellTag}>" : $"<{cellTag} style=\"{cellBg}\">");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append($"</{cellTag}>\n");
                break;
            case "panel":
                var panelType = PanelTypeOf(node);
                // Colours inlined rather than left to a stylesheet: an exported
                // HTML file is opened on its own, with none of the app's CSS.
                sb.Append($"<div data-panel-type=\"{panelType}\" style=\"{PanelStyle(panelType)}\">\n");
                sb.Append($"<strong>{PanelLabels[panelType]}</strong>\n");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append("</div>\n");
                break;
            case "taskList":
                sb.Append("<ul data-type=\"taskList\">\n"); RenderHtmlChildren(node, sb, ctx); sb.Append("</ul>\n");
                break;
            case "taskItem":
                sb.Append("<li><label><input type=\"checkbox\" disabled");
                if (BoolAttr(node, "checked")) sb.Append(" checked");
                sb.Append(" /></label><div>");
                RenderHtmlChildren(node, sb, ctx);
                sb.Append("</div></li>\n");
                break;
            case "mention":
                // The label, not a lookup: an exported file has no directory,
                // and neither does a page version from before a rename.
                sb.Append($"<span data-type=\"mention\" style=\"background: #deebff; color: #0747a6; ")
                  .Append("padding: 0 0.3em; border-radius: 3px\">@")
                  .Append(Escape(MentionLabel(node))).Append("</span>");
                break;
            default:
                RenderHtmlChildren(node, sb, ctx);
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
                "underline" => $"<u>{text}</u>",
                "strike" => $"<s>{text}</s>",
                "code" => $"<code>{text}</code>",
                "highlight" => HighlightHtml(mark, text),
                "textColor" => $"<span style=\"color: {TextColors[TextColorOf(mark)]}\">{text}</span>",
                "subscript" => $"<sub>{text}</sub>",
                "superscript" => $"<sup>{text}</sup>",
                "link" => $"<a href=\"{Escape(Attr(mark, "href") ?? "#")}\" rel=\"noreferrer\">{text}</a>",
                _ => text,
            };
        }
        return text;
    }

    // -- Markdown -------------------------------------------------------------

    private static void RenderMarkdownChildren(JsonElement node, StringBuilder sb, int listDepth, Ctx ctx)
    {
        foreach (var child in Children(node)) RenderMarkdown(child, sb, listDepth, ctx);
    }

    private static void RenderMarkdown(JsonElement node, StringBuilder sb, int listDepth, Ctx ctx)
    {
        switch (TypeOf(node))
        {
            case "text":
                sb.Append(ApplyMarkdownMarks(node));
                break;
            case "paragraph":
                RenderMarkdownChildren(node, sb, listDepth, ctx);
                sb.Append("\n\n");
                break;
            case "heading":
                var level = int.TryParse(Attr(node, "level"), out var l) ? Math.Clamp(l, 1, 6) : 1;
                var mdAnchor = ctx.NextHeading();
                if (ctx.MarkdownNeedsAnchors && mdAnchor is not null)
                    sb.Append($"<a id=\"{Escape(mdAnchor.Id)}\"></a>\n");
                sb.Append(new string('#', level)).Append(' ');
                RenderMarkdownChildren(node, sb, listDepth, ctx);
                sb.Append("\n\n");
                break;
            case "tableOfContents":
                RenderMarkdownToc(ctx, sb);
                break;
            case "expand":
                // Markdown has no collapsible block: the title in bold, then the body.
                var expandTitle = (Attr(node, "title") ?? "").Trim();
                if (expandTitle.Length > 0) sb.Append("**").Append(expandTitle).Append("**\n\n");
                RenderMarkdownChildren(node, sb, listDepth, ctx);
                break;
            case "status":
                // A code span is the nearest thing to a lozenge most renderers have.
                sb.Append('`').Append(StatusText(node).Replace('`', '\'')).Append('`');
                break;
            case "mention":
                sb.Append('@').Append(MentionLabel(node));
                break;
            case "date":
                var mdIso = IsoDate(node);
                sb.Append(mdIso is null ? Attr(node, "date") ?? "" : DateText(mdIso.Value));
                break;
            case "decision":
                var decisionInner = new StringBuilder();
                RenderMarkdownChildren(node, decisionInner, listDepth, ctx);
                sb.Append("> **Decision:**\n>\n");
                foreach (var line in decisionInner.ToString().TrimEnd().Split('\n'))
                    sb.Append("> ").Append(line).Append('\n');
                sb.Append('\n');
                break;
            case "layoutSection":
            case "layoutColumn":
                // Columns in order, one after the other — Markdown has no columns.
                RenderMarkdownChildren(node, sb, listDepth, ctx);
                break;
            case "bulletList":
            case "orderedList":
                RenderMarkdownList(node, sb, listDepth, ordered: TypeOf(node) == "orderedList", ctx);
                break;
            case "blockquote":
                var inner = new StringBuilder();
                RenderMarkdownChildren(node, inner, listDepth, ctx);
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
            case "image":
                // Exported Markdown references the app's own (authenticated) attachment
                // URL, so it won't render standalone outside the app — acceptable for
                // internal dev docs.
                sb.Append($"![{Attr(node, "alt") ?? ""}]({Attr(node, "src") ?? ""})\n\n");
                break;
            case "table":
                RenderMarkdownTable(node, sb);
                break;
            case "panel":
                // GFM has no callout syntax that renders consistently (GitHub's
                // own "> [!NOTE]" alerts are GitHub-only), so a blockquote with
                // a bold type label degrades sensibly in every renderer.
                var panelInner = new StringBuilder();
                RenderMarkdownChildren(node, panelInner, listDepth, ctx);
                sb.Append("> **").Append(PanelLabels[PanelTypeOf(node)]).Append("**\n>\n");
                foreach (var line in panelInner.ToString().TrimEnd().Split('\n'))
                    sb.Append("> ").Append(line).Append('\n');
                sb.Append('\n');
                break;
            case "taskList":
                RenderMarkdownTaskList(node, sb, listDepth, ctx);
                break;
            default:
                RenderMarkdownChildren(node, sb, listDepth, ctx);
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

    /// <summary>The border/drop-shadow display attrs (editor-only affordances) as an inline style, or null.</summary>
    private static string? ImageStyle(JsonElement node)
    {
        var parts = new List<string>();
        if (BoolAttr(node, "border")) parts.Add("border: 1px solid #e4e6eb; padding: 2px");
        if (BoolAttr(node, "shadow")) parts.Add("box-shadow: 0 4px 14px rgba(23, 43, 77, 0.25)");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>A table cell's background colour (TableCellMenu's palette) as an inline style, or null.</summary>
    private static string? CellBackgroundStyle(JsonElement node)
    {
        var color = Attr(node, "backgroundColor");
        return IsSafeCssColor(color) ? $"background-color: {color}" : null;
    }

    /// <summary>
    /// A highlight mark, carrying its colour when the editor set one
    /// (extensions.ts configures Highlight with multicolor). Marks stored
    /// before that predate the attribute and render as a plain &lt;mark&gt;.
    /// </summary>
    private static string HighlightHtml(JsonElement mark, string text)
    {
        var color = Attr(mark, "color");
        return IsSafeCssColor(color)
            ? $"<mark style=\"background-color: {color}\">{text}</mark>"
            : $"<mark>{text}</mark>";
    }

    /// <summary>
    /// Whitelists the colour shapes the editor's palettes actually produce (a
    /// #rgb/#rrggbb hex) before it reaches a `style` attribute. Attribute
    /// values come from stored document JSON, which the API accepts as
    /// arbitrary JSON — so an unvalidated colour would be a way to inject
    /// arbitrary CSS into exported HTML.
    /// </summary>
    private static bool IsSafeCssColor(string? color) =>
        color is not null
        && (color.Length == 4 || color.Length == 7)
        && color[0] == '#'
        && color.Skip(1).All(Uri.IsHexDigit);

    private static readonly Dictionary<string, string> PanelLabels = new()
    {
        ["info"] = "Info",
        ["note"] = "Note",
        ["success"] = "Tip",
        ["warning"] = "Warning",
        ["error"] = "Error",
    };

    // Background/border/text per panel type, matching index.css's .panel--*.
    private static readonly Dictionary<string, (string Bg, string Border, string Text)> PanelColors = new()
    {
        ["info"] = ("#deebff", "#579dff", "#0c66e4"),
        ["note"] = ("#eae6ff", "#9f8fef", "#5e4db2"),
        ["success"] = ("#e3fcef", "#4bce97", "#216e4e"),
        ["warning"] = ("#fff7d6", "#e2b203", "#a54800"),
        ["error"] = ("#ffedeb", "#f87168", "#ae2e24"),
    };

    /// <summary>The node's panelType, defaulted to "info" if absent or unrecognised.</summary>
    private static string PanelTypeOf(JsonElement node)
    {
        var type = Attr(node, "panelType");
        return type is not null && PanelColors.ContainsKey(type) ? type : "info";
    }

    private static string PanelStyle(string panelType)
    {
        var (bg, border, text) = PanelColors[panelType];
        return $"background: {bg}; border: 1px solid {border}; color: {text}; "
             + "border-radius: 6px; padding: 12px 16px; margin: 16px 0";
    }

    /// <summary>The table's manually-dragged width or full-width toggle (see extensions.ts's Table
    /// extension) as an inline style, or null for the unset/default case.</summary>
    private static string? TableStyle(JsonElement node)
    {
        if (Attr(node, "layout") == "full-width") return "width: 100%";
        var width = Attr(node, "width");
        return width is not null && int.TryParse(width, out var px) ? $"width: min({px}px, 100%)" : null;
    }

    private static void RenderMarkdownTaskList(JsonElement listNode, StringBuilder sb, int depth, Ctx ctx)
    {
        var indent = new string(' ', depth * 2);
        foreach (var item in Children(listNode))
        {
            var marker = BoolAttr(item, "checked") ? "- [x] " : "- [ ] ";
            var itemText = new StringBuilder();
            RenderMarkdownChildren(item, itemText, depth + 1, ctx);
            // The assignee is a denormalised copy of the mention already
            // inside the item (taskAssignee.ts), so it is deliberately not
            // repeated here — it would read as the name twice.

            var lines = itemText.ToString().TrimEnd().Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == 0) sb.Append(indent).Append(marker).Append(lines[i]).Append('\n');
                else if (lines[i].Length > 0) sb.Append(indent).Append("  ").Append(lines[i]).Append('\n');
            }
        }
        if (depth == 0) sb.Append('\n');
    }

    private static void RenderMarkdownList(JsonElement listNode, StringBuilder sb, int depth, bool ordered, Ctx ctx)
    {
        var indent = new string(' ', depth * 2);
        var index = 1;
        foreach (var item in Children(listNode))
        {
            var marker = ordered ? $"{index++}. " : "- ";
            var itemText = new StringBuilder();
            RenderMarkdownChildren(item, itemText, depth + 1, ctx);

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
                // GFM has no native highlight syntax; most renderers pass inline
                // raw HTML through untouched, so this degrades gracefully.
                "highlight" => HighlightHtml(mark, text),
                // GFM has no syntax for any of these three; most renderers
                // pass inline raw HTML through untouched, so they degrade.
                "textColor" => $"<span style=\"color: {TextColors[TextColorOf(mark)]}\">{text}</span>",
                "subscript" => $"<sub>{text}</sub>",
                "superscript" => $"<sup>{text}</sup>",
                "link" => $"[{text}]({Attr(mark, "href") ?? "#"})",
                _ => text,
            };
        }
        return text;
    }

    // -- Phase 7 Wave B formatting ----------------------------------------------

    /// <summary>
    /// Light-theme ink per colour name, matching index.css's
    /// <c>--text-color-*</c>. The mark stores a name, never a colour value
    /// (see textColorMark.ts), so nothing from the document can reach a
    /// style attribute — an unknown name falls back to grey.
    /// </summary>
    private static readonly Dictionary<string, string> TextColors = new()
    {
        ["grey"] = "#42526e",
        ["blue"] = "#0747a6",
        ["teal"] = "#008da6",
        ["green"] = "#006644",
        ["yellow"] = "#946f00",
        ["orange"] = "#b65c02",
        ["red"] = "#bf2600",
        ["purple"] = "#403294",
    };

    private static string TextColorOf(JsonElement mark)
    {
        var color = Attr(mark, "color");
        return color is not null && TextColors.ContainsKey(color) ? color : "grey";
    }

    /// <summary>
    /// A block's alignment and indent as one inline style, or null when it
    /// has neither. The indent is recomputed from a clamped integer — never
    /// echoed from the document — the same rule textFormatting.ts follows.
    /// </summary>
    private static string? BlockStyle(JsonElement node)
    {
        var parts = new List<string>();
        var align = Attr(node, "textAlign");
        if (align is "left" or "center" or "right" or "justify") parts.Add($"text-align: {align}");
        if (int.TryParse(Attr(node, "textIndent"), out var raw))
        {
            var level = Math.Clamp(raw, 0, MaxIndent);
            if (level > 0)
                parts.Add($"margin-left: {(level * IndentStepRem).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}rem");
        }
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>Kept in step with textFormatting.ts's MAX_INDENT / INDENT_STEP_REM.</summary>
    private const int MaxIndent = 4;
    private const double IndentStepRem = 1.75;

    // -- Phase 7 Wave A blocks --------------------------------------------------

    /// <summary>
    /// The same nesting TocView.tsx builds: each heading sits under the
    /// nearest shallower one before it, so an H1 followed by an H4 indents
    /// once, not three times.
    /// </summary>
    private sealed class TocNode(HeadingAnchors.Anchor anchor)
    {
        public HeadingAnchors.Anchor Anchor { get; } = anchor;
        public List<TocNode> Children { get; } = [];
    }

    private static List<TocNode> TocTree(IReadOnlyList<HeadingAnchors.Anchor> anchors)
    {
        var roots = new List<TocNode>();
        var stack = new List<TocNode>();
        foreach (var a in anchors)
        {
            var node = new TocNode(a);
            while (stack.Count > 0 && stack[^1].Anchor.Level >= a.Level) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0) roots.Add(node); else stack[^1].Children.Add(node);
            stack.Add(node);
        }
        return roots;
    }

    private static string TocText(HeadingAnchors.Anchor a) => a.Text.Length == 0 ? "Untitled heading" : a.Text;

    private static void RenderHtmlToc(Ctx ctx, StringBuilder sb)
    {
        if (ctx.Anchors.Count == 0) return;
        sb.Append("<nav data-type=\"table-of-contents\">\n");
        Write(TocTree(ctx.Anchors));
        sb.Append("</nav>\n");

        void Write(List<TocNode> nodes)
        {
            sb.Append("<ul>\n");
            foreach (var n in nodes)
            {
                sb.Append($"<li><a href=\"#{Escape(n.Anchor.Id)}\">{Escape(TocText(n.Anchor))}</a>");
                if (n.Children.Count > 0) Write(n.Children);
                sb.Append("</li>\n");
            }
            sb.Append("</ul>\n");
        }
    }

    private static void RenderMarkdownToc(Ctx ctx, StringBuilder sb)
    {
        if (ctx.Anchors.Count == 0) return;
        Write(TocTree(ctx.Anchors), 0);
        sb.Append('\n');

        void Write(List<TocNode> nodes, int depth)
        {
            foreach (var n in nodes)
            {
                sb.Append(new string(' ', depth * 2)).Append("- [").Append(TocText(n.Anchor).Replace("]", "\\]"))
                  .Append("](#").Append(n.Anchor.Id).Append(")\n");
                Write(n.Children, depth + 1);
            }
        }
    }

    // Background/ink per status colour, matching index.css's light --status-* tokens.
    private static readonly Dictionary<string, (string Bg, string Ink)> StatusColors = new()
    {
        ["grey"] = ("#dfe1e6", "#42526e"),
        ["red"] = ("#ffebe6", "#de350b"),
        ["yellow"] = ("#fff0b3", "#974f0c"),
        ["green"] = ("#e3fcef", "#006644"),
        ["blue"] = ("#deebff", "#0747a6"),
        ["purple"] = ("#eae6ff", "#403294"),
    };

    /// <summary>The status's colour name, defaulted to grey — never a value from the document.</summary>
    private static string StatusColorOf(JsonElement node)
    {
        var color = Attr(node, "color");
        return color is not null && StatusColors.ContainsKey(color) ? color : "grey";
    }

    /// <summary>
    /// A mention's stored display-name snapshot. The node also carries the
    /// user's id, but an export has no directory to resolve it against and a
    /// deleted account would resolve to nothing — the snapshot is what keeps
    /// an old document readable.
    /// </summary>
    private static string MentionLabel(JsonElement node)
    {
        var label = (Attr(node, "label") ?? "").Trim();
        return label.Length == 0 ? "Unknown user" : label;
    }

    private static string StatusText(JsonElement node)
    {
        var text = (Attr(node, "text") ?? "").Trim();
        return text.Length == 0 ? "STATUS" : text;
    }

    /// <summary>The date attr as a calendar date, or null when it is not a real yyyy-mm-dd.</summary>
    private static DateOnly? IsoDate(JsonElement node) =>
        DateOnly.TryParseExact(Attr(node, "date"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"10 Sep 2026" — invariant; an export has no viewer locale to honour.</summary>
    private static string DateText(DateOnly date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string LayoutWidthOf(JsonElement node) =>
        Attr(node, "width") is "wide" or "full" ? Attr(node, "width")! : "default";

    /// <summary>A column's flex weight: its stored percentage when it is a sane number, else an equal share.</summary>
    private static string ColumnWeight(JsonElement node) =>
        double.TryParse(Attr(node, "width"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
        && w > 0 && w <= 100
            ? w.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "1";

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
