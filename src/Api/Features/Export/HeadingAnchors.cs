using System.Text;
using System.Text.Json;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Heading ids for export, derived from heading text exactly the way the
/// editor derives them (<c>src/web/src/editor/headingAnchors.ts</c>): the
/// ids are never stored, so a link saved as <c>#setup</c> resolves in the
/// app and in an exported file only because both sides run the same rule.
/// Change one, change both, and keep <c>HeadingAnchorTests</c> green.
/// </summary>
public static class HeadingAnchors
{
    public sealed record Anchor(int Level, string Text, string Id);

    /// <summary>
    /// Lower-case; letters and decimal digits kept (any script); every other
    /// run of characters collapsed to one hyphen; hyphens trimmed. Empty
    /// becomes "heading".
    /// </summary>
    public static string Slugify(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingHyphen = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingHyphen && sb.Length > 0) sb.Append('-');
                pendingHyphen = false;
                sb.Append(Rune.ToLowerInvariant(rune).ToString());
            }
            else
            {
                pendingHyphen = true;
            }
        }
        return sb.Length == 0 ? "heading" : sb.ToString();
    }

    /// <summary>Headings in document order, ids de-duplicated with a numeric suffix (<c>setup</c>, <c>setup-2</c>, …).</summary>
    public static List<Anchor> Collect(JsonElement root)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var anchors = new List<Anchor>();
        Walk(root);
        return anchors;

        void Walk(JsonElement node)
        {
            if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "heading")
            {
                var text = PlainText(node);
                var baseId = Slugify(text);
                var id = baseId;
                for (var n = 2; used.Contains(id); n++) id = $"{baseId}-{n}";
                used.Add(id);
                var level = 1;
                if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
                    && attrs.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number)
                    level = Math.Clamp(l.GetInt32(), 1, 6);
                anchors.Add(new Anchor(level, text, id));
            }
            if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var child in content.EnumerateArray()) Walk(child);
        }
    }

    private static string PlainText(JsonElement node)
    {
        var sb = new StringBuilder();
        Walk(node);
        return sb.ToString();

        void Walk(JsonElement n)
        {
            if (n.TryGetProperty("type", out var t) && t.GetString() == "text" && n.TryGetProperty("text", out var text))
                sb.Append(text.GetString());
            if (n.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var c in content.EnumerateArray()) Walk(c);
        }
    }
}
