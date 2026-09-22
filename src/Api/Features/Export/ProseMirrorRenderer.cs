using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tesria.Api.Features.Blocks;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Renders a stored ProseMirror/TipTap document (the JSON persisted per page
/// version) to Markdown. Unknown nodes degrade to their text content rather
/// than failing the export.
///
/// It used to render HTML as well, and that was the reason exports looked
/// nothing like the page: a second renderer, in another language, against a
/// fifteen-line stylesheet, copying colours out of index.css by hand. Since
/// dev-plan 12.1 HTML and PDF are captured from the page itself, and only
/// Markdown is rendered here, because Markdown is a genuinely different
/// document rather than a picture of this one.
///
/// Nothing in this file should grow an HTML path again. If an export needs
/// to look like the page, it should be a capture of the page.
/// </summary>
public static class ProseMirrorRenderer
{
    public static string ToMarkdown(string contentJson, IReadOnlyList<BlockResult?>? blocks = null, string? baseUrl = null)
    {
        if (!TryParse(contentJson, out var root)) return string.Empty;
        var sb = new StringBuilder();
        RenderMarkdownChildren(root, sb, listDepth: 0, new Ctx(root, blocks, baseUrl));
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Per-document state: the heading anchors (dev-plan Phase 7 Wave A),
    /// handed out in document order as headings are rendered, the same
    /// order <see cref="HeadingAnchors.Collect"/> walked, so the nth
    /// heading gets the nth id, and a table of contents lists them all.
    /// </summary>
    private sealed class Ctx(JsonElement root, IReadOnlyList<BlockResult?>? blocks, string? baseUrl)
    {
        public IReadOnlyList<HeadingAnchors.Anchor> Anchors { get; } = HeadingAnchors.Collect(root);
        private int _next;
        public HeadingAnchors.Anchor? NextHeading() => _next < Anchors.Count ? Anchors[_next++] : null;

        /// <summary>The nth dynamic block gets the nth snapshot, in the same walk order <see cref="DynamicBlocks.Collect"/> used.</summary>
        private int _nextBlock;
        public BlockResult? NextBlock() =>
            blocks is not null && _nextBlock < blocks.Count ? blocks[_nextBlock++] : null;

        public string? BaseUrl { get; } = baseUrl?.TrimEnd('/');

        /// <summary>Set while rendering if the document contains a Mermaid block, so the caller can decide whether to ship a renderer.</summary>
        public bool UsedMermaid { get; set; }

        /// <summary>App-relative block hrefs become absolute when a base URL is known; a standalone file has no app to be relative to.</summary>
        public string Href(string? href) =>
            href is not null && href.StartsWith('/') && BaseUrl is not null ? BaseUrl + href : href ?? "#";

        /// <summary>
        /// Markdown gets explicit <c>&lt;a id&gt;</c> anchors only when the
        /// document links to its own headings: GitHub's auto-generated ids
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
                RenderMarkdownToc(node, ctx, sb);
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
            case "dynamicBlock":
                RenderMarkdownBlock(node, ctx.NextBlock(), ctx, sb, listDepth);
                break;
            case "math":
                var mdLatex = Attr(node, "latex") ?? "";
                sb.Append(BoolAttr(node, "display") ? $"\n$$\n{mdLatex}\n$$\n\n" : $"${mdLatex}$");
                break;
            case "chart":
                sb.Append($"_[Chart of table {Attr(node, "source") ?? "1"}]_\n\n");
                break;
            case "embed":
            case "smartLink":
                var mdTarget = SafeExternalUrl(Attr(node, "url"));
                if (mdTarget is not null) sb.Append('<').Append(mdTarget).Append(">\n\n");
                break;
            case "attachmentBlock":
                var mdAttachment = Attr(node, "attachmentId");
                if (mdAttachment is not null)
                    sb.Append("[attached file](").Append(ctx.Href($"/api/attachments/{mdAttachment}/download")).Append(")\n\n");
                break;
            case "gallery":
                RenderMarkdownChildren(node, sb, listDepth, ctx);
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
                // Columns in order, one after the other: Markdown has no columns.
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
                // URL, so it won't render standalone outside the app: acceptable for
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
    /// arbitrary JSON, so an unvalidated colour would be a way to inject
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
            // repeated here: it would read as the name twice.

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
                // Sanitised, not passed through: a Markdown file gets rendered
                // by something eventually, and a javascript: url that survives
                // into a permissive renderer is a live link. The HTML path
                // checked this and Markdown did not, which 12.1's cleanup
                // found when the HTML path was removed.
                "link" => $"[{text}]({SafeLinkTarget(Attr(mark, "href"))})",
                _ => text,
            };
        }
        return text;
    }

    // -- Phase 7 Wave D dynamic blocks (the one renderer per format) -------------

    private static string BlockKindOf(JsonElement node) => Attr(node, "kind") ?? "block";

    private static void RenderMarkdownBlock(JsonElement node, BlockResult? result, Ctx ctx, StringBuilder sb, int listDepth)
    {
        var kind = BlockKindOf(node);
        if (result is null) { sb.Append($"_[{kind}: dynamic content, shown on the page]_\n\n"); return; }
        if (result.Title is not null) sb.Append("**").Append(result.Title).Append("**\n\n");
        switch (result.Shape)
        {
            case "list":
                if (result.Items.Count == 0) sb.Append('_').Append(result.Empty ?? "Nothing to show.").Append("_\n");
                else WriteList(result.Items, 0);
                sb.Append('\n');
                break;
            case "table":
                if (result.Items.Count == 0) { sb.Append('_').Append(result.Empty ?? "Nothing to show.").Append("_\n\n"); break; }
                var cols = result.Columns ?? [];
                sb.Append('|'); foreach (var c in cols) sb.Append(' ').Append(EscapeTablePipes(c.Label)).Append(" |"); sb.Append('\n');
                sb.Append('|'); foreach (var _ in cols) sb.Append(" --- |"); sb.Append('\n');
                foreach (var item in result.Items)
                {
                    sb.Append('|');
                    foreach (var c in cols)
                    {
                        var text = item.Cells is not null && item.Cells.TryGetValue(c.Key, out var cell) ? CellMarkdown(cell, ctx) : "";
                        sb.Append(' ').Append(EscapeTablePipes(text)).Append(" |");
                    }
                    sb.Append('\n');
                }
                sb.Append('\n');
                break;
            case "document":
                if (result.Document is not null && TryParse(result.Document, out var inner))
                    RenderMarkdownChildren(inner, sb, listDepth, new Ctx(inner, null, ctx.BaseUrl));
                else sb.Append('_').Append(result.Empty ?? "Nothing to show.").Append("_\n\n");
                break;
        }
        sb.Append($"_Snapshot taken {result.GeneratedAt:yyyy-MM-dd HH:mm} UTC_\n\n");

        void WriteList(IReadOnlyList<BlockItem> items, int depth)
        {
            foreach (var item in items)
            {
                sb.Append(new string(' ', depth * 2)).Append("- ");
                sb.Append(item.Href is null ? item.Title : $"[{item.Title}]({ctx.Href(item.Href)})");
                if (item.Subtitle is not null) sb.Append(": ").Append(item.Subtitle);
                sb.Append('\n');
                if (item.Children is { Count: > 0 }) WriteList(item.Children, depth + 1);
            }
        }
    }

    private static string CellMarkdown(BlockCell cell, Ctx ctx)
    {
        if (cell.User is not null) return cell.User.DisplayName;
        if (cell.Date is { } d) return d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        if (cell.Checked is { } c) return c ? "[x]" : "[ ]";
        var text = cell.Text ?? "";
        return cell.Href is null ? text : $"[{text}]({ctx.Href(cell.Href)})";
    }

    /// <summary>
    /// An author-supplied external URL, accepted only as plain http(s).
    /// Document JSON is stored as the client sent it, so a
    /// <c>javascript:</c> or <c>data:</c> URL would otherwise become a live
    /// link in an exported file opened straight from disk.
    /// </summary>
    private static string? SafeExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri.ToString()
                : null;
    }

    // -- Phase 7 Wave B formatting ----------------------------------------------

    /// <summary>
    /// Light-theme ink per colour name, matching index.css's
    /// <c>--text-color-*</c>. The mark stores a name, never a colour value
    /// (see textColorMark.ts), so nothing from the document can reach a
    /// style attribute: an unknown name falls back to grey.
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
        /// <summary>Outline number, "1", "1.2", shown when section numbers are on.</summary>
        public string Number { get; set; } = "";
    }

    /// <summary>
    /// A table of contents' options: Confluence Cloud's macro parameters. The
    /// same rules as the editor's tocOptions.ts, and the same defaults: a node
    /// with no options renders exactly as it did before options existed.
    /// </summary>
    internal sealed record TocOptions(
        string Display, string BulletStyle, int MinLevel, int MaxLevel, bool SectionNumbers,
        string Indent, string Include, string Exclude, string CssClass, bool ExcludeInPdf)
    {
        public static readonly TocOptions Default = new("vertical", "bullet", 1, 6, false, "", "", "", "", false);
        private static readonly string[] BulletStyles = ["bullet", "mixed", "circle", "square", "numbered", "none"];
        private static readonly Regex Length = new(@"^(0|\d+(\.\d+)?(px|em|rem|pt|%))$", RegexOptions.CultureInvariant);
        private static readonly Regex ClassToken = new(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

        public static TocOptions Read(JsonElement node)
        {
            static int Level(string? raw, int fallback) =>
                int.TryParse(raw, out var n) ? Math.Clamp(n, 1, 6) : fallback;
            var min = Level(Attr(node, "minLevel"), 1);
            var max = Level(Attr(node, "maxLevel"), 6);
            var bullet = Attr(node, "bulletStyle");
            var indent = (Attr(node, "indent") ?? "").Trim();
            var cssClass = string.Join(' ', (Attr(node, "cssClass") ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => ClassToken.IsMatch(t)).Take(5));
            return new TocOptions(
                Attr(node, "display") == "horizontal" ? "horizontal" : "vertical",
                bullet is not null && BulletStyles.Contains(bullet) ? bullet : "bullet",
                Math.Min(min, max), Math.Max(min, max),
                BoolAttr(node, "sectionNumbers"),
                // Validated, not escaped: it lands in a style attribute.
                Length.IsMatch(indent) ? indent : "",
                Attr(node, "include") ?? "", Attr(node, "exclude") ?? "",
                cssClass, BoolAttr(node, "excludeInPdf"));
        }

        /// <summary>`|`-separated, case-sensitive, whole-text patterns; `*` any run of characters, `?` one.</summary>
        private static List<Regex> Patterns(string value) =>
            value.Split('|').Select(p => p.Trim()).Where(p => p.Length > 0)
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                .ToList();

        public List<HeadingAnchors.Anchor> Filter(IReadOnlyList<HeadingAnchors.Anchor> anchors)
        {
            var include = Patterns(Include);
            var exclude = Patterns(Exclude);
            return anchors.Where(a =>
                a.Level >= MinLevel && a.Level <= MaxLevel
                && (include.Count == 0 || include.Any(r => r.IsMatch(a.Text)))
                && !exclude.Any(r => r.IsMatch(a.Text))).ToList();
        }

        /// <summary>The list-style for a nesting depth, or null for "leave it to the browser" (Bullet).</summary>
        /// Section numbers sit alongside the chosen bullet, except Numbered, where
        /// two numbers per line would print: the outline numbers replace the list's.
        public string? ListStyle(int depth) =>
            SectionNumbers && BulletStyle == "numbered" ? "none" : BulletStyle switch
            {
                "mixed" => new[] { "disc", "circle", "square" }[depth % 3],
                "circle" => "circle",
                "square" => "square",
                "numbered" => "decimal",
                "none" => "none",
                _ => null,
            };

        public string? UlStyle(int depth)
        {
            var parts = new List<string>();
            if (ListStyle(depth) is { } ls) parts.Add($"list-style-type: {ls}");
            if (Indent.Length > 0) parts.Add($"padding-left: {Indent}");
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }

    private static void NumberToc(List<TocNode> nodes, string prefix)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].Number = prefix + (i + 1);
            NumberToc(nodes[i].Children, nodes[i].Number + ".");
        }
    }

    private static IEnumerable<TocNode> FlattenToc(List<TocNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(FlattenToc(n.Children)));

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

    private static void RenderMarkdownToc(JsonElement node, Ctx ctx, StringBuilder sb)
    {
        var o = TocOptions.Read(node);
        var tree = TocTree(o.Filter(ctx.Anchors));
        if (tree.Count == 0) return;
        NumberToc(tree, "");

        string Link(TocNode n) =>
            "[" + (o.SectionNumbers ? n.Number + " " : "") + TocText(n.Anchor).Replace("]", "\\]") + "](#" + n.Anchor.Id + ")";

        if (o.Display == "horizontal")
        {
            sb.Append(string.Join(" | ", FlattenToc(tree).Select(Link))).Append("\n\n");
            return;
        }
        Write(tree, 0);
        sb.Append('\n');

        void Write(List<TocNode> nodes, int depth)
        {
            var i = 0;
            foreach (var n in nodes)
            {
                i++;
                // Markdown has no bullet shapes; "Numbered" (without section
                // numbers, which already number the text) is the one style it can say.
                var marker = o.BulletStyle == "numbered" && !o.SectionNumbers ? $"{i}." : "-";
                sb.Append(new string(' ', depth * (marker == "-" ? 2 : 3))).Append(marker).Append(' ').Append(Link(n)).Append('\n');
                Write(n.Children, depth + 1);
            }
        }
    }

    /// <summary>
    /// A mention's stored display-name snapshot. The node also carries the
    /// user's id, but an export has no directory to resolve it against and a
    /// deleted account would resolve to nothing: the snapshot is what keeps
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

    /// <summary>"10 Sep 2026": invariant; an export has no viewer locale to honour.</summary>
    private static string DateText(DateOnly date) => date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>
    /// A link target safe to write into an exported document: http(s), a
    /// same-document anchor, or a path within this instance. Anything else
    /// (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>) becomes an inert
    /// placeholder, because the text of the link is still worth keeping and
    /// the destination is not.
    /// </summary>
    private static string SafeLinkTarget(string? href)
    {
        var value = href?.Trim();
        if (string.IsNullOrEmpty(value)) return "#";
        if (value.StartsWith('#') || value.StartsWith('/')) return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? value
                : "#";
    }

}
