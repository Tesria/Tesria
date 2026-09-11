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

        var rows = await query
            .Take(MaxResults)
            .Select(p => new { p.Id, p.SpaceId, SpaceKey = p.Space!.Key, p.Title })
            .ToListAsync();

        // Space access is not enough — drop pages hidden by page restrictions.
        var visible = rows.ToList();
        var allowed = new List<Guid>();
        foreach (var r in visible)
            if (await perms.CanViewPageAsync(r.Id)) allowed.Add(r.Id);

        // The passage that matched, not the page's opening line — computed
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
