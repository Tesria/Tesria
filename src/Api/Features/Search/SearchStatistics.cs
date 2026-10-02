using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;

namespace Tesria.Api.Features.Search;

/// <summary>
/// The average title and text length BM25 measures each page against
/// (<see cref="Bm25.Corpus"/>), kept for a few minutes rather than computed
/// for every search (dev-plan 23.1).
///
/// <para>The rest of BM25's statistics are counted exactly for every search,
/// because the full-text index answers them cheaply: how many pages the
/// searcher can see, and how many of those hold each query word. The averages
/// are different: they read every page's index entry (about 2 ms for the 261
/// pages of the seeded instance, 30 ms for 10,000 on a laptop), and they
/// barely move between one search and the next, so a few minutes' old average
/// orders results the same. Nothing is stored, so there is no table to keep
/// current as pages are published, moved, restored or deleted. PostgreSQL's
/// <c>ts_stat</c> would give the word counts too, but by reading every page
/// for every search: 5 ms for DOCS, 120 ms at 10,000 pages.</para>
///
/// <para>Over every published page, not only the ones a searcher may read:
/// an average length says nothing about any one page, and one shared value is
/// what lets it be kept.</para>
/// </summary>
public sealed class SearchStatistics(TimeProvider clock)
{
    /// <summary>How long averages are reused before they are counted again.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public readonly record struct Averages(double TitleLength, double BodyLength);

    private sealed record Cached(Averages Value, DateTimeOffset At);

    private volatile Cached? _cached;

    public async Task<Averages> AveragesAsync(AppDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_cached is { } hit && now - hit.At < Lifetime) return hit.Value;

        // Drafts and the trash left out, as the query filter on Pages does
        // for search (AppDbContext). Written by hand because ts_filter has no
        // translation inside an aggregate.
        var rows = await PageSearch.QueryAsync(db, """
            select coalesce(avg(length(ts_filter("SearchVector", '{a}'))), 0)::float8,
                   coalesce(avg(length("SearchVector")), 0)::float8
            from "Pages"
            where "DeletedAt" is null and "Status" <> @draft
            """, [("draft", (int)PageStatus.Draft)], r => new Averages(r.GetDouble(0), r.GetDouble(1)), ct);
        var value = rows.Count == 0 ? new Averages(0, 0) : rows[0];
        _cached = new Cached(value, now);
        return value;
    }
}
