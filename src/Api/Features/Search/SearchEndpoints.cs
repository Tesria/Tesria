using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Search;

public static class SearchEndpoints
{
    private const int MaxResults = 50;
    private const int SnippetLength = 200;

    public record SearchResult(Guid PageId, Guid SpaceId, string SpaceKey, string Title, string Snippet);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/search", SearchAsync).WithTags("Search").RequireAuthorization();
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
            .Select(p => new { p.Id, p.SpaceId, SpaceKey = p.Space!.Key, p.Title, p.SearchText })
            .ToListAsync();

        // Space access is not enough — drop pages hidden by page restrictions.
        var results = new List<SearchResult>();
        foreach (var r in rows)
        {
            if (!await perms.CanViewPageAsync(r.Id)) continue;
            results.Add(new SearchResult(r.Id, r.SpaceId, r.SpaceKey, r.Title, Snippet(r.SearchText)));
        }
        return Results.Ok(results);
    }

    private static string Snippet(string text) =>
        text.Length <= SnippetLength ? text : text[..SnippetLength].TrimEnd() + "…";
}
