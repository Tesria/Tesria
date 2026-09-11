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

    public static bool TryNormalize(string? input, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            normalized = EmptyDoc;
            return true;
        }
        try
        {
            using var _ = JsonDocument.Parse(input);
            normalized = input;
            return true;
        }
        catch (JsonException)
        {
            normalized = EmptyDoc;
            return false;
        }
    }

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
