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
            options.AddDocumentTransformer((document, context, _) =>
            {
                // The release this document describes (dev-plan 16.1), to
                // signed-in readers only (dev-plan 14.3); anyone else sees the
                // API's own version, which has not changed since there was one.
                var signedIn = context.ApplicationServices.GetService<IHttpContextAccessor>()?
                    .HttpContext?.User.Identity?.IsAuthenticated == true;
                document.Info = new OpenApiInfo
                {
                    Title = "Tesria API",
                    Version = signedIn ? Infrastructure.Versioning.AppVersion.Current : "1",
                    Description =
                        "The REST API behind Tesria, a self-hosted wiki.\n\n"
                        + "**Authentication.** Scripts and integrations send an API token as "
                        + "`Authorization: Bearer <token>` (create one under *Profile, API Tokens*). "
                        + "A token that is mistyped, revoked or expired is refused with `401`. "
                        + "The SPA uses a session cookie instead, and cookie-authenticated "
                        + "requests that change anything must also send `X-Requested-With: Tesria`: "
                        + "that header is the CSRF defense, and a browser cannot set it "
                        + "cross-origin.\n\n"
                        + "**Permissions.** Anything you may not see is `404`, so restricted "
                        + "pages are not discoverable by probing. Something you can see but may "
                        + "not change is `403`, and so is any change made with a read-only token "
                        + "(`read_only_token`) or one that needs a password in the browser "
                        + "(`reauth_required`).\n\n"
                        + "**Errors.** A refusal has a JSON body: `errors` names each field that "
                        + "is wrong, and `code` and `message` say why, where there is more to say.",
                    License = new OpenApiLicense { Name = "Apache-2.0", Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0") },
                };

                document.Components ??= new OpenApiComponents();
                // Both collections start null on a fresh document.
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[ApiTokenScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "An API token from *Profile, API Tokens*. Shown once, when it is created.",
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
                AddRefusals(operation, context, guarded && !anonymous);
                return Task.CompletedTask;
            });
        });

    private const string ErrorSchema = "Error";

    /// <summary>
    /// The refusals every operation of its kind can answer with, and what
    /// their bodies hold (T5-021: every operation was documented as "200 OK"
    /// and nothing else). Success answers, and refusals particular to one
    /// request such as a 409, are declared where the endpoint is mapped
    /// (<c>.Produces</c>); these are the ones that follow from what the
    /// request is: it has a body, it names something by id, it needs a
    /// token.
    /// </summary>
    private static void AddRefusals(OpenApiOperation operation, OpenApiOperationTransformerContext context, bool guarded)
    {
        var document = context.Document;
        if (document is null) return;
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        if (!document.Components.Schemas.ContainsKey(ErrorSchema))
            document.Components.Schemas[ErrorSchema] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Why a request was refused. Which fields are present depends on the refusal.",
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    ["code"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "A reason a script can check, such as `read_only_token` or `reauth_required`.",
                    },
                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "The reason, in words." },
                    ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "The reason, in words." },
                    ["errors"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Description = "For each field that is wrong, what is wrong with it.",
                        AdditionalProperties = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Array,
                            Items = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    },
                },
            };

        operation.Responses ??= new OpenApiResponses();
        void Add(int status, string description)
        {
            var key = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (operation.Responses.ContainsKey(key)) return;
            operation.Responses[key] = new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(ErrorSchema, document) },
                },
            };
        }

        if (operation.RequestBody is not null)
        {
            Add(400, "Something in the request is wrong: `errors` names the field and why.");
            // T5-024, T5-025: neither was answered or documented before.
            Add(413, "Too large: an attachment can be up to 25 MB, a pack 500 MB, and any other request 100 MB. "
                + "`detail` says which limit.");
            if (operation.RequestBody.Content?.ContainsKey("application/json") == true)
                Add(415, "The body is not marked as JSON: send it with the header `Content-Type: application/json`.");
        }
        if (guarded)
        {
            Add(401, "Not signed in: the token is missing, mistyped, revoked or expired.");
            Add(403, "Not allowed: you can see it but may not change it, the token is read-only "
                + "(`read_only_token`), or the action needs a password in the browser (`reauth_required`).");
        }
        else
        {
            // Open to anyone, and so to the limit on callers with no token or
            // session, which an administrator sets (AnonymousRateLimitPerMinute).
            Add(429, "Too many requests from this address without a token or session: "
                + "wait the number of seconds in `Retry-After`.");
        }
        if (context.Description.ParameterDescriptions.Any(p => p.Source == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Path))
            Add(404, "Not found, or you may not see it: the two are the same answer.");
    }

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
