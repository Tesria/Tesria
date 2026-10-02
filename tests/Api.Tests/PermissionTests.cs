using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Security-critical behavior: these assert that access is actually *denied*,
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
    public async Task Closing_a_space_to_everyone_locks_everyone_else_out()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "PRIV");
        var page = await NewPage(alice, space.Id, "Confidential");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        // A grant no longer closes a space (dev-plan 21.1): what everyone
        // signed in gets is its own setting.
        var grant = await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        await alice.MakePrivateAsync(space.Key);

        // Bob loses access, and the space is not even discoverable.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{page.Id}")).StatusCode);
        Assert.DoesNotContain((await bob.GetFromJsonAsync<List<SpaceDto>>("/api/spaces"))!, s => s.Id == space.Id);
        Assert.Empty((await bob.GetFromJsonAsync<List<SearchResult>>("/api/search?q=pineapple"))!);

        // Alice keeps working: she made the space, so she is in its Admins group.
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
        await alice.MakePrivateAsync(space.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        // Put Bob in a group and grant the group view access.
        var group = await (await alice.PostAsJsonAsync("/api/groups", new { Name = "Readers" }))
            .Content.ReadFromJsonAsync<GroupDto>();
        await alice.PostAsJsonAsync($"/api/groups/{group!.Id}/members", new { UserId = bobId });
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = Group, PrincipalId = group.Id, Operation = View });

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
    }

    private record GrantRow(Guid Id, int PrincipalType, Guid PrincipalId, int Operation);

    private record BuiltInGroupDto(Guid Id, string Name, string? Description, int MemberCount, bool BuiltIn);
    private record MemberDto(Guid UserId, string Email, string DisplayName);

    [Fact]
    public async Task The_built_in_groups_follow_each_accounts_tier()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        var member = factory.CreateClient();
        var memberId = await member.RegisterAndSignInAsync();

        var groups = await owner.GetFromJsonAsync<List<BuiltInGroupDto>>("/api/groups");
        Assert.Equal(["Owner", "Admins", "Users", "Global Viewers", "Global Reviewers"], groups!.Take(5).Select(g => g.Name));
        Assert.All(groups!.Take(5), g => Assert.True(g.BuiltIn));
        var users = groups!.Single(g => g.Name == "Users");
        var admins = groups!.Single(g => g.Name == "Admins");
        Assert.Equal(2, users.MemberCount);
        Assert.Equal([ownerId], (await owner.GetFromJsonAsync<List<MemberDto>>($"/api/groups/{admins.Id}/members"))!.Select(m => m.UserId));

        // A private space shared with Users is readable by everyone with an account.
        var space = await NewSpace(owner, "ALL");
        await owner.MakePrivateAsync(space.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
        await owner.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = Group, PrincipalId = users.Id, Operation = View });
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        // Promotion puts someone in Admins without anyone touching the group.
        (await owner.PutAsJsonAsync($"/api/admin/users/{memberId}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(2, (await owner.GetFromJsonAsync<List<MemberDto>>($"/api/groups/{admins.Id}/members"))!.Count);

        // And they are not anyone's to change.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/groups/{users.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/groups/{users.Id}", new { Name = "Everyone" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/groups/{admins.Id}/members", new { UserId = ownerId })).StatusCode);
    }

    [Fact]
    public async Task A_private_space_can_be_made_open_again_by_its_administrator()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "BACK");
        await alice.MakePrivateAsync(space.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);

        // Not someone who cannot administer it.
        Assert.Contains((await bob.DeleteAsync($"/api/spaces/{space.Key}/permissions")).StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/spaces/{space.Key}/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_group_takes_its_grants_with_it()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "GONE");
        await alice.MakePrivateAsync(space.Key);
        var group = (await (await alice.PostAsJsonAsync("/api/groups", new { Name = "Readers" }))
            .Content.ReadFromJsonAsync<GroupDto>())!;
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = Group, PrincipalId = group.Id, Operation = View });

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);

        var access = await alice.GetFromJsonAsync<AccessDto>($"/api/spaces/{space.Key}/permissions");
        Assert.DoesNotContain(access!.Grants, g => g.PrincipalId == group.Id);
    }

    [Fact]
    public async Task A_group_that_is_a_spaces_last_administrator_cannot_be_deleted()
    {
        // Since 21.1 removing a grant cannot open a space, but it can still
        // leave it with nobody to administer it.
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await NewSpace(alice, "ONLY");
        await alice.MakePrivateAsync(space.Key);
        var group = (await (await alice.PostAsJsonAsync("/api/groups", new { Name = "Owners" }))
            .Content.ReadFromJsonAsync<GroupDto>())!;
        await alice.PostAsJsonAsync($"/api/groups/{group.Id}/members", new { UserId = aliceId });
        (await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = Group, PrincipalId = group.Id, Operation = Admin })).EnsureSuccessStatusCode();
        // She made the space, so she is in its Admins group too; she leaves
        // it, which the group's grant allows, leaving the group.
        var admins = await alice.SpaceGroupAsync(space.Key, 2);
        (await alice.DeleteAsync($"/api/groups/{admins}/members/{aliceId}")).EnsureSuccessStatusCode();

        var refused = await alice.DeleteAsync($"/api/groups/{group.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("only access", await refused.Content.ReadAsStringAsync());

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/spaces/{space.Key}")).StatusCode);
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

        // Alice closes the space (she is in its Admins group); Bob gets view only.
        await alice.MakePrivateAsync(space.Key);
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
        await alice.MakePrivateAsync(space.Key);

        // A grant of her own can go: the Admins group still has her.
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });
        var access = await alice.GetFromJsonAsync<AccessDto>($"/api/spaces/{space.Key}/permissions");
        var adminRow = access!.Grants.Single(p => p.Operation == Admin);
        Assert.Equal(HttpStatusCode.NoContent,
            (await alice.DeleteAsync($"/api/spaces/{space.Key}/permissions/{adminRow.Id}")).StatusCode);

        // But not her place in Admins, the last there is.
        var admins = await alice.SpaceGroupAsync(space.Key, 2);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.DeleteAsync($"/api/groups/{admins}/members/{aliceId}")).StatusCode);
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
        await alice.MakePrivateAsync(space.Key);

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
    private record AccessDto(int? EveryoneAccess, bool CanManageAdmins, List<PermissionRow> Grants);
}
