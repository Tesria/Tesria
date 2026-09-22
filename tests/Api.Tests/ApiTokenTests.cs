using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

public class ApiTokenTests
{
    private record CreatedToken(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, string Token);
    private record TokenRow(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

    [Fact]
    public async Task Bearer_token_authenticates_without_a_cookie()
    {
        using var factory = new TestAppFactory();
        var cookieClient = factory.CreateClient();
        await cookieClient.RegisterAndSignInAsync();

        var created = await (await cookieClient.PostAsJsonAsync("/api/api-tokens", new { Name = "CI" }))
            .Content.ReadFromJsonAsync<CreatedToken>();
        Assert.StartsWith("cct_", created!.Token);

        // A brand-new client with no cookie jar at all, authenticated only by
        // the bearer token, must reach the same authenticated identity.
        var bearerClient = factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        var me = await bearerClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Listing_tokens_never_exposes_the_raw_secret()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var created = await (await client.PostAsJsonAsync("/api/api-tokens", new { Name = "Laptop" }))
            .Content.ReadFromJsonAsync<CreatedToken>();

        var body = await client.GetStringAsync("/api/api-tokens");
        var rows = await client.GetFromJsonAsync<List<TokenRow>>("/api/api-tokens");
        Assert.Single(rows!);
        Assert.Equal("Laptop", rows![0].Name);
        // The short identifying prefix is fine to show; the full secret must
        // never reappear once it's been issued.
        Assert.DoesNotContain(created!.Token, body);
    }

    [Fact]
    public async Task Revoked_token_stops_authenticating()
    {
        using var factory = new TestAppFactory();
        var cookieClient = factory.CreateClient();
        await cookieClient.RegisterAndSignInAsync();
        var created = await (await cookieClient.PostAsJsonAsync("/api/api-tokens", new { Name = "Old laptop" }))
            .Content.ReadFromJsonAsync<CreatedToken>();

        await cookieClient.DeleteAsync($"/api/api-tokens/{created!.Id}");

        var bearerClient = factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearerClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Garbage_or_forged_tokens_are_rejected()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cct_not-a-real-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Bearer_auth_respects_the_same_permission_model_as_cookies()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();

        var created = await (await alice.PostAsJsonAsync("/api/api-tokens", new { Name = "Script" }))
            .Content.ReadFromJsonAsync<CreatedToken>();
        var script = factory.CreateClient();
        script.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Token);

        // The token belongs to Alice, so it can create pages in her space:
        // proving CurrentUser/permission checks work identically for token auth.
        var res = await script.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Via token", ContentJson = (string?)null });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }
}
