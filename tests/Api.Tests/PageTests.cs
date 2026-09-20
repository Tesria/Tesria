using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

public class PageTests
{
    private record PageDetail(
        Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, int Position, int Status,
        int CurrentVersionNumber, string ContentJson, bool FullWidth, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private record VersionMeta(Guid Id, int VersionNumber, string? ChangeComment, Guid AuthorId, DateTimeOffset CreatedAt);
    private record VersionContent(Guid Id, int VersionNumber, string ContentJson, string? ChangeComment, Guid AuthorId, DateTimeOffset CreatedAt);
    private record TreeNode(Guid Id, string Title, int Position, List<TreeNode> Children);
    private record SpaceDto(Guid Id, string Key, string Name);

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
            new { ParentPageId = child!.Id, Index = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Move_reorders_siblings_by_index()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        async Task<PageDetail> MakePage(string title) =>
            (await (await client.PostAsJsonAsync("/api/pages",
                new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
                .Content.ReadFromJsonAsync<PageDetail>())!;
        var a = await MakePage("A");
        var b = await MakePage("B");
        var c = await MakePage("C");

        // Drag "C" to the front of its siblings.
        var res = await client.PutAsJsonAsync($"/api/pages/{c.Id}/move",
            new { ParentPageId = (Guid?)null, Index = 0 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var tree = (await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}"))!;
        Assert.Equal(["C", "A", "B"], tree.Select(n => n.Title));
        // Positions are renumbered densely, not just the moved page.
        Assert.Equal([0, 1, 2], tree.Select(n => n.Position));
    }

    [Fact]
    public async Task Move_reparents_a_page_and_appends_to_the_new_siblings_by_default()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var parentA = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Parent A", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var parentB = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Parent B", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var existingChild = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parentB!.Id, Title = "Existing Child", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var mover = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parentA!.Id, Title = "Mover", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        var res = await client.PutAsJsonAsync($"/api/pages/{mover!.Id}/move",
            new { ParentPageId = parentB.Id, Index = 1 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var tree = (await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}"))!;
        var newParentB = tree.Single(n => n.Id == parentB.Id);
        Assert.Equal(["Existing Child", "Mover"], newParentB.Children.Select(n => n.Title));
        Assert.Empty(tree.Single(n => n.Id == parentA.Id).Children);
        Assert.Equal(existingChild!.Id, newParentB.Children[0].Id); // sanity: existing child untouched
    }

    [Fact]
    public async Task Move_rejects_a_different_space()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var otherSpaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Mine", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var otherSpacePage = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = otherSpaceId, ParentPageId = (Guid?)null, Title = "Theirs", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        var res = await client.PutAsJsonAsync($"/api/pages/{page!.Id}/move",
            new { ParentPageId = otherSpacePage!.Id, Index = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Move_requires_edit_rights_on_both_the_page_and_the_destination_parent()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var space = (await alice.GetFromJsonAsync<List<SpaceDto>>("/api/spaces"))!.Single(s => s.Id == spaceId);
        var pageA = await (await alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "A", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        var pageB = await (await alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "B", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        // Locking the space to explicit grants also drops Alice to View unless
        // re-granted — restore her Admin access alongside Bob's View-only.
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = 0, PrincipalId = aliceId, Operation = 2 });
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = 0, PrincipalId = bobId, Operation = 0 });

        var res = await bob.PutAsJsonAsync($"/api/pages/{pageA!.Id}/move",
            new { ParentPageId = pageB!.Id, Index = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task SetLayout_toggles_full_width_without_creating_a_new_version()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Wide", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.False(page!.FullWidth);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/pages/{page.Id}/layout", new { FullWidth = true })).StatusCode);

        var fetched = await client.GetFromJsonAsync<PageDetail>($"/api/pages/{page.Id}");
        Assert.True(fetched!.FullWidth);
        Assert.Equal(1, fetched.CurrentVersionNumber); // display metadata only — no new version
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
    [Fact]
    public async Task A_page_whose_current_version_pointer_is_missing_can_still_be_edited()
    {
        // The wedged state a half-committed create used to leave behind: a
        // page with a version and no pointer at it. Numbering the next
        // version from zero collided with the one already there and failed
        // every edit from then on (found by the 12.1 fixture).
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Wedged", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Pages.FirstAsync(p => p.Id == page!.Id);
            row.CurrentVersionId = null;
            await db.SaveChangesAsync();
        }

        var res = await client.PutAsJsonAsync($"/api/pages/{page!.Id}",
            new { Title = "Wedged", ContentJson = Doc });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

}
