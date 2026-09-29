using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The colors a person's next new code block starts with (2026-09-29): kept
/// on the account, returned with it, and only ever one of the editor's
/// schemes.
/// </summary>
public class EditorPreferenceTests
{
    private record Me(string? CodeBlockScheme);

    private static async Task<HttpClient> SignedInAsync(TestAppFactory factory)
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "writer@example.com", DisplayName = "Writer", Password = "supersecret" })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task A_chosen_scheme_is_remembered_and_default_clears_it()
    {
        using var factory = new TestAppFactory();
        var client = await SignedInAsync(factory);
        Assert.Null((await client.GetFromJsonAsync<Me>("/api/auth/me"))!.CodeBlockScheme);

        var set = await client.PutAsJsonAsync("/api/auth/me/editor", new { CodeBlockScheme = "dracula" });
        set.EnsureSuccessStatusCode();
        Assert.Equal("dracula", (await set.Content.ReadFromJsonAsync<Me>())!.CodeBlockScheme);
        Assert.Equal("dracula", (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.CodeBlockScheme);

        (await client.PutAsJsonAsync("/api/auth/me/editor", new { CodeBlockScheme = "default" })).EnsureSuccessStatusCode();
        Assert.Null((await client.GetFromJsonAsync<Me>("/api/auth/me"))!.CodeBlockScheme);
    }

    [Fact]
    public async Task An_unknown_scheme_is_refused_and_changes_nothing()
    {
        using var factory = new TestAppFactory();
        var client = await SignedInAsync(factory);
        (await client.PutAsJsonAsync("/api/auth/me/editor", new { CodeBlockScheme = "nord" })).EnsureSuccessStatusCode();

        var refused = await client.PutAsJsonAsync("/api/auth/me/editor", new { CodeBlockScheme = "<script>" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("nord", (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.CodeBlockScheme);
    }

    [Fact]
    public async Task Signed_out_it_is_refused()
    {
        using var factory = new TestAppFactory();
        var response = await factory.CreateClient().PutAsJsonAsync("/api/auth/me/editor", new { CodeBlockScheme = "nord" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
