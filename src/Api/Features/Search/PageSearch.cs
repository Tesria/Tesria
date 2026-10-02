using System.Data.Common;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Tesria.Api.Features.Search;

/// <summary>
/// Search, as the REST endpoint and the MCP tool both run it: find the pages
/// that match, rank them by BM25 (<see cref="Bm25"/>, dev-plan 23.1), keep
/// the ones the searcher may read, and fetch the matching passage of each.
/// One implementation, so the two doors cannot rank or filter differently.
///
/// <para>The full-text index still finds the matches; only the order is
/// computed here. For the matching pages the database supplies small counts
/// (how often each query word appears in the title and the text, and how
/// long each is), never the pages themselves, and the score is computed
/// in the app. Worst case measured: a word on three pages in four of a
/// 10,000-page wiki, about 75 ms of database time on a laptop against 25 ms
/// for <c>ts_rank</c>; a rare word, under 5 ms.</para>
/// </summary>
public static class PageSearch
{
    public sealed record Hit(Guid Id, Guid SpaceId, string SpaceKey, string Title, string Snippet, double? Score);

    /// <summary>Permission checks one search may make, as before: pages hidden by restrictions are dropped after ranking.</summary>
    private const int MaxChecked = 1000;

    public static async Task<IReadOnlyList<Hit>> RunAsync(
        AppDbContext db, IPermissionService perms, SearchStatistics statistics,
        string query, Guid? spaceId, int limit, CancellationToken ct)
    {
        var term = (query ?? "").Trim();
        if (term.Length == 0) return [];

        // The soft-delete query filter already excludes trashed pages and drafts.
        // Never anything from spaces the caller cannot view.
        var viewableSpaces = await perms.ViewableSpaceIdsAsync();
        IQueryable<Page> visible = db.Pages.AsNoTracking().Where(p => viewableSpaces.Contains(p.SpaceId));
        var scoped = spaceId is { } sid ? visible.Where(p => p.SpaceId == sid) : visible;

        List<(Guid Id, double? Score)> ranked;
        string snippetQuery;
        if (db.Database.IsNpgsql())
        {
            var parsed = await ParseAsync(db, term, ct);
            if (parsed is null) return [];
            snippetQuery = parsed.Value.Folded;
            ranked = await RankAsync(db, visible, scoped, parsed.Value.Folded, parsed.Value.Words, statistics, ct);
        }
        else
        {
            // Portable fallback (SQLite tests): case-insensitive substring
            // match on the title or the text, in title order, unscored.
            var like = $"%{term}%";
            snippetQuery = term;
            ranked = (await scoped
                    .Where(p => EF.Functions.Like(p.Title, like) || EF.Functions.Like(p.SearchText, like))
                    .OrderBy(p => p.Title)
                    .Select(p => p.Id)
                    .Take(MaxChecked)
                    .ToListAsync(ct))
                .Select(id => (id, (double?)null))
                .ToList();
        }

        // Space access is not enough: pages hidden by page restrictions are
        // dropped too, after ranking, reading on until there are enough
        // readable hits (found 2026-09-23: taking the first 50 and then
        // filtering returned fewer than 50 when more existed). The cap
        // bounds the work a query can cause.
        var allowed = new List<(Guid Id, double? Score)>();
        foreach (var hit in ranked.Take(MaxChecked))
        {
            if (allowed.Count >= limit) break;
            if (await perms.CanViewPageAsync(hit.Id)) allowed.Add(hit);
        }
        if (allowed.Count == 0) return [];

        var ids = allowed.Select(a => a.Id).ToList();
        var rows = await db.Pages.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.SpaceId, SpaceKey = p.Space!.Key, p.Title })
            .ToDictionaryAsync(p => p.Id, ct);
        // The passage that matched, not the page's opening line: computed
        // once for the survivors, so nothing is computed for a page that will
        // not be returned (SearchSnippets).
        var snippets = await SearchSnippets.ForAsync(db, ids, snippetQuery, ct);
        return allowed
            .Where(a => rows.ContainsKey(a.Id))
            .Select(a =>
            {
                var row = rows[a.Id];
                return new Hit(row.Id, row.SpaceId, row.SpaceKey, row.Title,
                    snippets.TryGetValue(a.Id, out var s) ? s : "",
                    a.Score is { } score ? Math.Round(score, 4) : null);
            })
            .ToList();
    }

    /// <summary>
    /// The query folded the way the index is (accents dropped,
    /// <see cref="SearchIndex.Fold"/>) and the words it asks for, both from
    /// the database so they match the index exactly. Null when nothing is
    /// left to search for (only stop words, such as "the").
    /// </summary>
    private static async Task<(string Folded, IReadOnlyList<string> Words)?> ParseAsync(
        AppDbContext db, string term, CancellationToken ct)
    {
        var row = await QueryAsync(db, $"""
            select f, websearch_to_tsquery('{SearchIndex.Config}', f)::text
            from (select {SearchIndex.Fold("@term")} as f) folded
            """, [("term", term)], r => (r.GetString(0), r.GetString(1)), ct);
        if (row.Count == 0 || row[0].Item2.Length == 0) return null;
        return (row[0].Item1, QueryWords.Wanted(row[0].Item2));
    }

    private static async Task<List<(Guid Id, double? Score)>> RankAsync(
        AppDbContext db, IQueryable<Page> visible, IQueryable<Page> scoped,
        string folded, IReadOnlyList<string> words, SearchStatistics statistics, CancellationToken ct)
    {
        // The matches. WebSearchToTsQuery accepts user-friendly query syntax;
        // the EF.Functions call is inlined so it is translated to SQL.
        var candidates = await scoped
            .Where(p => p.SearchVector!.Matches(EF.Functions.WebSearchToTsQuery(SearchIndex.Config, folded)))
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        // Rarity, among the pages this searcher can see: counting pages they
        // cannot would let a score hint at what those pages say. Each count is
        // answered by the full-text index.
        var documents = await visible.LongCountAsync(ct);
        var frequency = new long[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            NpgsqlTsQuery word = new NpgsqlTsQueryLexeme(words[i]);
            frequency[i] = await visible.LongCountAsync(p => p.SearchVector!.Matches(word), ct);
        }
        var averages = await statistics.AveragesAsync(db, ct);
        var corpus = new Bm25.Corpus(documents, averages.TitleLength, averages.BodyLength);

        // Per page, only the query words survive the filter (setweight marks
        // them, ts_filter keeps them), so unnesting what is left is cheap:
        // unnesting a whole page's index entry instead was about three times
        // slower at 10,000 pages.
        var features = await QueryAsync(db, """
            select p."Id", p."Title", length(p."SearchVector"), length(t.v), a.words, a.n, w.words, w.n
            from "Pages" p
            cross join lateral (select ts_filter(p."SearchVector", '{a}') as v) t
            cross join lateral (select array_agg(u.lexeme) as words, array_agg(cardinality(u.positions)) as n
                                from unnest(ts_filter(setweight(p."SearchVector", 'B', @words), '{b}')) u) a
            cross join lateral (select array_agg(u.lexeme) as words, array_agg(cardinality(u.positions)) as n
                                from unnest(ts_filter(setweight(t.v, 'B', @words), '{b}')) u) w
            where p."Id" = any(@ids)
            """,
            [("words", words.ToArray()), ("ids", candidates.ToArray())],
            r => (Id: r.GetGuid(0), Title: r.GetString(1), Length: r.GetInt32(2), TitleLength: r.GetInt32(3),
                  All: Counts(r, 4), InTitle: Counts(r, 6)),
            ct);

        return features
            .Select(f =>
            {
                var terms = words.Select((w, i) =>
                {
                    var all = f.All.GetValueOrDefault(w);
                    var inTitle = f.InTitle.GetValueOrDefault(w);
                    return new Bm25.Term(frequency[i], inTitle, Math.Max(all - inTitle, 0));
                });
                return (f.Id, f.Title, Score: Bm25.Score(corpus, f.TitleLength, f.Length, terms));
            })
            // Equal scores in title order, so the same search lists the same way.
            .OrderByDescending(f => f.Score)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Id)
            .Select(f => (f.Id, (double?)f.Score))
            .ToList();
    }

    private static Dictionary<string, int> Counts(DbDataReader r, int at)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (r.IsDBNull(at) || r.IsDBNull(at + 1)) return counts;
        var words = r.GetFieldValue<string[]>(at);
        var n = r.GetFieldValue<int[]>(at + 1);
        for (var i = 0; i < words.Length && i < n.Length; i++) counts[words[i]] = n[i];
        return counts;
    }

    /// <summary>
    /// A hand-written query, for what full-text search has no EF binding for
    /// (<c>ts_filter</c>, <c>setweight</c>, <c>unnest</c> of a tsvector).
    /// Values always go as parameters.
    /// </summary>
    internal static async Task<List<T>> QueryAsync<T>(
        AppDbContext db, string sql, IEnumerable<(string Name, object Value)> parameters,
        Func<DbDataReader, T> read, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(read(reader));
            return rows;
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }
}
