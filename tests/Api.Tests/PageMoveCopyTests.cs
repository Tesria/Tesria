using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Moving a page to another space, and copying pages (dev-plan 15.3).</summary>
public class PageMoveCopyTests
{
    private record PageDetail(Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, string ContentJson);
    private record TreeNode(Guid Id, string Title, List<TreeNode> Children);
    private record Copied(Guid Id, Guid SpaceId, string Title, int Pages);
    private record AttachmentRow(Guid Id, Guid PageId, string Filename);

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title, Guid? parent = null, string? content = null) =>
        (await (await c.PostAsJsonAsync("/api/pages", new
        {
            SpaceId = spaceId, ParentPageId = parent, Title = title,
            ContentJson = content ?? $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{title}} body"}]}]}""",
        })).Content.ReadFromJsonAsync<PageDetail>())!;

    [Fact]
    public async Task A_page_moves_to_another_space_with_the_pages_under_it()
    {
        using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var from = await c.CreateSpaceAsync();
        var to = await c.CreateSpaceAsync();
        var parent = await NewPage(c, from, "Guides");
        var child = await NewPage(c, from, "Install", parent.Id);

        (await c.PutAsJsonAsync($"/api/pages/{parent.Id}/move", new { ParentPageId = (Guid?)null, Index = 0, SpaceId = to }))
            .EnsureSuccessStatusCode();

        Assert.Equal(to, (await c.GetFromJsonAsync<PageDetail>($"/api/pages/{parent.Id}"))!.SpaceId);
        Assert.Equal(to, (await c.GetFromJsonAsync<PageDetail>($"/api/pages/{child.Id}"))!.SpaceId);
        Assert.Empty((await c.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={from}"))!);
        var tree = await c.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={to}");
        Assert.Equal("Install", tree!.Single().Children.Single().Title);
    }

    [Fact]
    public async Task A_copy_brings_content_labels_and_its_own_attachments()
    {
        using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var space = await c.CreateSpaceAsync();
        var page = await NewPage(c, space, "Plan");
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(bytes, "file", "pic.png");
        var att = (await (await c.PostAsync($"/api/pages/{page.Id}/attachments", form)).Content.ReadFromJsonAsync<AttachmentRow>())!;
        var withImage = "{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"/api/attachments/" + att.Id + "/download\",\"alt\":\"pic\"}}]}";
        (await c.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = withImage })).EnsureSuccessStatusCode();
        await c.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = "launch" });
        await NewPage(c, space, "Child", page.Id);

        var copied = (await (await c.PostAsJsonAsync($"/api/pages/{page.Id}/copy", new { IncludeChildren = true }))
            .Content.ReadFromJsonAsync<Copied>())!;
        Assert.Equal("Copy of Plan", copied.Title);
        Assert.Equal(2, copied.Pages);

        var copy = (await c.GetFromJsonAsync<PageDetail>($"/api/pages/{copied.Id}"))!;
        var copyAtts = (await c.GetFromJsonAsync<List<AttachmentRow>>($"/api/pages/{copied.Id}/attachments"))!;
        var copyAtt = Assert.Single(copyAtts);
        Assert.NotEqual(att.Id, copyAtt.Id);
        Assert.Contains(copyAtt.Id.ToString(), copy.ContentJson);
        Assert.DoesNotContain(att.Id.ToString(), copy.ContentJson);
        var labels = await c.GetStringAsync($"/api/pages/{copied.Id}/labels");
        Assert.Contains("launch", labels);
        var tree = await c.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={space}");
        Assert.Contains(tree!, n => n.Title == "Copy of Plan" && n.Children.Any(k => k.Title == "Child"));
    }

    [Fact]
    public async Task Copying_a_page_beneath_itself_finishes()
    {
        using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        var space = await c.CreateSpaceAsync();
        var page = await NewPage(c, space, "Loop");
        await NewPage(c, space, "Under", page.Id);

        var copied = (await (await c.PostAsJsonAsync($"/api/pages/{page.Id}/copy",
            new { ParentPageId = page.Id, IncludeChildren = true })).Content.ReadFromJsonAsync<Copied>())!;
        Assert.Equal(2, copied.Pages);
    }
}
