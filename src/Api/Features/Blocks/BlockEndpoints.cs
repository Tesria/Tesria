using Tesria.Api.Infrastructure.Auth;

namespace Tesria.Api.Features.Blocks;

/// <summary>
/// <c>GET /api/pages/{hostId}/blocks/{kind}?param=value…</c>: the one
/// endpoint behind every dynamic block (architecture.md, "Dynamic blocks",
/// decision 3). Anonymous-capable: a public page's blocks are part of the
/// page, and the anonymous principal falls out of the permission service.
/// </summary>
public static class BlockEndpoints
{
    public static IEndpointRouteBuilder MapBlockEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/pages/{hostId:guid}/blocks/{kind}", Render)
            .WithTags("Blocks")
            .AllowAnonymous();
        return routes;
    }

    private static async Task<IResult> Render(
        Guid hostId, string kind, HttpContext http, IDynamicBlockService blocks, CurrentUser current, CancellationToken ct)
    {
        // Unknown kind is a client error, not a missing page, but say so
        // before touching the host, so the answer does not depend on whether
        // the host exists (which would be a probe).
        if (!blocks.Kinds.Contains(kind))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = [$"Unknown block kind '{kind}'."] });

        var parameters = http.Request.Query
            .Where(q => q.Key != "kind")
            .ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);

        BlockResult? result;
        try
        {
            result = await blocks.RenderAsync(hostId, kind, parameters, ct);
        }
        catch (BlockParamException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.Param] = [ex.Message] });
        }
        // Same masking as a page read: not viewable and not found are one answer.
        if (result is null) return Results.NotFound();

        http.Response.Headers.CacheControl = current.Id is null ? "public, max-age=60" : "private, no-store";
        return Results.Ok(result);
    }
}
