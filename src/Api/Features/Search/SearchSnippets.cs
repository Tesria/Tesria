using System.Data.Common;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Search;

/// <summary>
/// The passage of a page that actually matched, and how well it scored.
///
/// The first 200 characters of a page is not a snippet: searching
/// "webhook" and being shown a page's opening sentence tells a reader (and
/// an assistant) nothing about why it came back, so the only way to judge
/// relevance is to open every result. Postgres has <c>ts_headline</c> for
/// exactly this; it has no EF binding, so this is the one place the app
/// writes SQL by hand.
/// </summary>
public static class SearchSnippets
{
    public sealed record Match(string Snippet, double? Score);

    /// <summary>Marks the matching words. Plain text rather than HTML: these snippets go to an assistant as often as to a browser.</summary>
    private const string StartMarker = "**";
    private const string StopMarker = "**";

    private const int FallbackWindow = 240;

    /// <summary>
    /// Snippets and scores for pages already narrowed and permission-checked
    /// by the caller. One round trip for the whole page, not one per result.
    /// </summary>
    public static async Task<Dictionary<Guid, Match>> ForAsync(
        AppDbContext db, IReadOnlyList<Guid> pageIds, string term, CancellationToken ct)
    {
        var found = new Dictionary<Guid, Match>();
        if (pageIds.Count == 0) return found;

        if (!db.Database.IsNpgsql())
        {
            // SQLite (tests) has no full-text engine here; a window around the
            // first matching word is still far better than the page's opening.
            var rows = await db.Pages.AsNoTracking()
                .Where(p => pageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.SearchText })
                .ToListAsync(ct);
            foreach (var row in rows) found[row.Id] = new Match(Window(row.SearchText, term), null);
            return found;
        }

        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            // ts_headline picks the fragments containing the query terms;
            // ts_rank is the same score the ORDER BY uses, surfaced so a
            // client can decide what is worth reading.
            command.CommandText = $"""
                select "Id",
                       ts_headline('english', "SearchText", websearch_to_tsquery('english', @term),
                           'StartSel={StartMarker}, StopSel={StopMarker}, MaxWords=40, MinWords=20, ShortWord=3, MaxFragments=2, FragmentDelimiter= … '),
                       ts_rank("SearchVector", websearch_to_tsquery('english', @term))
                from "Pages"
                where "Id" = any(@ids)
                """;
            command.Parameters.Add(Parameter(command, "term", term));
            command.Parameters.Add(Parameter(command, "ids", pageIds.ToArray()));

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var snippet = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var score = reader.IsDBNull(2) ? (double?)null : Math.Round(reader.GetFloat(2), 6);
                found[id] = new Match(snippet.Trim(), score);
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
        return found;
    }

    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    /// <summary>
    /// A window around the first query word found, with that word marked:
    /// the fallback when the database cannot do it properly.
    /// </summary>
    public static string Window(string text, string term)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var words = term.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('"', '\'', '(', ')'))
            .Where(w => w.Length > 2)
            .ToList();

        var at = -1;
        var matched = "";
        foreach (var word in words)
        {
            var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || (at >= 0 && index >= at)) continue;
            at = index;
            matched = word;
        }
        if (at < 0)
            return text.Length <= FallbackWindow ? text : text[..FallbackWindow].TrimEnd() + " …";

        var start = Math.Max(0, at - FallbackWindow / 3);
        var length = Math.Min(FallbackWindow, text.Length - start);
        var slice = text.Substring(start, length);
        // Mark the hit, so the fallback reads like the real thing.
        var hit = slice.IndexOf(matched, StringComparison.OrdinalIgnoreCase);
        if (hit >= 0)
            slice = slice[..hit] + StartMarker + slice.Substring(hit, matched.Length) + StopMarker
                  + slice[(hit + matched.Length)..];

        return (start > 0 ? "… " : "") + slice.Trim() + (start + length < text.Length ? " …" : "");
    }
}
