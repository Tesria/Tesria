using System.Text.Json;

namespace Tesria.Api.Infrastructure.Mentions;

/// <summary>
/// Finds the users a stored ProseMirror document mentions (dev-plan Phase 7
/// Wave C). The editor writes a <c>mention</c> inline node carrying the
/// mentioned user's id; everything the server needs is in the saved JSON, so
/// no separate mention table exists to fall out of step with the content.
/// </summary>
public static class Mentions
{
    /// <summary>
    /// Distinct user ids mentioned anywhere in the document. Malformed JSON,
    /// or a mention with no parseable id, yields nothing rather than throwing:
    /// document JSON is stored as the client sent it, so this must tolerate
    /// anything.
    /// </summary>
    public static HashSet<Guid> UserIdsIn(string? contentJson)
    {
        var found = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(contentJson)) return found;

        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            Walk(doc.RootElement);
        }
        catch (JsonException)
        {
            return found;
        }
        return found;

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Walk(child);
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;

            if (node.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "mention"
                && node.TryGetProperty("attrs", out var attrs)
                && attrs.ValueKind == JsonValueKind.Object
                && attrs.TryGetProperty("userId", out var id)
                && id.ValueKind == JsonValueKind.String
                && Guid.TryParse(id.GetString(), out var userId))
            {
                found.Add(userId);
            }

            if (node.TryGetProperty("content", out var content)) Walk(content);
        }
    }

    /// <summary>
    /// Who to notify after an edit: everyone mentioned now who was not
    /// mentioned before, minus the author.
    ///
    /// Diffing rather than notifying on every save is what keeps a page that
    /// mentions ten people from pinging all ten on every typo fix.
    /// </summary>
    public static IReadOnlyCollection<Guid> NewlyMentioned(string? before, string? after, Guid authorId)
    {
        var previous = UserIdsIn(before);
        var current = UserIdsIn(after);
        current.ExceptWith(previous);
        current.Remove(authorId);
        return current;
    }
}
