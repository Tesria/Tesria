using System.Net;
using System.Text.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The machine-readable spec (dev-plan 8.3). These assert it describes the
/// API this app actually has: a spec generated from the routes cannot
/// invent an endpoint, but it can silently stop being served.
/// </summary>
public class OpenApiTests
{
    private static async Task<JsonElement> SpecAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/openapi.json");
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task The_spec_is_served_without_an_account()
    {
        using var factory = new TestAppFactory();
        // The shape of an API is not a secret, and every endpoint still
        // enforces its own permissions.
        var spec = await SpecAsync(factory.CreateClient());

        Assert.Equal("Tesria API", spec.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("Apache-2.0", spec.GetProperty("info").GetProperty("license").GetProperty("name").GetString());
    }

    [Fact]
    public async Task It_describes_the_endpoints_that_exist()
    {
        using var factory = new TestAppFactory();
        var paths = (await SpecAsync(factory.CreateClient())).GetProperty("paths");

        foreach (var path in new[]
        {
            "/api/pages/{id}", "/api/spaces", "/api/search", "/api/pages/{id}/export",
            "/api/pages/{hostId}/blocks/{kind}", "/api/embeds/resolve", "/api/notifications",
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"the spec should describe {path}");
        }
    }

    [Fact]
    public async Task Both_ways_of_authenticating_are_described()
    {
        using var factory = new TestAppFactory();
        var schemes = (await SpecAsync(factory.CreateClient()))
            .GetProperty("components").GetProperty("securitySchemes");

        Assert.Equal("bearer", schemes.GetProperty("ApiToken").GetProperty("scheme").GetString());
        Assert.Equal("cookie", schemes.GetProperty("SessionCookie").GetProperty("in").GetString());
    }

    [Fact]
    public async Task An_endpoint_anyone_may_call_is_not_described_as_needing_a_token()
    {
        using var factory = new TestAppFactory();
        var paths = (await SpecAsync(factory.CreateClient())).GetProperty("paths");

        // Two ways of being open, and the spec must reflect both: health
        // never asks for authorization at all...
        var health = paths.GetProperty("/api/health").GetProperty("get");
        Assert.True(health.TryGetProperty("security", out var security) && security.GetArrayLength() == 0);

        // ...and the page tree is deliberately opened to anonymous readers
        // (dev-plan 5.2).
        var tree = paths.GetProperty("/api/pages/tree").GetProperty("get");
        Assert.True(tree.TryGetProperty("security", out var treeSecurity) && treeSecurity.GetArrayLength() == 0);

        // A protected one keeps the document-level requirement.
        var notifications = paths.GetProperty("/api/notifications").GetProperty("get");
        Assert.False(notifications.TryGetProperty("security", out var s2) && s2.GetArrayLength() == 0);
    }

    [Fact]
    public async Task Each_operation_says_what_it_answers_not_only_200()
    {
        // T5-021: every operation was documented as "200 OK" with no body.
        using var factory = new TestAppFactory();
        var spec = await SpecAsync(factory.CreateClient());
        var paths = spec.GetProperty("paths");

        static JsonElement Responses(JsonElement paths, string path, string verb) =>
            paths.GetProperty(path).GetProperty(verb).GetProperty("responses");
        static string? BodyRef(JsonElement response) =>
            response.GetProperty("content").GetProperty("application/json").GetProperty("schema")
                .TryGetProperty("$ref", out var r) ? r.GetString() : null;

        var create = Responses(paths, "/api/pages", "post");
        Assert.False(create.TryGetProperty("200", out _));
        Assert.EndsWith("PageDetailResponse", BodyRef(create.GetProperty("201")));
        Assert.True(create.TryGetProperty("400", out _));
        Assert.True(create.TryGetProperty("401", out _));

        var update = Responses(paths, "/api/pages/{id}", "put");
        Assert.EndsWith("PageDetailResponse", BodyRef(update.GetProperty("200")));
        Assert.EndsWith("PageDetailResponse", BodyRef(update.GetProperty("409")));
        Assert.True(update.TryGetProperty("404", out _));

        Assert.True(Responses(paths, "/api/pages/{id}", "delete").TryGetProperty("204", out _));
        Assert.True(Responses(paths, "/api/api-tokens", "post").TryGetProperty("201", out _));
        Assert.True(Responses(paths, "/api/pages/{pageId}/labels/{name}", "delete").TryGetProperty("204", out _));
        Assert.True(Responses(paths, "/api/pages/{id}/layout", "put").TryGetProperty("204", out _));
        Assert.True(Responses(paths, "/api/pages/{id}/publish", "post").TryGetProperty("409", out _));

        // Refusals carry a described body, and every protected operation can say 401.
        Assert.True(spec.GetProperty("components").GetProperty("schemas").TryGetProperty("Error", out _));
        foreach (var path in paths.EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
            {
                var responses = op.Value.GetProperty("responses");
                Assert.True(responses.EnumerateObject().Any(r => r.Name != "200"),
                    $"{op.Name.ToUpperInvariant()} {path.Name} documents nothing but 200");
                var open = op.Value.TryGetProperty("security", out var s) && s.GetArrayLength() == 0;
                if (!open)
                    Assert.True(responses.TryGetProperty("401", out _), $"{op.Name.ToUpperInvariant()} {path.Name} has no 401");
            }
    }

    [Fact]
    public async Task Optional_fields_are_not_marked_required()
    {
        using var factory = new TestAppFactory();
        var schemas = (await SpecAsync(factory.CreateClient())).GetProperty("components").GetProperty("schemas");

        // "Leave out contentJson to rename": none of an update's fields is required.
        var update = schemas.GetProperty("UpdatePageRequest");
        Assert.False(update.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);

        var create = schemas.GetProperty("CreatePageRequest").GetProperty("required");
        Assert.Equal(["spaceId", "title"], create.EnumerateArray().Select(r => r.GetString()).Order());
    }

    [Fact]
    public async Task The_introduction_does_not_promise_404_for_everything()
    {
        using var factory = new TestAppFactory();
        var info = (await SpecAsync(factory.CreateClient())).GetProperty("info").GetProperty("description").GetString()!;
        Assert.DoesNotContain("never `403`", info);
        Assert.DoesNotContain("mint", info, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("` :", info);
    }

    [Fact]
    public async Task The_reader_is_served_at_api_docs()
    {
        using var factory = new TestAppFactory();
        var res = await factory.CreateClient().GetAsync("/api/docs");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("text/html", res.Content.Headers.ContentType?.MediaType);
    }
}

/// <summary>
/// The reference UI is a third-party page inside an app with a strict CSP.
/// These pin the two things that make that safe.
/// </summary>
public class ApiReferenceCspTests
{
    [Fact]
    public async Task The_docs_page_gets_a_nonce_and_no_other_page_does()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var docs = await client.GetAsync("/api/docs");
        var docsCsp = docs.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("'nonce-", docsCsp);
        // Never `unsafe-inline`: the whole point of the nonce is not to open
        // inline scripts for the rest of the app.
        Assert.DoesNotContain("unsafe-inline", docsCsp.Split("; ").First(d => d.StartsWith("script-src")));

        var elsewhere = (await client.GetAsync("/api/health"))
            .Headers.GetValues("Content-Security-Policy").Single();
        Assert.DoesNotContain("'nonce-", elsewhere);
    }

    [Fact]
    public async Task Each_request_to_the_docs_page_gets_a_fresh_nonce()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        static string NonceOf(HttpResponseMessage r) =>
            r.Headers.GetValues("Content-Security-Policy").Single();

        // A reused nonce is no better than unsafe-inline.
        Assert.NotEqual(NonceOf(await client.GetAsync("/api/docs")), NonceOf(await client.GetAsync("/api/docs")));
    }
}
