using System.Text;
using System.Text.Json.Nodes;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// Markdown in, a stored ProseMirror document out (dev-plan 8.4, decision 4).
///
/// The subset is deliberately exactly what the *export* emits, so a page can
/// be read as Markdown, edited, and written back without losing its shape:
/// headings, paragraphs, bold/italic/strike/code, links, bullet/ordered/task
/// lists, fenced code with a language, blockquotes, GFM tables, rules and
/// images. Anything the editor can hold that Markdown cannot say (panels,
/// status lozenges, layouts, dynamic blocks) is out of reach here *by
/// design*: an assistant writes body text, and a person enriches it. A
/// caller that genuinely has a document uses `contentJson` instead.
///
/// Unsupported constructs degrade to their text rather than being dropped,
/// because silently losing a paragraph is worse than rendering it plainly.
/// </summary>
public static class MarkdownToProseMirror
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseGridTables()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras()   // ~~strikethrough~~
        .Build();

    public static string Convert(string markdown)
    {
        var document = Markdown.Parse(markdown ?? "", Pipeline);
        var content = new JsonArray();
        foreach (var block in document) AppendBlock(block, content);
        // ProseMirror's schema requires at least one block.
        if (content.Count == 0) content.Add(Paragraph(new JsonArray()));
        return new JsonObject { ["type"] = "doc", ["content"] = content }.ToJsonString();
    }

    private static void AppendBlock(Block block, JsonArray into)
    {
        switch (block)
        {
            case HeadingBlock heading:
                into.Add(new JsonObject
                {
                    ["type"] = "heading",
                    ["attrs"] = new JsonObject { ["level"] = Math.Clamp(heading.Level, 1, 6) },
                    ["content"] = Inlines(heading.Inline),
                });
                break;

            case ParagraphBlock paragraph:
                into.Add(Paragraph(Inlines(paragraph.Inline)));
                break;

            case ThematicBreakBlock:
                into.Add(new JsonObject { ["type"] = "horizontalRule" });
                break;

            case FencedCodeBlock fenced:
                into.Add(new JsonObject
                {
                    ["type"] = "codeBlock",
                    ["attrs"] = new JsonObject { ["language"] = fenced.Info is { Length: > 0 } l ? l : "plaintext" },
                    ["content"] = TextRun(CodeText(fenced)),
                });
                break;

            case CodeBlock code:
                into.Add(new JsonObject
                {
                    ["type"] = "codeBlock",
                    ["attrs"] = new JsonObject { ["language"] = "plaintext" },
                    ["content"] = TextRun(CodeText(code)),
                });
                break;

            case QuoteBlock quote:
                var quoted = new JsonArray();
                foreach (var child in quote) AppendBlock(child, quoted);
                if (quoted.Count == 0) quoted.Add(Paragraph(new JsonArray()));
                into.Add(new JsonObject { ["type"] = "blockquote", ["content"] = quoted });
                break;

            case ListBlock list:
                into.Add(BuildList(list));
                break;

            case Markdig.Extensions.Tables.Table table:
                into.Add(BuildTable(table));
                break;

            default:
                // Anything else keeps its text rather than vanishing.
                if (block is LeafBlock leaf && leaf.Inline is not null)
                    into.Add(Paragraph(Inlines(leaf.Inline)));
                break;
        }
    }

    /// <summary>
    /// A list where *any* item carries a checkbox becomes a task list, and
    /// its unchecked-marker-less items become unchecked tasks: the schema
    /// has no "list with some checkboxes", so one kind has to win, and
    /// promoting is lossless where demoting would throw the checkboxes away.
    ///
    /// Note that two `-` lists separated only by a blank line are *one* list
    /// in CommonMark, so mixing is easier to write than it looks.
    /// </summary>
    private static JsonObject BuildList(ListBlock list)
    {
        var isTask = list.OfType<ListItemBlock>()
            .Any(item => item.FirstOrDefault() is ParagraphBlock p
                && p.Inline?.FirstChild is TaskList);

        var items = new JsonArray();
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var blocks = new JsonArray();
            var check = false;
            foreach (var child in item)
            {
                if (isTask && child is ParagraphBlock para && para.Inline?.FirstChild is TaskList task)
                {
                    check = task.Checked;
                    // Drop the checkbox marker itself; its state is an attribute.
                    blocks.Add(Paragraph(Inlines(para.Inline, skipFirstTaskMarker: true)));
                    continue;
                }
                AppendBlock(child, blocks);
            }
            if (blocks.Count == 0) blocks.Add(Paragraph(new JsonArray()));

            items.Add(isTask
                ? new JsonObject { ["type"] = "taskItem", ["attrs"] = new JsonObject { ["checked"] = check }, ["content"] = blocks }
                : new JsonObject { ["type"] = "listItem", ["content"] = blocks });
        }

        return new JsonObject
        {
            ["type"] = isTask ? "taskList" : list.IsOrdered ? "orderedList" : "bulletList",
            ["content"] = items,
        };
    }

    /// <summary>The first row becomes the header row, which is what a GFM table means.</summary>
    private static JsonObject BuildTable(Markdig.Extensions.Tables.Table table)
    {
        var rows = new JsonArray();
        var first = true;
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = new JsonArray();
            foreach (var cell in row.OfType<TableCell>())
            {
                var blocks = new JsonArray();
                foreach (var child in cell) AppendBlock(child, blocks);
                if (blocks.Count == 0) blocks.Add(Paragraph(new JsonArray()));
                cells.Add(new JsonObject
                {
                    ["type"] = first || row.IsHeader ? "tableHeader" : "tableCell",
                    ["content"] = blocks,
                });
            }
            rows.Add(new JsonObject { ["type"] = "tableRow", ["content"] = cells });
            first = false;
        }
        return new JsonObject { ["type"] = "table", ["content"] = rows };
    }

    private static JsonArray Inlines(ContainerInline? container, bool skipFirstTaskMarker = false)
    {
        var content = new JsonArray();
        if (container is null) return content;
        var skipped = !skipFirstTaskMarker;
        foreach (var inline in container)
        {
            if (!skipped && inline is TaskList) { skipped = true; continue; }
            skipped = true;
            AppendInline(inline, content, []);
        }
        // Markdig models "[x] done" as a TaskList inline followed by the
        // literal " done": the separating space belongs to the marker, not
        // the text. Dropping the marker without it makes every round trip
        // add a space ("- [x]  done"), which compounds on each edit.
        if (skipFirstTaskMarker) TrimLeadingSpace(content);
        Trim(content);
        return content;
    }

    private static void TrimLeadingSpace(JsonArray content)
    {
        if (content.Count == 0 || content[0] is not JsonObject first) return;
        if (first["type"]?.GetValue<string>() != "text") return;
        var text = first["text"]?.GetValue<string>() ?? "";
        if (!text.StartsWith(' ')) return;
        var trimmed = text[1..];
        if (trimmed.Length == 0) content.RemoveAt(0);
        else first["text"] = trimmed;
    }

    private static void AppendInline(Inline inline, JsonArray into, IReadOnlyList<JsonObject> marks)
    {
        switch (inline)
        {
            case LiteralInline literal:
                Add(into, literal.Content.ToString(), marks);
                break;

            case CodeInline code:
                Add(into, code.Content, [.. marks, Mark("code")]);
                break;

            case LineBreakInline lineBreak:
                if (lineBreak.IsHard) into.Add(new JsonObject { ["type"] = "hardBreak" });
                else Add(into, " ", marks);
                break;

            case EmphasisInline emphasis:
                var mark = emphasis.DelimiterChar is '~'
                    ? Mark("strike")
                    : emphasis.DelimiterCount >= 2 ? Mark("bold") : Mark("italic");
                foreach (var child in emphasis) AppendInline(child, into, [.. marks, mark]);
                break;

            case LinkInline { IsImage: true } image:
                into.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["attrs"] = new JsonObject
                    {
                        ["src"] = image.Url ?? "",
                        ["alt"] = PlainText(image),
                    },
                });
                break;

            case LinkInline link:
                var linkMark = new JsonObject
                {
                    ["type"] = "link",
                    ["attrs"] = new JsonObject { ["href"] = link.Url ?? "" },
                };
                var inner = new JsonArray();
                foreach (var child in link) AppendInline(child, inner, [.. marks, linkMark]);
                // A bare link with no text still has to say something.
                if (inner.Count == 0) Add(inner, link.Url ?? "", [.. marks, linkMark]);
                foreach (var node in inner.ToList()) { inner.Remove(node); into.Add(node); }
                break;

            case AutolinkInline auto:
                Add(into, auto.Url, [.. marks, new JsonObject
                {
                    ["type"] = "link",
                    ["attrs"] = new JsonObject { ["href"] = auto.Url },
                }]);
                break;

            case HtmlInline or HtmlEntityInline:
                // Raw HTML is not part of the contract; keep its text.
                Add(into, inline is HtmlEntityInline entity ? entity.Transcoded.ToString() : "", marks);
                break;

            case ContainerInline container:
                foreach (var child in container) AppendInline(child, into, marks);
                break;
        }
    }

    private static void Add(JsonArray into, string text, IReadOnlyList<JsonObject> marks)
    {
        if (text.Length == 0) return;
        var node = new JsonObject { ["type"] = "text", ["text"] = text };
        if (marks.Count > 0)
        {
            var array = new JsonArray();
            foreach (var mark in marks) array.Add(mark.DeepClone());
            node["marks"] = array;
        }
        into.Add(node);
    }

    private static JsonObject Mark(string type) => new() { ["type"] = type };

    private static JsonObject Paragraph(JsonArray content) =>
        new() { ["type"] = "paragraph", ["content"] = content };

    private static JsonArray TextRun(string text)
    {
        var array = new JsonArray();
        if (text.Length > 0) array.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        return array;
    }

    private static string CodeText(LeafBlock block)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < block.Lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(block.Lines.Lines[i].Slice.ToString());
        }
        return sb.ToString();
    }

    private static string PlainText(ContainerInline container)
    {
        var sb = new StringBuilder();
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal) sb.Append(literal.Content.ToString());
            else if (inline is ContainerInline nested) sb.Append(PlainText(nested));
        }
        return sb.ToString();
    }

    /// <summary>Markdig keeps the trailing newline of a paragraph's last line; ProseMirror should not.</summary>
    private static void Trim(JsonArray content)
    {
        if (content.Count == 0) return;
        if (content[^1] is JsonObject last && last["type"]?.GetValue<string>() == "text")
        {
            var text = last["text"]?.GetValue<string>() ?? "";
            var trimmed = text.TrimEnd('\n', '\r');
            if (trimmed.Length == 0) content.RemoveAt(content.Count - 1);
            else last["text"] = trimmed;
        }
    }
}
