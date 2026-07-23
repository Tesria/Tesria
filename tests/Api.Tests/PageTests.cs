using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class PageTests
{
    private record PageDetail(
        Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, int Position, int Status,
        int CurrentVersionNumber, string ContentJson, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private record VersionMeta(Guid Id, int VersionNumber, string? ChangeComment, Guid AuthorId, DateTimeOffset CreatedAt);
    private record VersionContent(Guid Id, int VersionNumber, string ContentJson, string? ChangeComment, Guid AuthorId, DateTimeOffset CreatedAt);
    private record TreeNode(Guid Id, string Title, int Position, List<TreeNode> Children);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";

    private static async Task<(TestAppFactory, HttpClient, Guid)> NewClientWithSpace()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        return (factory, client, spaceId);
    }

    [Fact]
    public async Task Create_starts_at_version_1_and_is_fetchable()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;

        var create = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Home", ContentJson = Doc });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var page = await create.Content.ReadFromJsonAsync<PageDetail>();
        Assert.Equal(1, page!.CurrentVersionNumber);

        var fetched = await client.GetFromJsonAsync<PageDetail>($"/api/pages/{page.Id}");
        Assert.Equal("Home", fetched!.Title);
        Assert.Contains("hi", fetched.ContentJson);
    }

    [Fact]
    public async Task Update_creates_a_new_version_and_keeps_history()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Doc", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        var updated = await (await client.PutAsJsonAsync($"/api/pages/{page!.Id}",
            new { Title = "Doc v2", ContentJson = """{"type":"doc","content":[]}""", ChangeComment = "trim" }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.Equal(2, updated!.CurrentVersionNumber);
        Assert.Equal("Doc v2", updated.Title);

        var versions = await client.GetFromJsonAsync<List<VersionMeta>>($"/api/pages/{page.Id}/versions");
        Assert.Equal(2, versions!.Count);
        Assert.Equal(2, versions[0].VersionNumber); // newest first
    }

    [Fact]
    public async Task Restore_appends_a_version_with_old_content()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "P", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        await client.PutAsJsonAsync($"/api/pages/{page!.Id}",
            new { Title = (string?)null, ContentJson = """{"type":"doc","content":[]}""", ChangeComment = (string?)null });

        var restored = await (await client.PostAsync($"/api/pages/{page.Id}/versions/1/restore", null))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.Equal(3, restored!.CurrentVersionNumber); // new version appended
        Assert.Contains("hi", restored.ContentJson);     // content of v1 restored

        var v1 = await client.GetFromJsonAsync<VersionContent>($"/api/pages/{page.Id}/versions/1");
        Assert.Contains("hi", v1!.ContentJson);
    }

    [Fact]
    public async Task Create_rejects_invalid_content_and_missing_title()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;

        var badJson = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "X", ContentJson = "{ not json" });
        Assert.Equal(HttpStatusCode.BadRequest, badJson.StatusCode);

        var noTitle = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "  ", ContentJson = Doc });
        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
    }

    [Fact]
    public async Task Tree_reflects_parent_child_structure()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var root = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Root", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = root!.Id, Title = "Child", ContentJson = Doc });

        var tree = await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}");
        Assert.Single(tree!);
        Assert.Equal("Root", tree![0].Title);
        Assert.Single(tree[0].Children);
        Assert.Equal("Child", tree[0].Children[0].Title);
    }

    [Fact]
    public async Task Move_rejects_cycles()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var root = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Root", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var child = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = root!.Id, Title = "Child", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        // Moving Root beneath its own Child would create a cycle.
        var res = await client.PutAsJsonAsync($"/api/pages/{root.Id}/move",
            new { ParentPageId = child!.Id, Position = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private record TrashedPage(Guid Id, string Title, DateTimeOffset DeletedAt, Guid? DeletedById);

    [Fact]
    public async Task Delete_trashes_page_and_its_subtree()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var root = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Root", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var child = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = root!.Id, Title = "Child", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        // Deleting the root trashes the whole subtree in one step.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pages/{root.Id}")).StatusCode);

        // Both drop out of the tree and can no longer be fetched.
        Assert.Empty((await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/pages/{root.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/pages/{child!.Id}")).StatusCode);

        // The trash lists only the deleted root, not each descendant.
        var trash = await client.GetFromJsonAsync<List<TrashedPage>>($"/api/pages/trash?spaceId={spaceId}");
        Assert.Single(trash!);
        Assert.Equal(root.Id, trash![0].Id);
    }

    [Fact]
    public async Task Restore_brings_back_the_trashed_subtree()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var root = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Root", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = root!.Id, Title = "Child", ContentJson = Doc });
        await client.DeleteAsync($"/api/pages/{root.Id}");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/pages/{root.Id}/restore", null)).StatusCode);

        var tree = await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}");
        Assert.Single(tree!);
        Assert.Single(tree![0].Children); // child came back too
        Assert.Empty((await client.GetFromJsonAsync<List<TrashedPage>>($"/api/pages/trash?spaceId={spaceId}"))!);
    }

    [Fact]
    public async Task Purge_permanently_removes_the_trashed_page()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Doomed", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        await client.DeleteAsync($"/api/pages/{page!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pages/{page.Id}/purge")).StatusCode);
        // Gone for good: no longer in trash and cannot be restored.
        Assert.Empty((await client.GetFromJsonAsync<List<TrashedPage>>($"/api/pages/trash?spaceId={spaceId}"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/pages/{page.Id}/restore", null)).StatusCode);
    }
}
