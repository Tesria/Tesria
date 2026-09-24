using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>A page's emoji, before its title (dev-plan 15.7).</summary>
public class PageEmojiTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title, int CurrentVersionNumber, string? Emoji);
    private record TreeNode(Guid Id, string Title, List<TreeNode> Children, string? Emoji);
    private record SpaceRow(Guid Id, string Key);

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title, Guid? parent = null) =>
        (await (await c.PostAsJsonAsync("/api/pages", new
        {
            SpaceId = spaceId, ParentPageId = parent, Title = title,
            ContentJson = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"body"}]}]}""",
        })).Content.ReadFromJsonAsync<PageDetail>())!;

    private static Task<HttpResponseMessage> SetEmoji(HttpClient c, Guid page, string? emoji) =>
        c.PutAsJsonAsync($"/api/pages/{page}/emoji", new { Emoji = emoji });

    [Fact]
    public async Task An_emoji_shows_on_the_page_and_in_the_tree_and_makes_no_new_version()
    {
        using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var space = await c.CreateSpaceAsync();
        var page = await NewPage(c, space, "The editor");

        Assert.Equal(HttpStatusCode.NoContent, (await SetEmoji(c, page.Id, "✏️")).StatusCode);

        var read = await c.GetFromJsonAsync<PageDetail>($"/api/pages/{page.Id}");
        Assert.Equal("✏️", read!.Emoji);
        Assert.Equal(page.CurrentVersionNumber, read.CurrentVersionNumber);
        var tree = await c.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={space}");
        Assert.Equal("✏️", Assert.Single(tree!).Emoji);

        Assert.Equal(HttpStatusCode.NoContent, (await SetEmoji(c, page.Id, null)).StatusCode);
        Assert.Null((await c.GetFromJsonAsync<PageDetail>($"/api/pages/{page.Id}"))!.Emoji);
    }

    [Theory]
    [InlineData("1️⃣")]
    [InlineData("🔟")]
    [InlineData("•")]
    [InlineData("▸")]
    [InlineData("✔")]
    public async Task Numbers_and_bullets_are_accepted_as_well_as_emoji(string value)
    {
        // The picker offers them for pages read in order (the owner, 2026-09-23).
        using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var page = await NewPage(c, await c.CreateSpaceAsync(), "Step");
        Assert.Equal(HttpStatusCode.NoContent, (await SetEmoji(c, page.Id, value)).StatusCode);
        Assert.Equal(value, (await c.GetFromJsonAsync<PageDetail>($"/api/pages/{page.Id}"))!.Emoji);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("🚀🚀🚀🚀🚀🚀🚀🚀🚀")]
    [InlineData("\u0007")]
    public async Task Something_that_is_not_one_emoji_is_refused(string value)
    {
        using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var page = await NewPage(c, await c.CreateSpaceAsync(), "Page");
        Assert.Equal(HttpStatusCode.BadRequest, (await SetEmoji(c, page.Id, value)).StatusCode);
    }

    [Fact]
    public async Task Only_someone_who_may_edit_the_page_may_set_it()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var space = await owner.CreateSpaceAsync();
        var page = await NewPage(owner, space, "Private");
        // Restricted to its author for editing.
        var me = await owner.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        (await owner.PostAsJsonAsync($"/api/pages/{page.Id}/restrictions",
            new { PrincipalType = 0, PrincipalId = me.GetProperty("id").GetGuid(), Operation = 1 })).EnsureSuccessStatusCode();

        var other = factory.CreateClient();
        await other.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await SetEmoji(other, page.Id, "🔥")).StatusCode);
    }

    [Fact]
    public async Task A_copy_keeps_the_emoji_and_a_pack_carries_it()
    {
        await using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var space = await c.CreateSpaceAsync("EMO");
        var page = await NewPage(c, space, "Guides");
        (await SetEmoji(c, page.Id, "📘")).EnsureSuccessStatusCode();

        var copy = await (await c.PostAsJsonAsync($"/api/pages/{page.Id}/copy", new { IncludeChildren = false }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.Equal("📘", (await c.GetFromJsonAsync<PageDetail>($"/api/pages/{copy!.Id}"))!.Emoji);

        var pack = await (await c.GetAsync("/api/spaces/EMO/export/pack")).Content.ReadAsByteArrayAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pack);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "pack.zip");
        form.Add(new StringContent("EMOCOPY"), "key");
        (await c.PostAsync("/api/spaces/import", form)).EnsureSuccessStatusCode();

        var imported = await c.GetFromJsonAsync<SpaceRow>("/api/spaces/EMOCOPY");
        var tree = await c.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={imported!.Id}");
        Assert.All(tree!, n => Assert.Equal("📘", n.Emoji));
    }
}
