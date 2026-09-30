using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// The note get_page puts on one section of a page, so that section is never
/// taken for the whole page on its way back.
///
/// An assistant that read one section (get_page with <c>section</c>) and sent
/// it back through update_page as the page's content replaced the entire
/// page with that one section. Now the section comes back marked: as a
/// Markdown comment on its first line, which also tells the assistant what to
/// do with it, or as a <c>section</c> property on the document for the JSON
/// format. update_page refuses a marked body unless it names the section it
/// replaces, and then replaces only that section.
/// </summary>
public static partial class SectionMark
{
    private const string Prefix = "tesria-section:";

    /// <summary>The section's Markdown with the note above it.</summary>
    public static string Markdown(string markdown, string section) =>
        $"<!-- {Prefix} {section}. Only this section of the page, not all of it. To save a change, send it back " +
        $"to update_page with section \"{section}\", which replaces this section and keeps the rest of the page. -->\n\n"
        + markdown;

    /// <summary>The section's ProseMirror document with its section named on it.</summary>
    public static string Json(string json, string section)
    {
        if (JsonNode.Parse(json) is not JsonObject doc) return json;
        doc["section"] = section;
        return doc.ToJsonString();
    }

    /// <summary>The section a body says it is, or null for a body that is not marked.</summary>
    public static string? Of(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        if (body.TrimStart().StartsWith('{'))
        {
            try
            {
                return JsonNode.Parse(body) is JsonObject doc
                    && doc["section"] is JsonValue value && value.TryGetValue<string>(out var id)
                    ? id
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        var match = MarkdownNote().Match(body);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>The body without its note, for storing.</summary>
    public static string Strip(string body)
    {
        if (body.TrimStart().StartsWith('{'))
        {
            try
            {
                if (JsonNode.Parse(body) is JsonObject doc && doc.Remove("section")) return doc.ToJsonString();
            }
            catch (JsonException)
            {
                // Not JSON after all: Markdown that starts with a brace.
            }
            return body;
        }
        return MarkdownNoteWhole().Replace(body, "");
    }

    // A heading id is letters and digits joined by single hyphens (HeadingAnchors.Slugify).
    [GeneratedRegex(@"^[ \t]*<!--[ \t]*tesria-section:[ \t]*(?<id>[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*)", RegexOptions.Multiline)]
    private static partial Regex MarkdownNote();

    [GeneratedRegex(@"^[ \t]*<!--[ \t]*tesria-section:.*?-->[ \t]*(?:\r?\n)?", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MarkdownNoteWhole();
}
