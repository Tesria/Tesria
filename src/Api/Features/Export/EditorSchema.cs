using System.Text.Json.Nodes;

namespace Tesria.Api.Features.Export;

/// <summary>
/// The element and mark types the editor knows, for checking a document
/// that came from outside this instance before it is stored.
///
/// <para>The editor's schema is declared once, in
/// <c>src/web/src/editor/extensions.ts</c>. This is a copy of its names only,
/// because the server cannot run it; <c>src/web/src/editor/serverSchema.test.ts</c>
/// builds the real schema and fails when the two disagree, so a type added
/// there and forgotten here fails the web tests rather than a user's
/// import.</para>
///
/// <para>Why it matters: the editor drops a whole document it cannot parse,
/// rather than the one element it does not know. A pack page with one
/// unknown element was imported and then showed as a blank page, and saving
/// it from the editor would have stored the blank (t6-015).</para>
/// </summary>
public static class EditorSchema
{
    public static readonly IReadOnlySet<string> NodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "attachmentBlock", "blockquote", "bulletList", "chart", "codeBlock", "date", "decision", "doc",
        "dynamicBlock", "embed", "excerpt", "expand", "gallery", "hardBreak", "heading", "horizontalRule",
        "image", "layoutColumn", "layoutSection", "listItem", "math", "mention", "orderedList",
        "pageProperties", "panel", "paragraph", "smartLink", "smartLinkInline", "status", "table",
        "tableCell", "tableHeader", "tableOfContents", "tableRow", "taskItem", "taskList", "text",
    };

    public static readonly IReadOnlySet<string> MarkTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "bold", "code", "comment", "externalDelete", "externalInsert", "highlight", "italic", "link",
        "strike", "subscript", "superscript", "textColor", "underline",
    };

    /// <summary>
    /// The first element or mark type in <paramref name="document"/> that the
    /// editor does not know, or null when it knows them all. Only the types
    /// are checked: an unknown attribute is ignored by the editor, not fatal.
    /// </summary>
    public static string? FirstUnknownType(JsonNode? document)
    {
        // Something that is not an element at all is as unreadable to the
        // editor as an element it has never heard of.
        if (document is not JsonObject node) return Untyped;
        var name = TypeOf(node);
        if (name is null || !NodeTypes.Contains(name)) return name ?? Untyped;
        if (node["marks"] is JsonArray marks)
            foreach (var mark in marks)
            {
                var markName = mark is JsonObject m ? TypeOf(m) : null;
                if (markName is null || !MarkTypes.Contains(markName)) return markName ?? Untyped;
            }
        if (node["content"] is JsonArray content)
            foreach (var child in content)
                if (FirstUnknownType(child) is { } unknown) return unknown;
        return null;
    }

    /// <summary>
    /// Each element type's content, as the editor declares it (ProseMirror
    /// content expressions), and the groups those expressions name. Also
    /// copied from the editor's schema and kept honest by
    /// <c>serverSchema.test.ts</c>.
    ///
    /// <para>Names alone let through a document that puts things where the
    /// editor cannot have them, such as text straight under the document:
    /// accepted, then opened empty in the editor, where Update would have
    /// published the empty page (t5-R05, the 0.8.3 retest).</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Content = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["attachmentBlock"] = "", ["blockquote"] = "block+", ["bulletList"] = "listItem+", ["chart"] = "",
        ["codeBlock"] = "text*", ["date"] = "", ["decision"] = "block+", ["doc"] = "(block | layoutSection)+",
        ["dynamicBlock"] = "", ["embed"] = "", ["excerpt"] = "block+", ["expand"] = "block+", ["gallery"] = "block+",
        ["hardBreak"] = "", ["heading"] = "inline*", ["horizontalRule"] = "", ["image"] = "",
        ["layoutColumn"] = "block+", ["layoutSection"] = "layoutColumn{2,3}", ["listItem"] = "paragraph block*",
        ["math"] = "", ["mention"] = "", ["orderedList"] = "listItem+", ["pageProperties"] = "block+",
        ["panel"] = "block+", ["paragraph"] = "inline*", ["smartLink"] = "", ["smartLinkInline"] = "",
        ["status"] = "", ["table"] = "tableRow+", ["tableCell"] = "block+", ["tableHeader"] = "block+",
        ["tableOfContents"] = "", ["tableRow"] = "(tableCell | tableHeader)*", ["taskItem"] = "paragraph block*",
        ["taskList"] = "taskItem+", ["text"] = "",
    };

    /// <summary>Each element type's groups, space-separated, as the editor declares them.</summary>
    public static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["attachmentBlock"] = "block", ["blockquote"] = "block", ["bulletList"] = "block list", ["chart"] = "block",
        ["codeBlock"] = "block", ["date"] = "inline", ["decision"] = "block", ["doc"] = "", ["dynamicBlock"] = "block",
        ["embed"] = "block", ["excerpt"] = "block", ["expand"] = "block", ["gallery"] = "block", ["hardBreak"] = "inline",
        ["heading"] = "block", ["horizontalRule"] = "block", ["image"] = "block", ["layoutColumn"] = "",
        ["layoutSection"] = "", ["listItem"] = "", ["math"] = "inline", ["mention"] = "inline",
        ["orderedList"] = "block list", ["pageProperties"] = "block", ["panel"] = "block", ["paragraph"] = "block",
        ["smartLink"] = "block", ["smartLinkInline"] = "inline", ["status"] = "inline", ["table"] = "block",
        ["tableCell"] = "", ["tableHeader"] = "", ["tableOfContents"] = "block", ["tableRow"] = "",
        ["taskItem"] = "", ["taskList"] = "block list", ["text"] = "inline",
    };

    /// <summary>
    /// Why <paramref name="type"/> cannot hold these children, in words, or
    /// null when it can. The expressions above use only a small part of
    /// ProseMirror's syntax (names, groups, <c>(a | b)</c>, and <c>*</c>,
    /// <c>+</c> or <c>{m,n}</c>), so a greedy walk decides them.
    /// </summary>
    public static string? ChildrenProblem(string type, IReadOnlyList<string> children)
    {
        if (!Content.TryGetValue(type, out var expression)) return null;
        if (expression.Length == 0)
            return children.Count == 0 ? null : $"A \"{type}\" holds no content.";
        var at = 0;
        foreach (var term in System.Text.RegularExpressions.Regex.Matches(expression, @"(\([^)]*\)|[A-Za-z]+)(\*|\+|\{\d+,\d+\})?"))
        {
            var match = (System.Text.RegularExpressions.Match)term;
            var names = match.Groups[1].Value.Trim('(', ')').Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var (min, max) = match.Groups[2].Value switch
            {
                "*" => (0, int.MaxValue),
                "+" => (1, int.MaxValue),
                "" => (1, 1),
                var range => (int.Parse(range[1..^1].Split(',')[0]), int.Parse(range[1..^1].Split(',')[1])),
            };
            var count = 0;
            while (at < children.Count && count < max && names.Any(n => Fits(children[at], n))) { at++; count++; }
            if (count < min)
                return at < children.Count
                    ? $"A \"{children[at]}\" cannot go inside a \"{type}\"."
                    : $"A \"{type}\" cannot be empty.";
        }
        return at < children.Count ? $"A \"{children[at]}\" cannot go inside a \"{type}\"." : null;
    }

    private static bool Fits(string child, string name) =>
        child == name || (Groups.TryGetValue(child, out var groups) && groups.Split(' ').Contains(name));

    /// <summary>What <see cref="FirstUnknownType"/> says of an element with no type.</summary>
    public const string Untyped = "(an element with no type)";

    private static string? TypeOf(JsonObject node) =>
        node["type"] is JsonValue type && type.TryGetValue<string>(out var name) ? name : null;
}
