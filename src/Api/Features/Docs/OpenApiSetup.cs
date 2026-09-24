using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Tesria.Api.Features.Docs;

/// <summary>
/// The machine-readable API description (dev-plan 8.3).
///
/// The API space documents this API in prose for people; this is the same
/// surface for tools: an HTTP client, a generated SDK, and (8.4) an MCP
/// server, which is much easier to define from a spec than from reading
/// endpoints. Generated from the routes themselves, so it cannot describe
/// an endpoint that does not exist.
/// </summary>
public static class OpenApiSetup
{
    public const string ApiTokenScheme = "ApiToken";
    public const string SessionScheme = "SessionCookie";

    public static IServiceCollection AddTesriaOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Tesria API",
                    // The release this document describes (dev-plan 16.1).
                    Version = Infrastructure.Versioning.AppVersion.Current,
                    Description =
                        "The REST API behind Tesria, a self-hosted wiki.\n\n"
                        + "**Authentication.** Scripts and integrations send an API token as "
                        + "`Authorization: Bearer <token>` (mint one at *Profile → API tokens*). "
                        + "The SPA uses a session cookie instead, and cookie-authenticated "
                        + "requests that change anything must also send `X-Requested-With: Tesria` "
                        + ": that header is the CSRF defense, and a browser cannot set it "
                        + "cross-origin.\n\n"
                        + "**Permissions.** Anything you may not see is `404`, never `403`, so "
                        + "restricted pages are not discoverable by probing.",
                    License = new OpenApiLicense { Name = "Apache-2.0", Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0") },
                };

                document.Components ??= new OpenApiComponents();
                // Both collections start null on a fresh document.
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[ApiTokenScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "An API token from *Profile → API tokens*. Shown once when minted.",
                };
                document.Components.SecuritySchemes[SessionScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = "tesria.auth",
                    Description = "The SPA's session cookie. Unsafe requests also need `X-Requested-With: Tesria`.",
                };

                // Either scheme satisfies any endpoint; the ones that need
                // neither are marked below.
                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(ApiTokenScheme, document)] = [],
                });
                return Task.CompletedTask;
            });

            // Endpoints anyone may call say so, rather than inheriting the
            // document-level requirement and sending a reader to mint a token
            // they do not need.
            //
            // Two ways an endpoint is open, and both count: an explicit
            // `.AllowAnonymous()` (a route deliberately opened to the world,
            // dev-plan 5.2), and simply never having asked for authorization:
            //health does that, and describing it as needing a token would
            // be a lie the generator cannot catch.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var anonymous = metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
                var guarded = metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();
                if (anonymous || !guarded) operation.Security = [];
                return Task.CompletedTask;
            });
        });

    /// <summary>
    /// Serves the spec at <c>/api/openapi.json</c> and a reader at
    /// <c>/api/docs</c>. Both are open: the shape of an API is not a secret,
    /// every endpoint still enforces its own permissions, and an operator who
    /// disagrees can block the two paths at the proxy.
    /// </summary>
    public static WebApplication MapTesriaApiDocs(this WebApplication app)
    {
        app.MapOpenApi("/api/openapi.json").AllowAnonymous();
        app.MapScalarApiReference("/api/docs", (options, http) =>
        {
            options.Title = "Tesria API";
            options.OpenApiRoutePattern = "/api/openapi.json";
            // Scalar's own JS is served from this origin already. Its default
            // web fonts are not, and this app does not fetch from anyone
            // else, so they are turned off rather than silently blocked by
            // `font-src 'self' data:`.
            options.WithDefaultFonts(false);
            // Scalar's sidebar offers features backed by api.scalar.com. This
            // app's `connect-src 'self'` blocks those calls, which is the
            // behavior we want, a documentation page has no business
            // phoning anywhere, so the button that leads to them is hidden
            // rather than left to fail in front of the reader. Scalar 2.17
            // added switches for the lookups themselves (its agent, MCP and
            // telemetry), which were still trying and failing against the CSP
            // on every load (seen 2026-09-23); the CSP stays the backstop.
            options.WithClientButton(false);
            options.DisableAgent();
            options.DisableMcp();
            options.DisableTelemetry();
            // Its developer toolbar (Configure, Share, Deploy) leads to
            // Scalar's hosted service, and it shows on localhost addresses.
            options.HideDeveloperTools();
            // The page's one inline script runs under the nonce the security
            // headers minted for this request (SecurityHeadersMiddleware),
            // instead of the app opening `unsafe-inline` for everyone.
            if (http.Items[Infrastructure.Security.SecurityHeadersMiddleware.CspNonceKey] is string nonce)
                options.WithNonce(nonce);
        });
        return app;
    }
}
