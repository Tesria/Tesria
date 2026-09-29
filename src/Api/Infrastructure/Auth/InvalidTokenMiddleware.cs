using Microsoft.AspNetCore.Authentication;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Answers 401 when a request to the REST API carries a token that is not
/// good: revoked, expired, mistyped, or its owner may no longer use tokens
/// (T5-018).
///
/// Authentication only reports such a token as a failure; it does not
/// refuse the request. On an endpoint that anonymous readers may call (the
/// space list, search, a public page) the request then went on as nobody,
/// and a script with a revoked token was told 200 with an empty list, which
/// reads as "there is nothing there" rather than "your token is no good". A
/// request that sends credentials wants to be that person or to be told it
/// is not; it never wants to be quietly anonymous.
///
/// Only under <c>/api</c>: <c>/mcp</c> accepts tokens alone and already
/// refuses a bad one, and the pages of the app itself are not read with
/// tokens. A header that is not a bearer token is not a token, and is left
/// alone as it always was.
/// </summary>
public sealed class InvalidTokenMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && context.User.Identity?.IsAuthenticated != true
            && context.Request.Headers.ContainsKey("Authorization"))
        {
            // The handler's answer for this request is cached, so asking
            // again costs nothing and runs no second lookup.
            var result = await context.AuthenticateAsync();
            if (result.Failure is not null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Unauthorized",
                    status = StatusCodes.Status401Unauthorized,
                    code = "invalid_token",
                    // The handler's own reason: a bad token, or an owner
                    // whose role no longer allows tokens.
                    message = result.Failure.Message,
                });
                return;
            }
        }
        await next(context);
    }
}
