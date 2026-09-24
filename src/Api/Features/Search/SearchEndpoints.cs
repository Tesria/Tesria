using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Search;

public static class SearchEndpoints
{
    private const int MaxResults = 50;

    public record SearchResult(Guid PageId, Guid SpaceId, string SpaceKey, string Title, string Snippet);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        // Anonymous callers search public spaces only (dev-plan 5.2).
        routes.MapGet("/search", SearchAsync).WithTags("Search").AllowAnonymous();
        return routes;
    }

    private sealed record Hit(Guid Id, Guid SpaceId, string SpaceKey, string Title);

    private static async Task<IResult> SearchAsync(
        string? q, Guid? spaceId, AppDbContext db, IPermissionService perms)
    {
        var term = (q ?? "").Trim();
        if (term.Length == 0) return Results.Ok(Array.Empty<SearchResult>());

        // The soft-delete query filter already excludes trashed pages.
        IQueryable<Page> query = db.Pages.AsNoTracking();
        if (spaceId is { } sid) query = query.Where(p => p.SpaceId == sid);

        // Never return hits from spaces the caller cannot view.
        var viewableSpaces = await perms.ViewableSpaceIdsAsync();
        query = query.Where(p => viewableSpaces.Contains(p.SpaceId));

        if (db.Database.IsNpgsql())
        {
            // Real full-text search: match the GIN-indexed tsvector, rank by
            // relevance. WebSearchToTsQuery accepts user-friendly query syntax.
            // The EF.Functions call must be inlined in each lambda so it is
            // translated to SQL rather than evaluated on the client.
            query = query
                .Where(p => p.SearchVector!.Matches(EF.Functions.WebSearchToTsQuery("english", term)))
                .OrderByDescending(p => p.SearchVector!.Rank(EF.Functions.WebSearchToTsQuery("english", term)));
        }
        else
        {
            // Portable fallback (SQLite tests): case-insensitive substring match.
            var like = $"%{term}%";
            query = query
                .Where(p => EF.Functions.Like(p.SearchText, like))
                .OrderBy(p => p.Title);
        }

        // Space access is not enough: pages hidden by page restrictions are
        // dropped too. That happens after the query, so the query reads on in
        // batches until it has MaxResults readable hits or runs out; taking 50
        // and then filtering returned fewer than 50 when more existed (found
        // 2026-09-23). The batch cap bounds the work a query can cause.
        const int batch = 100, maxBatches = 10;
        var projected = query.Select(p => new Hit(p.Id, p.SpaceId, p.Space!.Key, p.Title));
        var visible = new List<Hit>();
        var allowed = new List<Guid>();
        for (var i = 0; i < maxBatches && allowed.Count < MaxResults; i++)
        {
            var rows = await projected.Skip(i * batch).Take(batch).ToListAsync();
            foreach (var r in rows)
            {
                if (allowed.Count >= MaxResults) break;
                if (!await perms.CanViewPageAsync(r.Id)) continue;
                allowed.Add(r.Id);
                visible.Add(r);
            }
            if (rows.Count < batch) break;
        }

        // The passage that matched, not the page's opening line: computed
        // once for the survivors (SearchSnippets).
        var matches = await SearchSnippets.ForAsync(db, allowed, term, default);
        var results = visible
            .Where(r => allowed.Contains(r.Id))
            .Select(r => new SearchResult(r.Id, r.SpaceId, r.SpaceKey, r.Title,
                matches.TryGetValue(r.Id, out var m) ? m.Snippet : ""))
            .ToList();
        return Results.Ok(results);
    }

}
