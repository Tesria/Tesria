using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Groups for every space (dev-plan 21.1): who may change them, where they
/// may be named, who may see them, Global Viewers, what everyone signed in
/// gets, and the last-admin rule on every way to lose admin. The review's
/// essential tests 2 to 8 and 11; 1, 9 and 10 are in
/// <see cref="PermissionMigrationTests"/> and <see cref="DatabaseRoleTests"/>.
/// </summary>
public class SpaceGroupTests
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

    private static async Task<Guid> SpaceAsync(Person p, string key)
    {
        var id = await p.Client.CreateSpaceAsync(key);
        return id;
    }

    private static async Task<Guid> PageAsync(HttpClient c, Guid spaceId, string title = "Page")
    {
        var res = await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient c, Guid group, Guid user) =>
        c.PostAsJsonAsync($"/api/groups/{group}/members", new { UserId = user });

    private static Task<HttpResponseMessage> EveryoneAsync(HttpClient c, string key, int? access) =>
        c.PutAsJsonAsync($"/api/spaces/{key}/permissions/everyone", new { Access = access });

    /// <summary>Every session is past the sudo window, so a sudo action is refused until the password.</summary>
    private static TestAppFactory NoSudoWindow() => new(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" });

    private static T Scoped<T>(TestAppFactory f, Func<AppDbContext, Task<T>> read)
    {
        using var scope = f.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>()).GetAwaiter().GetResult();
    }

    // -- creating a space ------------------------------------------------------

    [Fact]
    public async Task A_new_space_has_its_four_groups_its_creator_in_Admins_and_stays_open()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        (await alice.Client.PostAsJsonAsync("/api/spaces", new { Key = "NEW", Name = "Handbook" })).EnsureSuccessStatusCode();

        var access = await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/NEW/permissions");
        Assert.Equal(Admin, access.GetProperty("everyoneAccess").GetInt32());
        Assert.True(access.GetProperty("canManageAdmins").GetBoolean());
        var groups = access.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal([Admins, Editors, Viewers, Reviewers], groups.Select(g => g.GetProperty("role").GetInt32()));
        Assert.Equal(["Handbook Admins", "Handbook Editors", "Handbook Viewers", "Handbook Reviewers"],
            groups.Select(g => g.GetProperty("name").GetString()));
        Assert.Equal([Admin, Edit, View, View], groups.Select(g => g.GetProperty("operation").GetInt32()));
        Assert.Equal([alice.Id], groups[0].GetProperty("members").EnumerateArray().Select(m => m.GetProperty("userId").GetGuid()));
        Assert.Empty(access.GetProperty("grants").EnumerateArray());

        // As open as a new space has always been: Bob may administer it, but
        // he is not one of its explicit administrators.
        var asBob = await bob.Client.GetFromJsonAsync<JsonElement>("/api/spaces/NEW/permissions");
        Assert.False(asBob.GetProperty("canManageAdmins").GetBoolean());
    }

    // -- essential test 2: who manages a space's groups ------------------------

    [Fact]
    public async Task In_an_open_space_nobody_makes_themselves_an_explicit_admin()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f); // the instance owner, who holds Manage Groups
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var carol = await PersonAsync(f);
        await SpaceAsync(alice, "OPENA");
        var admins = await alice.Client.SpaceGroupAsync("OPENA", Admins);
        var editors = await alice.Client.SpaceGroupAsync("OPENA", Editors);

        // Bob may administer the open space, but not choose its administrators:
        // not through its Admins group, and not by a grant of Admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(bob.Client, admins, bob.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.PostAsJsonAsync("/api/spaces/OPENA/permissions",
            new { PrincipalType = UserPrincipal, PrincipalId = bob.Id, Operation = Admin })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.DeleteAsync($"/api/groups/{admins}/members/{alice.Id}")).StatusCode);
        // The other three are his to manage, as an administrator of the space.
        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(bob.Client, editors, carol.Id)).StatusCode);

        // An explicit administrator may.
        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(alice.Client, admins, carol.Id)).StatusCode);

        // Manage Groups does not reach a space's groups: not their names, not
        // their existence, and not Admins in a space its holder only
        // administers because it is open.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PutAsJsonAsync($"/api/groups/{editors}", new { Name = "Mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.DeleteAsync($"/api/groups/{editors}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(owner.Client, admins, owner.Id)).StatusCode);
        await alice.Client.MakePrivateAsync("OPENA");
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(owner.Client, editors, owner.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(owner.Client, admins, owner.Id)).StatusCode);
        Assert.False(Scoped(f, db => db.UserGroups.AnyAsync(ug => ug.UserId == owner.Id || ug.UserId == bob.Id)));
    }

    [Fact]
    public async Task One_spaces_administrator_cannot_touch_another_spaces_groups()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var carol = await PersonAsync(f);
        await SpaceAsync(alice, "AAA");
        await SpaceAsync(carol, "BBB");
        await carol.Client.MakePrivateAsync("BBB");
        var theirViewers = await carol.Client.SpaceGroupAsync("BBB", Viewers);
        var theirAdmins = await carol.Client.SpaceGroupAsync("BBB", Admins);

        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(alice.Client, theirViewers, alice.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(alice.Client, theirAdmins, alice.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.DeleteAsync($"/api/groups/{theirAdmins}/members/{carol.Id}")).StatusCode);
    }

    // -- essential test 3: a space's groups only in their own space ------------

    [Fact]
    public async Task A_spaces_group_is_named_only_in_its_own_space()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        await SpaceAsync(alice, "HOME");
        var other = await SpaceAsync(alice, "AWAY");
        var homeViewers = await alice.Client.SpaceGroupAsync("HOME", Viewers);
        var awayPage = await PageAsync(alice.Client, other);
        var homePage = await PageAsync(alice.Client, Scoped(f, db => db.Spaces.Where(s => s.Key == "HOME").Select(s => s.Id).SingleAsync()));

        var grant = await alice.Client.PostAsJsonAsync("/api/spaces/AWAY/permissions",
            new { PrincipalType = GroupPrincipal, PrincipalId = homeViewers, Operation = View });
        Assert.Equal(HttpStatusCode.BadRequest, grant.StatusCode);
        Assert.Contains("another space", await grant.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Client.PostAsJsonAsync($"/api/pages/{awayPage}/restrictions",
            new { PrincipalType = GroupPrincipal, PrincipalId = homeViewers, Operation = View })).StatusCode);

        // In its own space it may be named in a restriction; a grant would only
        // repeat the access it already has.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PostAsJsonAsync($"/api/pages/{homePage}/restrictions",
            new { PrincipalType = GroupPrincipal, PrincipalId = homeViewers, Operation = View })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.Client.PostAsJsonAsync("/api/spaces/HOME/permissions",
            new { PrincipalType = GroupPrincipal, PrincipalId = homeViewers, Operation = Edit })).StatusCode);
    }

    [Fact]
    public async Task A_spaces_groups_keep_their_access()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "FIXED");
        var locked = Scoped(f, db => db.SpacePermissions
            .Where(p => p.SpaceId == spaceId && p.PrincipalType == PrincipalType.Group).Select(p => p.Id).ToListAsync());
        Assert.Equal(4, locked.Count);
        foreach (var id in locked)
            Assert.Equal(HttpStatusCode.Conflict, (await alice.Client.DeleteAsync($"/api/spaces/FIXED/permissions/{id}")).StatusCode);
        Assert.Equal(4, Scoped(f, db => db.SpacePermissions.CountAsync(p => p.SpaceId == spaceId)));
    }

    // -- essential test 4: hidden from people who cannot see the space --------

    [Fact]
    public async Task A_private_spaces_groups_are_hidden_from_people_who_cannot_see_it()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "HUSH");
        await alice.Client.MakePrivateAsync("HUSH");
        var viewers = await alice.Client.SpaceGroupAsync("HUSH", Viewers);

        var listed = await bob.Client.GetFromJsonAsync<List<JsonElement>>("/api/groups");
        Assert.DoesNotContain(listed!, g => g.GetProperty("spaceId").ValueKind != JsonValueKind.Null
            && g.GetProperty("spaceId").GetGuid() == spaceId);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/groups/{viewers}/members")).StatusCode);

        // Alice sees them, named after the space.
        var hers = await alice.Client.GetFromJsonAsync<List<JsonElement>>("/api/groups");
        Assert.Equal(4, hers!.Count(g => g.GetProperty("spaceKey").GetString() == "HUSH"));

        // Once everyone signed in may view it, so may Bob see its groups.
        (await EveryoneAsync(alice.Client, "HUSH", View)).EnsureSuccessStatusCode();
        listed = await bob.Client.GetFromJsonAsync<List<JsonElement>>("/api/groups");
        Assert.Equal(4, listed!.Count(g => g.GetProperty("spaceKey").GetString() == "HUSH"));
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.GetAsync($"/api/groups/{viewers}/members")).StatusCode);
    }

    // -- essential test 5: Global Viewers --------------------------------------

    [Fact]
    public async Task Global_Viewers_read_every_space_but_restrictions_and_drafts_still_bind_them()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var reader = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "INNER");
        await alice.Client.MakePrivateAsync("INNER");
        var open = await PageAsync(alice.Client, spaceId, "Open");
        var restricted = await PageAsync(alice.Client, spaceId, "Restricted");
        (await alice.Client.PostAsJsonAsync($"/api/pages/{restricted}/restrictions",
            new { PrincipalType = UserPrincipal, PrincipalId = alice.Id, Operation = View })).EnsureSuccessStatusCode();
        var draft = (await (await alice.Client.PostAsJsonAsync("/api/pages/draft", new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var archivedId = await SpaceAsync(alice, "OLDER");
        await alice.Client.MakePrivateAsync("OLDER");
        (await alice.Client.PostAsync("/api/spaces/OLDER/archive", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await reader.Client.GetAsync("/api/spaces/INNER")).StatusCode);
        (await AddAsync(owner.Client, BuiltInGroups.GlobalViewersId, reader.Id)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync("/api/spaces/INNER")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync($"/api/pages/{open}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync("/api/spaces/OLDER")).StatusCode);
        var all = await reader.Client.GetFromJsonAsync<List<JsonElement>>("/api/spaces?includeArchived=true");
        Assert.Contains(all!, s => s.GetProperty("id").GetGuid() == archivedId);
        // An implicit View: restrictions bind, drafts stay hidden, nothing is editable.
        Assert.Equal(HttpStatusCode.NotFound, (await reader.Client.GetAsync($"/api/pages/{restricted}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.Client.GetAsync($"/api/pages/{draft}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.PutAsJsonAsync($"/api/pages/{open}",
            new { Title = "Changed", ContentJson = Doc, ChangeComment = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.GetAsync("/api/spaces/INNER/permissions")).StatusCode);

        // The space's Permissions tab says so.
        var access = await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/INNER/permissions");
        Assert.Equal(1, access.GetProperty("globalViewers").GetInt32());

        // Suspended, they read nothing.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var perms = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            (await db.Users.SingleAsync(u => u.Id == reader.Id)).Status = UserStatus.Suspended;
            await db.SaveChangesAsync();
            Assert.False(await perms.AsUser(reader.Id).CanViewSpaceAsync(spaceId));
        }
    }

    [Fact]
    public async Task Choosing_a_Global_Viewer_needs_the_password_and_is_audited_and_alerted()
    {
        // Outside the sudo window: refused, and nothing written.
        using (var stale = NoSudoWindow())
        {
            var o = await PersonAsync(stale);
            var m = await PersonAsync(stale);
            var refused = await AddAsync(o.Client, BuiltInGroups.GlobalViewersId, m.Id);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("reauth_required", await refused.Content.ReadAsStringAsync());
            Assert.False(Scoped(stale, db => db.UserGroups.AnyAsync(ug => ug.GroupId == BuiltInGroups.GlobalViewersId)));
            // Taking someone out is sudo too.
            Assert.Contains("reauth_required", await (await o.Client.DeleteAsync(
                $"/api/groups/{BuiltInGroups.GlobalViewersId}/members/{m.Id}")).Content.ReadAsStringAsync());
        }

        // Inside it: done, audited and alerted.
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var member = await PersonAsync(f);
        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(owner.Client, BuiltInGroups.GlobalViewersId, member.Id)).StatusCode);

        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "group.member_added" && a.TargetId == BuiltInGroups.GlobalViewersId)));
        Assert.True(Scoped(f, db => db.SecurityAlerts.AnyAsync(a => a.Kind == "group.global_member_added")));

        // Not anyone's to choose without Manage Groups, and not renamed or deleted by anyone.
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(member.Client, BuiltInGroups.GlobalReviewersId, member.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.DeleteAsync($"/api/groups/{BuiltInGroups.GlobalViewersId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PutAsJsonAsync($"/api/groups/{BuiltInGroups.GlobalViewersId}", new { Name = "Readers" })).StatusCode);
    }

    // -- EveryoneAccess ---------------------------------------------------------

    [Fact]
    public async Task What_everyone_signed_in_gets_is_a_setting_with_make_opens_protections()
    {
        // Outside the sudo window, narrowing needs no password and widening does.
        using (var stale = NoSudoWindow())
        {
            var a = await PersonAsync(stale);
            await SpaceAsync(a, "STALE");
            Assert.Equal(HttpStatusCode.NoContent, (await EveryoneAsync(a.Client, "STALE", null)).StatusCode);
            var refused = await EveryoneAsync(a.Client, "STALE", View);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("reauth_required", await refused.Content.ReadAsStringAsync());
            Assert.Contains("reauth_required", await (await a.Client.DeleteAsync("/api/spaces/STALE/permissions")).Content.ReadAsStringAsync());
            Assert.Null(Scoped(stale, db => db.Spaces.Where(s => s.Key == "STALE").Select(s => s.EveryoneAccess).SingleAsync()));
        }

        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "LEVEL");
        var page = await PageAsync(alice.Client, spaceId);
        Assert.Equal(HttpStatusCode.NoContent, (await EveryoneAsync(alice.Client, "LEVEL", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync("/api/spaces/LEVEL")).StatusCode);

        // Widening is audited and alerted, as make-open was.
        Assert.Equal(HttpStatusCode.NoContent, (await EveryoneAsync(alice.Client, "LEVEL", View)).StatusCode);
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "space.opened" && a.TargetId == spaceId)));
        Assert.True(Scoped(f, db => db.SecurityAlerts.AnyAsync(a => a.Kind == "space.opened")));

        // View is view.
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.GetAsync($"/api/pages/{page}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.PutAsJsonAsync($"/api/pages/{page}",
            new { Title = "Changed", ContentJson = Doc, ChangeComment = (string?)null })).StatusCode);

        // The old make-open route is now "everyone may administer".
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync("/api/spaces/LEVEL/permissions")).StatusCode);
        Assert.Equal(SpaceOperation.Admin, Scoped(f, db => db.Spaces.Where(s => s.Id == spaceId).Select(s => s.EveryoneAccess).SingleAsync()));
        // Its creator is still in Admins: it removed nothing.
        Assert.True((await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/LEVEL/permissions")).GetProperty("canManageAdmins").GetBoolean());
    }

    // -- essential test 6: the last-admin rule on every path --------------------

    [Fact]
    public async Task Lowering_what_everyone_gets_is_refused_to_someone_it_would_leave_without_admin()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await SpaceAsync(alice, "DROP");

        // Bob administers only because everyone may.
        var refused = await EveryoneAsync(bob.Client, "DROP", Edit);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("no longer administer", await refused.Content.ReadAsStringAsync());

        // Nor may Alice, once she has left Admins (which the open space allowed).
        var admins = await alice.Client.SpaceGroupAsync("DROP", Admins);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/groups/{admins}/members/{alice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await EveryoneAsync(alice.Client, "DROP", null)).StatusCode);
    }

    [Fact]
    public async Task Revoking_the_last_Admin_grant_is_refused()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await SpaceAsync(alice, "GRANTS");
        await alice.Client.MakePrivateAsync("GRANTS");
        (await alice.Client.PostAsJsonAsync("/api/spaces/GRANTS/permissions",
            new { PrincipalType = UserPrincipal, PrincipalId = bob.Id, Operation = Admin })).EnsureSuccessStatusCode();
        var admins = await alice.Client.SpaceGroupAsync("GRANTS", Admins);
        (await alice.Client.DeleteAsync($"/api/groups/{admins}/members/{alice.Id}")).EnsureSuccessStatusCode();

        var access = await bob.Client.GetFromJsonAsync<JsonElement>("/api/spaces/GRANTS/permissions");
        var his = access.GetProperty("grants").EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await bob.Client.DeleteAsync($"/api/spaces/GRANTS/permissions/{his}")).StatusCode);
    }

    [Fact]
    public async Task A_suspended_administrator_does_not_count()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var dave = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "AWAYS");
        await alice.Client.MakePrivateAsync("AWAYS");
        var admins = await alice.Client.SpaceGroupAsync("AWAYS", Admins);
        (await AddAsync(alice.Client, admins, dave.Id)).EnsureSuccessStatusCode();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == dave.Id)).Status = UserStatus.Suspended;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await alice.Client.DeleteAsync($"/api/groups/{admins}/members/{alice.Id}")).StatusCode);
        // And the tab says who is suspended.
        var access = await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/AWAYS/permissions");
        var members = access.GetProperty("groups")[0].GetProperty("members").EnumerateArray().ToList();
        Assert.False(members.Single(m => m.GetProperty("userId").GetGuid() == dave.Id).GetProperty("active").GetBoolean());
        _ = spaceId;
    }

    // -- essential test 7: live editing is told ---------------------------------

    [Fact]
    public async Task Changing_what_everyone_gets_ends_that_spaces_live_editing()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "LIVELY");
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var space = await db.Spaces.SingleAsync(s => s.Id == spaceId);

        space.Name = "Renamed";
        Assert.Empty(CollabRevocationInterceptor.RevocationsIn(db));
        space.EveryoneAccess = SpaceOperation.View;
        Assert.Contains(new CollabRevocation(SpaceId: spaceId), CollabRevocationInterceptor.RevocationsIn(db));
    }

    // -- essential test 8: packs -------------------------------------------------

    [Fact]
    public async Task A_packs_manifest_says_whether_the_space_was_restricted()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var openId = await SpaceAsync(alice, "OPENPACK");
        var closedId = await SpaceAsync(alice, "SHUTPACK");
        await PageAsync(alice.Client, openId);
        await PageAsync(alice.Client, closedId);
        await alice.Client.MakePrivateAsync("SHUTPACK");
        // A member of a space group of an open space is not a restriction.
        (await AddAsync(alice.Client, await alice.Client.SpaceGroupAsync("OPENPACK", Viewers), bob.Id)).EnsureSuccessStatusCode();

        static async Task<Features.Export.WikiPack.Model> Pack(HttpClient c, string key)
        {
            var res = await c.GetAsync($"/api/spaces/{key}/export/pack");
            res.EnsureSuccessStatusCode();
            return Features.Export.WikiPack.Read(new MemoryStream(await res.Content.ReadAsByteArrayAsync()));
        }

        Assert.Equal(0, (await Pack(alice.Client, "OPENPACK")).Manifest.Restrictions.Space);
        Assert.True((await Pack(alice.Client, "SHUTPACK")).Manifest.Restrictions.Space > 0);

        // A grant beyond its own groups makes even an open space restricted,
        // as any grant did before.
        (await alice.Client.PostAsJsonAsync("/api/spaces/OPENPACK/permissions",
            new { PrincipalType = UserPrincipal, PrincipalId = bob.Id, Operation = Edit })).EnsureSuccessStatusCode();
        Assert.True((await Pack(alice.Client, "OPENPACK")).Manifest.Restrictions.Space > 0);
    }

    // -- essential test 11: recover-access ---------------------------------------

    [Fact]
    public async Task Recovering_access_joins_the_spaces_Admins_once_and_never_in_an_open_space()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f); // an administrator, with spaces.manage
        var alice = await PersonAsync(f);
        var openId = await SpaceAsync(alice, "OPENREC");
        var closedId = await SpaceAsync(alice, "SHUTREC");
        await alice.Client.MakePrivateAsync("SHUTREC");

        var open = await (await owner.Client.PostAsync("/api/admin/spaces/OPENREC/recover-access", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(open.GetProperty("alreadyHadAccess").GetBoolean());
        Assert.False(Scoped(f, db => db.UserGroups.AnyAsync(ug => ug.UserId == owner.Id && ug.Group!.SpaceId == openId)));

        for (var i = 0; i < 2; i++)
            (await owner.Client.PostAsync("/api/admin/spaces/SHUTREC/recover-access", null)).EnsureSuccessStatusCode();
        Assert.Equal(1, Scoped(f, db => db.UserGroups.CountAsync(ug => ug.UserId == owner.Id
            && ug.Group!.SpaceId == closedId && ug.Group.SpaceRole == SpaceGroupRole.Admins)));
        Assert.Equal(1, Scoped(f, db => db.AuditLogs.CountAsync(a => a.Action == "space.access_recovered")));
        Assert.True((await owner.Client.GetFromJsonAsync<JsonElement>("/api/spaces/SHUTREC/permissions")).GetProperty("canManageAdmins").GetBoolean());
    }

    [Fact]
    public async Task An_open_space_nobody_administers_by_name_can_be_recovered()
    {
        // Every space that was open before 21.1 starts with an empty Admins
        // group, and nobody may add to it; without this it could never be
        // closed. Recovering puts the administrator in Admins, audited.
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var spaceId = await SpaceAsync(alice, "ORPHAN");
        var admins = await alice.Client.SpaceGroupAsync("ORPHAN", Admins);
        // Allowed: everyone signed in still administers it.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/api/groups/{admins}/members/{alice.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await EveryoneAsync(alice.Client, "ORPHAN", null)).StatusCode);

        var res = await (await owner.Client.PostAsync("/api/admin/spaces/ORPHAN/recover-access", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(res.GetProperty("alreadyHadAccess").GetBoolean());
        Assert.True(Scoped(f, db => db.UserGroups.AnyAsync(ug => ug.UserId == owner.Id && ug.GroupId == admins)));
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "space.access_recovered" && a.TargetId == spaceId)));
        // Now the space can be given administrators, and closed.
        Assert.Equal(HttpStatusCode.NoContent, (await AddAsync(owner.Client, admins, alice.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await EveryoneAsync(alice.Client, "ORPHAN", null)).StatusCode);
    }
}
