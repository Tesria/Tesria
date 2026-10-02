using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The stronger Groups page (dev-plan 21.4): its list searched and filtered
/// within 21.1's visibility rule, bulk adds under the same rights as single
/// ones, and "why can this person see this space?", which must agree with
/// <see cref="IPermissionService"/> in every case it explains.
/// </summary>
public class GroupsPageTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}""";
    private const int UserPrincipal = 0, GroupPrincipal = 1;
    private const int View = 0, Edit = 1, Admin = 2;
    private const int Viewers = 0, Editors = 1, Admins = 2, Reviewers = 3;

    private sealed record Person(HttpClient Client, Guid Id);

    private static async Task<Person> PersonAsync(TestAppFactory f)
    {
        var c = f.CreateClient();
        return new Person(c, await c.RegisterAndSignInAsync());
    }

    private static async Task<Guid> PageAsync(HttpClient c, Guid spaceId, string title = "Page", Guid? parent = null)
    {
        var res = await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient c, Guid group, Guid user) =>
        c.PostAsJsonAsync($"/api/groups/{group}/members", new { UserId = user });

    private static Task<HttpResponseMessage> BulkAsync(HttpClient c, Guid group, Guid[]? ids = null, string[]? emails = null) =>
        c.PostAsJsonAsync($"/api/groups/{group}/members/bulk", new { UserIds = ids, Emails = emails });

    private static Task<HttpResponseMessage> EveryoneAsync(HttpClient c, string key, int? access) =>
        c.PutAsJsonAsync($"/api/spaces/{key}/permissions/everyone", new { Access = access });

    private static async Task<Guid> CustomGroupAsync(HttpClient owner, string name)
    {
        var res = await owner.PostAsJsonAsync("/api/groups", new { Name = name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static T Scoped<T>(TestAppFactory f, Func<AppDbContext, Task<T>> read)
    {
        using var scope = f.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>()).GetAwaiter().GetResult();
    }

    private static void Suspend(TestAppFactory f, Guid userId) => Scoped(f, async db =>
    {
        (await db.Users.SingleAsync(u => u.Id == userId)).Status = UserStatus.Suspended;
        return await db.SaveChangesAsync();
    });

    private static async Task<List<JsonElement>> OverviewAsync(HttpClient c, string query = "")
    {
        var res = await c.GetAsync("/api/groups/overview" + query);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groups").EnumerateArray().ToList();
    }

    // -- the list ----------------------------------------------------------------

    [Fact]
    public async Task The_default_list_has_built_in_global_and_custom_groups_and_no_space_groups()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("LISTED");
        await CustomGroupAsync(owner.Client, "Engineering");

        var groups = await OverviewAsync(owner.Client);
        Assert.Equal(["builtin", "builtin", "builtin", "global", "global", "custom"],
            groups.Select(g => g.GetProperty("kind").GetString()));
        Assert.Equal(["Owner", "Admins", "Users", "Global Viewers", "Global Reviewers", "Engineering"],
            groups.Select(g => g.GetProperty("name").GetString()));
        Assert.True(groups[3].GetProperty("everySpace").GetBoolean());
        Assert.False(groups[0].GetProperty("canManageMembers").GetBoolean());
        Assert.True(groups[5].GetProperty("canManageMembers").GetBoolean());

        // By kind, and under a chosen space its four groups, with its name.
        Assert.Equal(["Engineering"], (await OverviewAsync(owner.Client, "?kind=custom")).Select(g => g.GetProperty("name").GetString()));
        Assert.Empty(await OverviewAsync(owner.Client, "?kind=space"));
        var space = await OverviewAsync(owner.Client, "?space=listed");
        Assert.Equal([Admins, Editors, Viewers, Reviewers], space.Select(g => g.GetProperty("spaceRole").GetInt32()));
        Assert.All(space, g => Assert.Equal("LISTED", g.GetProperty("spaceKey").GetString()));
        Assert.EndsWith(" Admins", space[0].GetProperty("name").GetString());
        // Its own fixed grant is shown with it.
        Assert.Equal(Admin, space[0].GetProperty("grants")[0].GetProperty("operation").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync("/api/groups/overview?kind=odd")).StatusCode);
    }

    [Fact]
    public async Task The_list_needs_Manage_Groups()
    {
        using var f = new TestAppFactory();
        await PersonAsync(f);
        var member = await PersonAsync(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync("/api/groups/overview")).StatusCode);
    }

    [Fact]
    public async Task Search_and_filters_never_show_a_space_the_caller_cannot_see()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        await alice.Client.CreateSpaceAsync("SECRET");
        await alice.Client.MakePrivateAsync("SECRET");
        var custom = await CustomGroupAsync(owner.Client, "Secret Keepers");
        (await alice.Client.PostAsJsonAsync("/api/spaces/SECRET/permissions",
            new { PrincipalType = GroupPrincipal, PrincipalId = custom, Operation = Edit })).EnsureSuccessStatusCode();

        // The owner holds Manage Groups but cannot see the space: not under
        // its key, not by a search for its members, and not as a grant of
        // the custom group he can see.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/groups/overview?space=SECRET")).StatusCode);
        var aliceName = Scoped(f, db => db.Users.Where(u => u.Id == alice.Id).Select(u => u.DisplayName).SingleAsync());
        var found = await OverviewAsync(owner.Client, "?q=" + Uri.EscapeDataString(aliceName));
        Assert.DoesNotContain(found, g => g.GetProperty("kind").GetString() == "space");
        Assert.Contains(found, g => g.GetProperty("name").GetString() == "Users");
        var keepers = (await OverviewAsync(owner.Client, "?kind=custom")).Single();
        Assert.Empty(keepers.GetProperty("grants").EnumerateArray());

        // Once he can see it, its groups appear under a search and its key,
        // and the custom group's grant there shows.
        (await EveryoneAsync(alice.Client, "SECRET", Admin)).EnsureSuccessStatusCode();
        found = await OverviewAsync(owner.Client, "?q=" + Uri.EscapeDataString(aliceName));
        var admins = Assert.Single(found, g => g.GetProperty("kind").GetString() == "space");
        Assert.Equal(Admins, admins.GetProperty("spaceRole").GetInt32());
        Assert.Equal(aliceName, admins.GetProperty("matches")[0].GetProperty("displayName").GetString());
        Assert.Equal(4, (await OverviewAsync(owner.Client, "?space=SECRET")).Count);
        keepers = (await OverviewAsync(owner.Client, "?kind=custom")).Single();
        var grant = Assert.Single(keepers.GetProperty("grants").EnumerateArray());
        Assert.Equal(("SECRET", Edit), (grant.GetProperty("spaceKey").GetString(), grant.GetProperty("operation").GetInt32()));
        // He may administer it (it is open), so he may manage its Editors,
        // but not its Admins: he is not one of its explicit administrators.
        var space = await OverviewAsync(owner.Client, "?space=SECRET");
        Assert.False(space.Single(g => g.GetProperty("spaceRole").GetInt32() == Admins).GetProperty("canManageMembers").GetBoolean());
        Assert.True(space.Single(g => g.GetProperty("spaceRole").GetInt32() == Editors).GetProperty("canManageMembers").GetBoolean());
    }

    [Fact]
    public async Task Search_finds_groups_by_name_and_by_member_email()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var design = await CustomGroupAsync(owner.Client, "Design");
        await CustomGroupAsync(owner.Client, "Finance");
        (await AddAsync(owner.Client, design, bob.Id)).EnsureSuccessStatusCode();
        var bobEmail = Scoped(f, db => db.Users.Where(u => u.Id == bob.Id).Select(u => u.Email).SingleAsync());

        Assert.Equal(["Finance"], (await OverviewAsync(owner.Client, "?q=fin")).Select(g => g.GetProperty("name").GetString()));
        var byEmail = await OverviewAsync(owner.Client, "?q=" + Uri.EscapeDataString(bobEmail) + "&kind=custom");
        var match = Assert.Single(byEmail);
        Assert.Equal("Design", match.GetProperty("name").GetString());
        Assert.Equal(bobEmail, match.GetProperty("matches")[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Members_are_counted_active_and_suspended_apart()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var a = await PersonAsync(f);
        var b = await PersonAsync(f);
        var team = await CustomGroupAsync(owner.Client, "Team");
        (await AddAsync(owner.Client, team, a.Id)).EnsureSuccessStatusCode();
        (await AddAsync(owner.Client, team, b.Id)).EnsureSuccessStatusCode();
        Suspend(f, b.Id);

        var groups = await OverviewAsync(owner.Client);
        var row = groups.Single(g => g.GetProperty("name").GetString() == "Team");
        Assert.Equal((1, 1), (row.GetProperty("activeMembers").GetInt32(), row.GetProperty("suspendedMembers").GetInt32()));
        // Users follows active accounts only.
        var users = groups.Single(g => g.GetProperty("name").GetString() == "Users");
        Assert.Equal((2, 0), (users.GetProperty("activeMembers").GetInt32(), users.GetProperty("suspendedMembers").GetInt32()));
    }

    [Fact]
    public async Task A_groups_page_restrictions_are_listed_only_where_the_caller_can_read()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var spaceId = await alice.Client.CreateSpaceAsync("PAGES");
        var team = await CustomGroupAsync(owner.Client, "Readers");
        var shown = await PageAsync(alice.Client, spaceId, "Shown");
        var hidden = await PageAsync(alice.Client, spaceId, "Hidden");
        (await alice.Client.PostAsJsonAsync($"/api/pages/{shown}/restrictions",
            new { PrincipalType = GroupPrincipal, PrincipalId = team, Operation = Edit })).EnsureSuccessStatusCode();
        (await alice.Client.PostAsJsonAsync($"/api/pages/{hidden}/restrictions",
            new { PrincipalType = GroupPrincipal, PrincipalId = team, Operation = View })).EnsureSuccessStatusCode();

        var row = (await OverviewAsync(owner.Client, "?kind=custom")).Single();
        var restriction = Assert.Single(row.GetProperty("restrictions").EnumerateArray());
        Assert.Equal(("Shown", Edit), (restriction.GetProperty("pageTitle").GetString(), restriction.GetProperty("operation").GetInt32()));
    }

    // -- bulk add -----------------------------------------------------------------

    [Fact]
    public async Task Bulk_add_reports_added_already_members_and_refused()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var a = await PersonAsync(f);
        var b = await PersonAsync(f);
        var c = await PersonAsync(f);
        var team = await CustomGroupAsync(owner.Client, "Bulk");
        (await AddAsync(owner.Client, team, a.Id)).EnsureSuccessStatusCode();
        Suspend(f, c.Id);
        var cEmail = Scoped(f, db => db.Users.Where(u => u.Id == c.Id).Select(u => u.Email).SingleAsync());

        var res = await BulkAsync(owner.Client, team, [a.Id, b.Id, Guid.NewGuid()],
            [cEmail.ToUpperInvariant(), "nobody@example.com", "not-an-address"]);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var report = await res.Content.ReadFromJsonAsync<JsonElement>();
        var added = report.GetProperty("added").EnumerateArray().ToList();
        Assert.Equal([b.Id, c.Id], added.Select(p => p.GetProperty("userId").GetGuid()).OrderBy(x => x == c.Id));
        Assert.False(added.Single(p => p.GetProperty("userId").GetGuid() == c.Id).GetProperty("active").GetBoolean());
        Assert.Equal([a.Id], report.GetProperty("alreadyMembers").EnumerateArray().Select(p => p.GetProperty("userId").GetGuid()));
        var refused = report.GetProperty("refused").EnumerateArray().Select(r => r.GetProperty("input").GetString()).ToList();
        Assert.Equal(3, refused.Count);
        Assert.Contains("nobody@example.com", refused);
        Assert.Contains("not-an-address", refused);

        Assert.Equal(3, Scoped(f, db => db.UserGroups.CountAsync(ug => ug.GroupId == team)));
        // Audited one by one, as single adds are.
        Assert.Equal(3, Scoped(f, db => db.AuditLogs.CountAsync(l => l.Action == "group.member_added" && l.TargetId == team)));
    }

    [Fact]
    public async Task Bulk_add_needs_the_same_rights_as_a_single_add_per_group()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var carol = await PersonAsync(f);
        await alice.Client.CreateSpaceAsync("RIGHTS");
        var admins = await alice.Client.SpaceGroupAsync("RIGHTS", Admins);
        var editors = await alice.Client.SpaceGroupAsync("RIGHTS", Editors);
        var custom = await CustomGroupAsync(owner.Client, "Custom");

        // Manage Groups for a custom group; computed groups are nobody's.
        Assert.Equal(HttpStatusCode.Forbidden, (await BulkAsync(bob.Client, custom, [carol.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await BulkAsync(owner.Client, BuiltInGroups.UsersId, [carol.Id])).StatusCode);
        // In an open space Bob may fill its Editors, not its Admins.
        Assert.Equal(HttpStatusCode.Forbidden, (await BulkAsync(bob.Client, admins, [bob.Id, carol.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BulkAsync(bob.Client, editors, [bob.Id, carol.Id])).StatusCode);
        // An explicit administrator may fill Admins; Manage Groups does not reach it.
        Assert.Equal(HttpStatusCode.Forbidden, (await BulkAsync(owner.Client, admins, [owner.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BulkAsync(alice.Client, admins, [carol.Id])).StatusCode);
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(l => l.Action == "space.group_member_added")));
        Assert.False(Scoped(f, db => db.UserGroups.AnyAsync(ug => ug.GroupId == admins && (ug.UserId == bob.Id || ug.UserId == owner.Id))));

        // The owner cannot see a private space's groups at all, and a member
        // cannot look people up by email.
        await alice.Client.MakePrivateAsync("RIGHTS");
        Assert.Equal(HttpStatusCode.NotFound, (await BulkAsync(owner.Client, editors, [owner.Id])).StatusCode);
        var viewers = await alice.Client.SpaceGroupAsync("RIGHTS", Viewers);
        var report = await (await BulkAsync(alice.Client, viewers, emails: ["someone@example.com"])).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cannot look people up", report.GetProperty("refused")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Bulk_add_to_a_global_group_needs_the_password_and_alerts_for_each()
    {
        using (var stale = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" }))
        {
            var o = await PersonAsync(stale);
            var m = await PersonAsync(stale);
            var refused = await BulkAsync(o.Client, BuiltInGroups.GlobalViewersId, [m.Id]);
            Assert.Contains("reauth_required", await refused.Content.ReadAsStringAsync());
            Assert.False(Scoped(stale, db => db.UserGroups.AnyAsync(ug => ug.GroupId == BuiltInGroups.GlobalViewersId)));
        }

        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var a = await PersonAsync(f);
        var b = await PersonAsync(f);
        Assert.Equal(HttpStatusCode.OK, (await BulkAsync(owner.Client, BuiltInGroups.GlobalReviewersId, [a.Id, b.Id])).StatusCode);
        Assert.Equal(2, Scoped(f, db => db.SecurityAlerts.CountAsync(x => x.Kind == "group.global_member_added")));
    }

    // -- why can this person see this space? ---------------------------------------

    private static async Task<JsonElement> ExplainAsync(HttpClient c, Guid userId, string? space = null, Guid? page = null)
    {
        var res = await c.GetAsync($"/api/access/explain?userId={userId}"
            + (space is null ? "" : "&space=" + space) + (page is null ? "" : "&pageId=" + page));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int? Level(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    /// <summary>
    /// The heart of it: the answer is the permission service's, as that
    /// person, and the reasons given account for exactly that answer.
    /// </summary>
    private static async Task<JsonElement> AgreesAsync(TestAppFactory f, HttpClient asker, Guid userId, Guid spaceId, string key, Guid? page = null)
    {
        var answer = await ExplainAsync(asker, userId, page is null ? key : null, page);
        using var scope = f.Services.CreateScope();
        var perms = scope.ServiceProvider.GetRequiredService<IPermissionService>().AsUser(userId);
        int? expected = await perms.CanAdminSpaceAsync(spaceId) ? Admin
            : await perms.CanEditSpaceAsync(spaceId) ? Edit
            : await perms.CanViewSpaceAsync(spaceId) ? View : null;
        var explicitAdmin = await perms.IsExplicitSpaceAdminAsync(spaceId);
        Assert.Equal(expected, Level(answer.GetProperty("level")));
        Assert.Equal(explicitAdmin, answer.GetProperty("explicitAdmin").GetBoolean());

        var reasons = answer.GetProperty("reasons").EnumerateArray().ToList();
        var explained = reasons.Where(r => r.GetProperty("counts").GetBoolean())
            .Select(r => (int?)r.GetProperty("level").GetInt32()).Max();
        Assert.Equal(expected, explained);
        Assert.Equal(explicitAdmin, reasons.Any(r => r.GetProperty("kind").GetString() is "direct" or "group"
            && r.GetProperty("level").GetInt32() == Admin));

        if (page is { } pageId)
        {
            var p = answer.GetProperty("page");
            Assert.Equal(await perms.CanReadPageAsync(pageId), p.GetProperty("canView").GetBoolean());
            Assert.Equal(await perms.CanEditPageAsync(pageId), p.GetProperty("canEdit").GetBoolean());
            // ...and the restrictions listed account for it.
            var rules = p.GetProperty("restrictions").EnumerateArray().ToList();
            bool Passes(int op) => rules.All(r => r.GetProperty("operation").GetInt32() != op)
                || rules.Any(r => r.GetProperty("operation").GetInt32() == op && r.GetProperty("matches").GetBoolean());
            var view = explained >= View && (explicitAdmin || Passes(View))
                && (!p.GetProperty("draft").GetBoolean() || p.GetProperty("isAuthor").GetBoolean() || explained >= Edit);
            var edit = explained >= Edit && (explicitAdmin || (Passes(View) && Passes(1)));
            Assert.Equal(view, p.GetProperty("canView").GetBoolean());
            Assert.Equal(edit, p.GetProperty("canEdit").GetBoolean());
        }
        return answer;
    }

    private static List<string?> Kinds(JsonElement answer, bool counting = true) => answer.GetProperty("reasons").EnumerateArray()
        .Where(r => r.GetProperty("counts").GetBoolean() == counting).Select(r => r.GetProperty("kind").GetString()).ToList();

    [Fact]
    public async Task An_open_space_is_explained_at_each_level_everyone_gets()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var member = await PersonAsync(f);
        var spaceId = await owner.Client.CreateSpaceAsync("OPEN");

        foreach (var level in new int?[] { View, Edit, Admin, null })
        {
            (await EveryoneAsync(owner.Client, "OPEN", level)).EnsureSuccessStatusCode();
            var answer = await AgreesAsync(f, owner.Client, member.Id, spaceId, "OPEN");
            Assert.Equal(level, Level(answer.GetProperty("level")));
            Assert.Equal(level is null ? [] : ["everyone"], Kinds(answer));
        }
    }

    [Fact]
    public async Task A_private_space_is_explained_through_each_of_its_groups()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var spaceId = await owner.Client.CreateSpaceAsync("PRIV");
        await owner.Client.MakePrivateAsync("PRIV");

        foreach (var (role, level) in new[] { (Viewers, View), (Editors, Edit), (Admins, Admin), (Reviewers, View) })
        {
            var member = await PersonAsync(f);
            (await AddAsync(owner.Client, await owner.Client.SpaceGroupAsync("PRIV", role), member.Id)).EnsureSuccessStatusCode();
            var answer = await AgreesAsync(f, owner.Client, member.Id, spaceId, "PRIV");
            Assert.Equal(level, Level(answer.GetProperty("level")));
            var reason = Assert.Single(answer.GetProperty("reasons").EnumerateArray());
            Assert.Equal(("group", "space"), (reason.GetProperty("kind").GetString(), reason.GetProperty("groupKind").GetString()));
        }

        var stranger = await PersonAsync(f);
        var none = await AgreesAsync(f, owner.Client, stranger.Id, spaceId, "PRIV");
        Assert.Equal(JsonValueKind.Null, none.GetProperty("level").ValueKind);
        Assert.Empty(none.GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public async Task Direct_custom_built_in_and_global_access_are_each_explained()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var direct = await PersonAsync(f);
        var teamMember = await PersonAsync(f);
        var global = await PersonAsync(f);
        var anyone = await PersonAsync(f);
        var spaceId = await owner.Client.CreateSpaceAsync("WAYS");
        await owner.Client.MakePrivateAsync("WAYS");
        var team = await CustomGroupAsync(owner.Client, "Team");
        (await AddAsync(owner.Client, team, teamMember.Id)).EnsureSuccessStatusCode();
        (await owner.Client.PostAsJsonAsync("/api/spaces/WAYS/permissions",
            new { PrincipalType = UserPrincipal, PrincipalId = direct.Id, Operation = Edit })).EnsureSuccessStatusCode();
        (await owner.Client.PostAsJsonAsync("/api/spaces/WAYS/permissions",
            new { PrincipalType = GroupPrincipal, PrincipalId = team, Operation = Edit })).EnsureSuccessStatusCode();
        (await owner.Client.PostAsJsonAsync("/api/spaces/WAYS/permissions",
            new { PrincipalType = GroupPrincipal, PrincipalId = BuiltInGroups.UsersId, Operation = View })).EnsureSuccessStatusCode();
        (await AddAsync(owner.Client, BuiltInGroups.GlobalViewersId, global.Id)).EnsureSuccessStatusCode();

        Assert.Equal(["direct", "group"], Kinds(await AgreesAsync(f, owner.Client, direct.Id, spaceId, "WAYS")));
        var teamAnswer = await AgreesAsync(f, owner.Client, teamMember.Id, spaceId, "WAYS");
        Assert.Equal("custom", teamAnswer.GetProperty("reasons")[0].GetProperty("groupKind").GetString());
        Assert.Equal("builtin", (await AgreesAsync(f, owner.Client, anyone.Id, spaceId, "WAYS")).GetProperty("reasons")[0].GetProperty("groupKind").GetString());
        var globalAnswer = await AgreesAsync(f, owner.Client, global.Id, spaceId, "WAYS");
        Assert.Contains("global", Kinds(globalAnswer));
        Assert.Equal(View, Level(globalAnswer.GetProperty("level")));

        // The owner (an explicit admin, as the creator in Admins) and the
        // administrators' way in: Get Access is offered only to those who
        // are not explicit admins already.
        var mine = await AgreesAsync(f, owner.Client, owner.Id, spaceId, "WAYS");
        Assert.True(mine.GetProperty("explicitAdmin").GetBoolean());
        Assert.False(mine.GetProperty("canRecoverAccess").GetBoolean());
    }

    [Fact]
    public async Task Get_Access_is_named_as_the_reason_an_administrator_is_in_Admins()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var spaceId = await alice.Client.CreateSpaceAsync("LOCKED");
        await alice.Client.MakePrivateAsync("LOCKED");

        // Alice is an explicit admin; the owner, who cannot see it, recovers access.
        (await owner.Client.PostAsync("/api/admin/spaces/LOCKED/recover-access", null)).EnsureSuccessStatusCode();
        var answer = await AgreesAsync(f, owner.Client, owner.Id, spaceId, "LOCKED");
        var admins = answer.GetProperty("reasons").EnumerateArray().Single(r => r.GetProperty("groupKind").GetString() == "space");
        Assert.NotEqual(JsonValueKind.Null, admins.GetProperty("recoveredAt").ValueKind);

        // An administrator who holds Manage spaces but is no explicit admin could.
        var bob = await PersonAsync(f);
        (await owner.Client.PutAsJsonAsync($"/api/admin/users/{bob.Id}/role", new { Role = (int)UserRole.Admin })).EnsureSuccessStatusCode();
        Assert.True((await AgreesAsync(f, owner.Client, bob.Id, spaceId, "LOCKED")).GetProperty("canRecoverAccess").GetBoolean());
    }

    [Fact]
    public async Task A_page_View_restriction_blocks_and_allows_and_is_explained()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var reader = await PersonAsync(f);
        var spaceId = await owner.Client.CreateSpaceAsync("RESTR");
        await owner.Client.MakePrivateAsync("RESTR");
        var viewers = await owner.Client.SpaceGroupAsync("RESTR", Viewers);
        var editors = await owner.Client.SpaceGroupAsync("RESTR", Editors);
        (await AddAsync(owner.Client, editors, reader.Id)).EnsureSuccessStatusCode();
        var parent = await PageAsync(owner.Client, spaceId, "Parent");
        var child = await PageAsync(owner.Client, spaceId, "Child", parent);

        // Unrestricted: they read and edit it.
        var open = await AgreesAsync(f, owner.Client, reader.Id, spaceId, "RESTR", child);
        Assert.True(open.GetProperty("page").GetProperty("canEdit").GetBoolean());

        // A View restriction on the parent naming only the owner blocks them.
        (await owner.Client.PostAsJsonAsync($"/api/pages/{parent}/restrictions",
            new { PrincipalType = UserPrincipal, PrincipalId = owner.Id, Operation = View })).EnsureSuccessStatusCode();
        var blocked = await AgreesAsync(f, owner.Client, reader.Id, spaceId, "RESTR", child);
        Assert.False(blocked.GetProperty("page").GetProperty("canView").GetBoolean());
        var rule = Assert.Single(blocked.GetProperty("page").GetProperty("restrictions").EnumerateArray());
        Assert.True(rule.GetProperty("inherited").GetBoolean());
        Assert.False(rule.GetProperty("matches").GetBoolean());
        // The owner, an explicit admin, passes regardless.
        var mine = await AgreesAsync(f, owner.Client, owner.Id, spaceId, "RESTR", child);
        Assert.True(mine.GetProperty("page").GetProperty("adminBypass").GetBoolean());

        // Adding their group to it lets them back in; an Edit restriction
        // naming only the Viewers group then keeps them from editing.
        (await owner.Client.PostAsJsonAsync($"/api/pages/{parent}/restrictions",
            new { PrincipalType = GroupPrincipal, PrincipalId = editors, Operation = View })).EnsureSuccessStatusCode();
        var allowed = await AgreesAsync(f, owner.Client, reader.Id, spaceId, "RESTR", child);
        Assert.True(allowed.GetProperty("page").GetProperty("canView").GetBoolean());
        (await owner.Client.PostAsJsonAsync($"/api/pages/{child}/restrictions",
            new { PrincipalType = GroupPrincipal, PrincipalId = viewers, Operation = Edit })).EnsureSuccessStatusCode();
        var readOnly = await AgreesAsync(f, owner.Client, reader.Id, spaceId, "RESTR", child);
        Assert.True(readOnly.GetProperty("page").GetProperty("canView").GetBoolean());
        Assert.False(readOnly.GetProperty("page").GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task A_suspended_account_is_explained_as_the_check_sees_it()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var member = await PersonAsync(f);
        var global = await PersonAsync(f);
        var spaceId = await owner.Client.CreateSpaceAsync("SUSP");
        (await EveryoneAsync(owner.Client, "SUSP", Edit)).EnsureSuccessStatusCode();
        (await AddAsync(owner.Client, await owner.Client.SpaceGroupAsync("SUSP", Viewers), member.Id)).EnsureSuccessStatusCode();
        (await AddAsync(owner.Client, BuiltInGroups.GlobalViewersId, global.Id)).EnsureSuccessStatusCode();
        Suspend(f, member.Id);
        Suspend(f, global.Id);

        // What everyone gets no longer counts; the stored group still does,
        // as the check has it (they cannot sign in to use it).
        var answer = await AgreesAsync(f, owner.Client, member.Id, spaceId, "SUSP");
        Assert.False(answer.GetProperty("person").GetProperty("active").GetBoolean());
        Assert.Equal(["group"], Kinds(answer));
        Assert.Equal(["everyone"], Kinds(answer, counting: false));
        var reader = await AgreesAsync(f, owner.Client, global.Id, spaceId, "SUSP");
        Assert.Equal(JsonValueKind.Null, reader.GetProperty("level").ValueKind);
        Assert.Equal(["everyone", "global"], Kinds(reader, counting: false));
    }

    [Fact]
    public async Task Asking_about_a_space_or_page_you_cannot_see_is_not_found()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var spaceId = await alice.Client.CreateSpaceAsync("HIDDEN");
        await alice.Client.MakePrivateAsync("HIDDEN");
        var page = await PageAsync(alice.Client, spaceId);

        // Not even an administrator with every right, who cannot see it.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/access/explain?userId={bob.Id}&space=HIDDEN")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/access/explain?userId={bob.Id}&pageId={page}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/access/explain?userId={bob.Id}&space=NOSUCH")).StatusCode);

        // A page restricted from the asker, in a space they can see.
        var openId = await alice.Client.CreateSpaceAsync("SHOWN");
        var restricted = await PageAsync(alice.Client, openId, "Private");
        (await alice.Client.PostAsJsonAsync($"/api/pages/{restricted}/restrictions",
            new { PrincipalType = UserPrincipal, PrincipalId = alice.Id, Operation = View })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/access/explain?userId={bob.Id}&pageId={restricted}")).StatusCode);

        // Seeing a space is not enough: a member who may only view it is
        // refused; one who administers it may ask.
        (await EveryoneAsync(alice.Client, "SHOWN", View)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.GetAsync($"/api/access/explain?userId={owner.Id}&space=SHOWN")).StatusCode);
        await AgreesAsync(f, alice.Client, bob.Id, openId, "SHOWN");
        // The owner sees users and spaces in Administration, so may ask about
        // a space he can see without administering it.
        await AgreesAsync(f, owner.Client, bob.Id, openId, "SHOWN");
    }
}
