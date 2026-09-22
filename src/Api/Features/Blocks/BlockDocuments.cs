using System.Text.Json;

namespace Tesria.Api.Features.Blocks;

/// <summary>
/// Reading pieces out of a stored ProseMirror document: the three
/// `document`- and content-driven kinds all need to, and none of them
/// should be parsing JSON by hand.
/// </summary>
public static class BlockDocuments
{
    /// <summary>The first node of the given type, as its own `doc`, or null when there is none.</summary>
    public static string? FirstNodeAsDocument(string? contentJson, string nodeType)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            var found = Find(doc.RootElement);
            if (found is null) return null;
            // Re-wrap the node's *children* as a document, so the container
            // itself (an `excerpt` frame) does not travel with the content.
            var content = found.Value.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.GetRawText()
                : "[]";
            return $"{{\"type\":\"doc\",\"content\":{content}}}";
        }
        catch (JsonException)
        {
            return null;
        }

        JsonElement? Find(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray())
                    if (Find(child) is { } hit) return hit;
                return null;
            }
            if (node.ValueKind != JsonValueKind.Object) return null;
            if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == nodeType)
                return node.Clone();
            return node.TryGetProperty("content", out var content) ? Find(content) : null;
        }
    }

    public sealed record TaskRow(string Text, bool Checked, Guid? AssigneeId, string? AssigneeName);

    /// <summary>
    /// Every `taskItem` in a document, with the Wave C assignee attributes.
    /// Walked in process: at wiki scale the candidate set is already small
    /// after the visibility filter. If it ever is not, the first optimisation
    /// is a jsonb containment prefilter (<c>@&gt; '{"type":"taskItem"}'</c>)
    /// to skip pages with no tasks before loading their content at all.
    /// </summary>
    public static List<TaskRow> Tasks(string? contentJson)
    {
        var rows = new List<TaskRow>();
        if (string.IsNullOrWhiteSpace(contentJson)) return rows;
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            Walk(doc.RootElement);
        }
        catch (JsonException) { /* stored as sent */ }
        return rows;

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array) { foreach (var c in node.EnumerateArray()) Walk(c); return; }
            if (node.ValueKind != JsonValueKind.Object) return;

            if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "taskItem")
            {
                Guid? assignee = null;
                string? name = null;
                var done = false;
                if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                {
                    done = attrs.TryGetProperty("checked", out var c) && c.ValueKind == JsonValueKind.True;
                    if (attrs.TryGetProperty("assigneeId", out var a) && a.ValueKind == JsonValueKind.String
                        && Guid.TryParse(a.GetString(), out var id)) assignee = id;
                    if (attrs.TryGetProperty("assigneeName", out var n) && n.ValueKind == JsonValueKind.String)
                        name = n.GetString();
                }
                rows.Add(new TaskRow(PlainText(node).Trim(), done, assignee, name));
                // Task lists nest; keep walking for the children.
            }

            if (node.TryGetProperty("content", out var content)) Walk(content);
        }
    }

    public sealed record PropertyRow(string Key, string Value);

    /// <summary>
    /// The key/value pairs of the first `pageProperties` node: a two-column
    /// table, first column the key, second the value. Rows with an empty key
    /// are skipped: an author's blank row is not a column.
    /// </summary>
    public static List<PropertyRow> Properties(string? contentJson)
    {
        var rows = new List<PropertyRow>();
        var fragment = FirstNodeAsDocument(contentJson, "pageProperties");
        if (fragment is null) return rows;
        try
        {
            using var doc = JsonDocument.Parse(fragment);
            var table = FindType(doc.RootElement, "table");
            if (table is null) return rows;
            foreach (var row in Children(table.Value))
            {
                if (TypeOf(row) != "tableRow") continue;
                var cells = Children(row).Where(c => TypeOf(c) is "tableCell" or "tableHeader").ToList();
                if (cells.Count < 2) continue;
                var key = PlainText(cells[0]).Trim();
                if (key.Length == 0) continue;
                rows.Add(new PropertyRow(key, PlainText(cells[1]).Trim()));
            }
        }
        catch (JsonException) { /* stored as sent */ }
        return rows;
    }

    private static JsonElement? FindType(JsonElement node, string type)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in node.EnumerateArray()) if (FindType(c, type) is { } hit) return hit;
            return null;
        }
        if (node.ValueKind != JsonValueKind.Object) return null;
        if (TypeOf(node) == type) return node.Clone();
        return node.TryGetProperty("content", out var content) ? FindType(content, type) : null;
    }

    private static string TypeOf(JsonElement node) =>
        node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray() : [];

    private static string PlainText(JsonElement node)
    {
        var sb = new System.Text.StringBuilder();
        Walk(node);
        return sb.ToString();

        void Walk(JsonElement n)
        {
            if (TypeOf(n) == "text" && n.TryGetProperty("text", out var t)) sb.Append(t.GetString());
            // A mention reads as its label, so an assignee named in a task
            // still has a name in a report.
            if (TypeOf(n) == "mention" && n.TryGetProperty("attrs", out var a)
                && a.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String)
                sb.Append('@').Append(l.GetString());
            foreach (var c in Children(n)) Walk(c);
        }
    }
}
