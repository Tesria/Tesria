using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing.Template;

namespace Tesria.Api.Infrastructure;

/// <summary>
/// Every refusal from <c>/api</c> says something a person can read, even
/// the ones no endpoint wrote (2026-10-02, the "messages people see first"
/// batch).
///
/// <para>Three kinds arrived with an empty body. An exception nobody caught
/// was a bare 500, which the SPA showed as "Request failed (500)." (T1-023,
/// t2-026, t6-014): two requests colliding in the database now answer 409,
/// and anything else a 500 that says so, with the details in the log as
/// before. A body over the size limit was a bare 413, and a JSON endpoint
/// sent something that is not marked as JSON a bare 415 (T5-024, T5-025);
/// both now say what to change.</para>
/// </summary>
public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> log)
{
    public const string ConflictMessage =
        "Someone else changed this at the same moment, so it was not saved. Try again.";
    public const string FaultMessage =
        "Something went wrong in Tesria, so that was not done. Try again; if it keeps happening, the server log has the details.";
    public const string NotJsonMessage =
        "Tesria could not read the body: send it as JSON, with the header Content-Type: application/json.";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            // Kestrel refusing the body while a handler read it (a form
            // upload past the limit, say). Its own message names a header or
            // a byte count, so ours is written instead.
            await WriteAsync(context, ex.StatusCode, ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? TooLarge(context)
                : "Tesria could not read that request.");
            return;
        }
        catch (Exception ex) when (DbConflicts.IsConflict(ex) && !context.Response.HasStarted)
        {
            log.LogWarning(ex, "Two requests collided in the database: {Method} {Path} answered 409",
                context.Request.Method, context.Request.Path);
            context.Response.Clear();
            await WriteAsync(context, StatusCodes.Status409Conflict, ConflictMessage);
            return;
        }
        catch (Exception ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            log.LogError(ex, "Unhandled exception: {Method} {Path} answered 500",
                context.Request.Method, context.Request.Path);
            context.Response.Clear();
            await WriteAsync(context, StatusCodes.Status500InternalServerError, FaultMessage);
            return;
        }

        // The framework's own refusals, which it sends with nothing in them.
        var response = context.Response;
        if (response.HasStarted || response.ContentLength is > 0 || response.ContentType is not null) return;
        if (response.StatusCode == StatusCodes.Status413PayloadTooLarge)
            await WriteAsync(context, response.StatusCode, TooLarge(context));
        else if (response.StatusCode == StatusCodes.Status415UnsupportedMediaType)
            await WriteAsync(context, response.StatusCode, NotJsonMessage);
    }

    /// <summary>The limit this request was held to, in words (T5-024).</summary>
    public static string TooLarge(HttpContext context)
    {
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
        return limit is { } bytes
            ? $"This request is larger than Tesria accepts here: {Megabytes(bytes)} at most."
            : "This request is larger than Tesria accepts here.";
    }

    private static string Megabytes(long bytes) => $"{Math.Round(bytes / (1024.0 * 1024.0))} MB";

    private static Task WriteAsync(HttpContext context, int status, string detail) =>
        Results.Problem(detail, statusCode: status).ExecuteAsync(context);

    /// <summary>
    /// A 415 for a body sent to a JSON endpoint without saying it is JSON
    /// (T5-025), or null. Routing does answer 415 itself, but only when no
    /// other endpoint matches, and the SPA fallback matches every path: so
    /// the request landed there and answered 404, which reads as "wrong
    /// address" for what is the classic forgotten curl header. The fallback
    /// asks this before saying 404.
    /// </summary>
    public static IResult? NotJson(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || request.HasJsonContentType())
            return null;

        var endpoints = context.RequestServices.GetRequiredService<EndpointDataSource>().Endpoints;
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            if (accepts is null || !accepts.ContentTypes.Any(t => t.Contains("json", StringComparison.OrdinalIgnoreCase)))
                continue;
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
            if (methods is not null && !methods.HttpMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
                continue;
            var matcher = new TemplateMatcher(new RouteTemplate(endpoint.RoutePattern), new RouteValueDictionary());
            if (matcher.TryMatch(request.Path, new RouteValueDictionary()))
                return Results.Problem(NotJsonMessage, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }
        return null;
    }
}
