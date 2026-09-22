using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Security-critical behaviour: these assert that access is actually *denied*,
/// not merely that the happy path works.
/// </summary>
public class PermissionTests
{
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record GroupDto(Guid Id, string Name, string? Description, int MemberCount);
    private record SearchResult(Guid PageId, Guid SpaceId, string SpaceKey, string Title, string Snippet);
    private record TreeNode(Guid Id, string Title, int Position, List<TreeNode> Children);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"secret pineapple"}]}]}""";

    // User = 0, Group = 1 | View = 0, Edit = 1, Admin = 2
    private const int User = 0, Group = 1;
    private const int View = 0, Admin = 2;

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title, Guid? parent = null) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    private static async Task<SpaceDto> NewSpace(HttpClient c, string key) =>
        (await (await c.PostAsJsonAsync("/api/spaces", new { Key = key, Name = $"Space {key}" }))
            .Content.ReadFromJsonAsync<SpaceDto>())!;

    [Fact]
    public async Task Default_open_lets_any_signed_in_user_read_an_unconfigured_space()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "OPEN");
        var page = await NewPage(alice, space.Id, "Public");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/pages/{page.Id}")).StatusCode);
        Assert.Contains((await bob.GetFromJsonAsync<List<SpaceDto>>("/api/spaces"))!, s => s.Id == space.Id);
    }

    [Fact]
    public async Task Granting_a_space_permission_locks_everyone_else_out()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "PRIV");
        var page = await NewPage(alice, space.Id, "Confidential");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        // The moment a grant exists the space stops being default-open.
        var grant = await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        // Bob loses access, and the space is not even discoverable.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{page.Id}")).StatusCode);
        Assert.DoesNotContain((await bob.GetFromJsonAsync<List<SpaceDto>>("/api/spaces"))!, s => s.Id == space.Id);
        Assert.Empty((await bob.GetFromJsonAsync<List<SearchResult>>("/api/search?q=pineapple"))!);

        // Alice keeps working: the grantor is auto-given admin to prevent lockout.
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/pages/{page.Id}")).StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<List<SearchResult>>("/api/search?q=pineapple"))!);
    }

    [Fact]
    public async Task Access_can_be_granted_through_a_group()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "TEAM");

        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();

        // Lock the space down to Alice only.
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        // Put Bob in a group and grant the group view access.
        var group = await (await alice.PostAsJsonAsync("/api/groups", new { Name = "Readers" }))
            .Content.ReadFromJsonAsync<GroupDto>();
        await alice.PostAsJsonAsync($"/api/groups/{group!.Id}/members", new { UserId = bobId });
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = Group, PrincipalId = group.Id, Operation = View });

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
    }

    [Fact]
    public async Task Page_restrictions_hide_the_page_and_its_children()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "REST");
        var open = await NewPage(alice, space.Id, "Open Page");
        var secret = await NewPage(alice, space.Id, "Secret Page");
        var child = await NewPage(alice, space.Id, "Secret Child", secret.Id);

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/pages/{secret.Id}")).StatusCode);

        await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View });

        // The restricted page and everything beneath it disappear for Bob...
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{secret.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{child.Id}")).StatusCode);
        // ...while the rest of the space is unaffected.
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/pages/{open.Id}")).StatusCode);

        var tree = await bob.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={space.Id}");
        Assert.Single(tree!);
        Assert.Equal("Open Page", tree![0].Title);

        // Alice still sees everything.
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/pages/{child.Id}")).StatusCode);
    }

    [Fact]
    public async Task Restricted_pages_do_not_leak_through_search_export_or_attachments()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "LEAK");
        var secret = await NewPage(alice, space.Id, "Secret");
        await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/labels", new { Name = "classified" });

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Single((await bob.GetFromJsonAsync<List<SearchResult>>("/api/search?q=pineapple"))!);

        await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View });

        Assert.Empty((await bob.GetFromJsonAsync<List<SearchResult>>("/api/search?q=pineapple"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.GetAsync($"/api/pages/{secret.Id}/export?format=markdown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.GetAsync($"/api/pages/{secret.Id}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.GetAsync($"/api/pages/{secret.Id}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.GetAsync($"/api/pages/{secret.Id}/versions")).StatusCode);
        // Label browsing must not reveal it either.
        Assert.Empty((await bob.GetFromJsonAsync<List<object>>("/api/labels/classified/pages"))!);
    }

    [Fact]
    public async Task Write_operations_are_forbidden_without_edit_rights()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "RO");
        var page = await NewPage(alice, space.Id, "Read Only");

        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();

        // Alice takes admin; Bob gets view only.
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = bobId, Operation = View });

        // Bob can read...
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/pages/{page.Id}")).StatusCode);
        // ...but every write is refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Hijacked", ContentJson = Doc, ChangeComment = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/pages/{page.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync(
            $"/api/pages/{page.Id}/labels", new { Name = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, ParentPageId = (Guid?)null, Title = "New", ContentJson = Doc })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/spaces/{space.Key}",
            new { Name = "Renamed", Description = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_removed()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "LOCK");

        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });

        var perms = await alice.GetFromJsonAsync<List<PermissionRow>>($"/api/spaces/{space.Key}/permissions");
        var adminRow = perms!.Single(p => p.Operation == Admin);

        var res = await alice.DeleteAsync($"/api/spaces/{space.Key}/permissions/{adminRow.Id}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Audit_entries_do_not_leak_titles_from_inaccessible_spaces()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "AUDSEC");
        await NewPage(alice, space.Id, "TopSecretTitle");

        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        // The audit log is an administrator's view, and administrators do
        // not bypass space permissions, which is what this test is about.
        (await alice.PutAsJsonAsync($"/api/admin/users/{bobId}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        // While default-open, Bob legitimately sees the entry.
        var before = await bob.GetFromJsonAsync<List<AuditRow>>("/api/audit");
        Assert.Contains(before!, e => e.MetadataJson != null && e.MetadataJson.Contains("TopSecretTitle"));

        // Once the space is private, its audit trail must disappear for Bob.
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });

        var after = await bob.GetFromJsonAsync<List<AuditRow>>("/api/audit");
        Assert.DoesNotContain(after!, e => e.MetadataJson != null && e.MetadataJson.Contains("TopSecretTitle"));
        // Alice still sees her own space's history.
        var aliceView = await alice.GetFromJsonAsync<List<AuditRow>>("/api/audit");
        Assert.Contains(aliceView!, e => e.MetadataJson != null && e.MetadataJson.Contains("TopSecretTitle"));
    }

    private record AuditRow(
        Guid Id, string Action, string TargetType, Guid? TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson, DateTimeOffset CreatedAt);

    private record PermissionRow(Guid Id, int PrincipalType, Guid PrincipalId, string? PrincipalName, int Operation);
}
