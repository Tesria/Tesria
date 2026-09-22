namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Cross-site request forgery guard for cookie sessions (dev-plan 3.4).
///
/// The API only speaks JSON, which a cross-site HTML form cannot send, and
/// the session cookie is SameSite=Lax, which a cross-site POST does not
/// carry. This is the third, explicit layer: every state-changing request
/// authenticated by the cookie must carry <c>X-Requested-With: Tesria</c>.
/// A browser will not add a custom header to a cross-origin request without
/// a CORS preflight, and no cross-origin caller passes ours, so the header
/// can only have come from our own page. Chosen over a double-submit token
/// because it needs no token plumbing and holds even for the multipart
/// upload endpoints, which a form <em>could</em> otherwise target.
///
/// Bearer-token callers have no cookie and therefore no CSRF exposure; they
/// are exempt. So is anything outside /api, which the auth handlers own.
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "Tesria";

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresHeader(context) && !HasHeader(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                $$"""{"title":"Forbidden","status":403,"detail":"State-changing requests from a browser session must send {{HeaderName}}: {{HeaderValue}}."}""");
            return;
        }
        await next(context);
    }

    private static bool RequiresHeader(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return false;
        if (!context.Request.Path.StartsWithSegments("/api")) return false;
        // Only a cookie session is forgeable; a Bearer caller sent its credential on purpose.
        if (context.Request.Headers.ContainsKey("Authorization")) return false;
        return context.User.Identity?.IsAuthenticated == true;
    }

    private static bool HasHeader(HttpContext context) =>
        context.Request.Headers.TryGetValue(HeaderName, out var value)
        && string.Equals(value, HeaderValue, StringComparison.Ordinal);
}
