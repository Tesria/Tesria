using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;

namespace Tesria.Api.Features.Search;

public static class SearchEndpoints
{
    private const int MaxResults = 50;

    public record SearchResult(Guid PageId, Guid SpaceId, string SpaceKey, string Title, string Snippet);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        // Anonymous callers search public spaces only (dev-plan 5.2).
        routes.MapGet("/search", SearchAsync).WithTags("Search").AllowAnonymous().Produces<List<SearchResult>>();
        return routes;
    }

    private static async Task<IResult> SearchAsync(
        string? q, Guid? spaceId, AppDbContext db, IPermissionService perms, SearchStatistics statistics,
        CancellationToken ct)
    {
        // Ranked by BM25, filtered by permission, with the matching passage:
        // the same as the MCP tool (PageSearch).
        var hits = await PageSearch.RunAsync(db, perms, statistics, q ?? "", spaceId, MaxResults, ct);
        return Results.Ok(hits.Select(h => new SearchResult(h.Id, h.SpaceId, h.SpaceKey, h.Title, h.Snippet)).ToList());
    }
}
