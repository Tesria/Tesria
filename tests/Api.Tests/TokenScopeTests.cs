using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Read-only API tokens (dev-plan 8.4): minted, listed, and refused where they must be.</summary>
public class TokenScopeTests
{
    private record Created(Guid Id, string Name, string Prefix, bool ReadOnly, DateTimeOffset CreatedAt, string Token);
    private record Listed(Guid Id, string Name, string Prefix, bool ReadOnly);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";

    private static async Task<(TestAppFactory f, HttpClient session, Guid spaceId, PageDetail page)> World()
    {
        var f = new TestAppFactory();
        var session = f.CreateClient();
        await session.RegisterAndSignInAsync();
        var spaceId = await session.CreateSpaceAsync();
        var page = (await (await session.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Scoped", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;
        return (f, session, spaceId, page);
    }

    private static async Task<HttpClient> TokenClient(TestAppFactory f, HttpClient session, bool readOnly)
    {
        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = readOnly ? "ro" : "rw", ReadOnly = readOnly }))
            .Content.ReadFromJsonAsync<Created>();
        Assert.Equal(readOnly, created!.ReadOnly);
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        return client;
    }

    [Fact]
    public async Task A_read_only_token_reads_but_cannot_change_anything_over_rest()
    {
        var (f, session, spaceId, page) = await World();
        using var _ = f;
        var ro = await TokenClient(f, session, readOnly: true);

        Assert.Equal(HttpStatusCode.OK, (await ro.GetAsync($"/api/pages/{page.Id}")).StatusCode);

        var refused = await ro.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = Doc });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("read_only_token", await refused.Content.ReadAsStringAsync());

        // Refused before the handler ran: nothing changed.
        var detail = await ro.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/pages/{page.Id}");
        Assert.Equal(1, detail.GetProperty("currentVersionNumber").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await ro.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "No", ContentJson = Doc })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ro.DeleteAsync($"/api/pages/{page.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_read_only_token_cannot_get_into_live_editing()
    {
        // The collab token is fetched with a GET, which the method-based
        // read-only check lets through, but it grants writes to the draft.
        var (f, session, _, page) = await World();
        using var _ = f;
        var ro = await TokenClient(f, session, readOnly: true);
        var rw = await TokenClient(f, session, readOnly: false);

        var refused = await ro.GetAsync($"/api/pages/{page.Id}/collab-token");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("read_only_token", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await rw.GetAsync($"/api/pages/{page.Id}/collab-token")).StatusCode);
    }

    [Fact]
    public async Task Renaming_a_page_without_sending_content_keeps_the_content()
    {
        var (f, session, _, page) = await World();
        using var _ = f;
        var rw = await TokenClient(f, session, readOnly: false);

        (await rw.PutAsJsonAsync($"/api/pages/{page.Id}", new { Title = "Renamed" })).EnsureSuccessStatusCode();

        var detail = await rw.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/pages/{page.Id}");
        Assert.Equal("Renamed", detail.GetProperty("title").GetString());
        Assert.Contains("hi", detail.GetProperty("contentJson").GetString());
    }

    [Fact]
    public async Task A_full_token_and_a_cookie_session_are_unaffected()
    {
        var (f, session, _, page) = await World();
        using var _ = f;
        var rw = await TokenClient(f, session, readOnly: false);

        Assert.Equal(HttpStatusCode.OK, (await rw.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = Doc })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = Doc })).StatusCode);
    }

    [Fact]
    public async Task Omitting_the_flag_mints_a_full_token_so_nothing_narrows_silently()
    {
        var (f, session, _, _) = await World();
        using var _ = f;
        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = "legacy shape" }))
            .Content.ReadFromJsonAsync<Created>();
        Assert.False(created!.ReadOnly);

        var listed = await session.GetFromJsonAsync<List<Listed>>("/api/api-tokens");
        Assert.Contains(listed!, t => t.Id == created.Id && !t.ReadOnly);
    }
}
