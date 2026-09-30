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

    /// <summary>What <see cref="FirstUnknownType"/> says of an element with no type.</summary>
    public const string Untyped = "(an element with no type)";

    private static string? TypeOf(JsonObject node) =>
        node["type"] is JsonValue type && type.TryGetValue<string>(out var name) ? name : null;
}
