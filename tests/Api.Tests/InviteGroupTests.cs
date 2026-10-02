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
/// Inviting someone with a role and groups (dev-plan 21.3): what an invite
/// may carry is checked when it is made, by the rules for doing the same
/// directly, and again when the account is made, by the inviter's rights at
/// that moment.
/// </summary>
public class InviteGroupTests
{
    private const int Viewers = 0, Editors = 1, Admins = 2, Reviewers = 3;
    private const int Member = 0, AdminTier = 1;

    private sealed record Person(HttpClient Client, Guid Id);
    private record Issued(string Token, string Path, string? Email);

    private static async Task<Person> PersonAsync(TestAppFactory f)
    {
        var c = f.CreateClient();
        return new Person(c, await c.RegisterAndSignInAsync());
    }

    private static T Scoped<T>(TestAppFactory f, Func<AppDbContext, Task<T>> read)
    {
        using var scope = f.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>()).GetAwaiter().GetResult();
    }

    /// <summary>Gives users (the Member tier's built-in role) a right they do not hold by default.</summary>
    private static async Task GrantUsersAsync(TestAppFactory f, string key)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.Key == Role.Keys.For(UserRole.Member));
        if (role.Permissions.All(p => p.Key != key)) role.Permissions.Add(new RolePermission { RoleId = role.Id, Key = key });
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<PermissionCache>().Invalidate();
    }

    private static async Task MakeAdminAsync(TestAppFactory f, Guid userId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Users.FirstAsync(u => u.Id == userId);
        row.Role = UserRole.Admin;
        row.RoleId = await RoleSeed.BuiltInIdAsync(db, UserRole.Admin);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<PermissionCache>().Invalidate();
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient c, string? email, int? role = null, params Guid[] groups) =>
        c.PostAsJsonAsync("/api/admin/invites", new { Email = email, Role = role, GroupIds = groups });

    private static async Task<(HttpClient Client, Guid Id, int Role)> RegisterAsync(TestAppFactory f, string email, string token)
    {
        var c = f.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = "Newcomer", Password = "supersecret", InviteToken = token });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (c, body.GetProperty("id").GetGuid(), body.GetProperty("role").GetInt32());
    }

    private static bool InGroup(TestAppFactory f, Guid group, Guid user) =>
        Scoped(f, db => db.UserGroups.AnyAsync(ug => ug.GroupId == group && ug.UserId == user));

    [Fact]
    public async Task An_invite_puts_the_new_account_in_its_groups_when_it_registers()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("HB");
        await owner.Client.MakePrivateAsync("HB");
        await owner.Client.CreateSpaceAsync("ENG");
        var hbEditors = await owner.Client.SpaceGroupAsync("HB", Editors);
        var engReviewers = await owner.Client.SpaceGroupAsync("ENG", Reviewers);

        var res = await InviteAsync(owner.Client, "new@example.com", null, hbEditors, engReviewers, BuiltInGroups.GlobalViewersId);
        res.EnsureSuccessStatusCode();
        var issued = await res.Content.ReadFromJsonAsync<Issued>();

        // The list shows what is waiting, the global group first.
        var listed = (await owner.Client.GetFromJsonAsync<JsonElement>("/api/admin/invites")).EnumerateArray().Single();
        Assert.Equal(Member, listed.GetProperty("role").GetInt32());
        var names = listed.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()!).ToList();
        Assert.Equal(3, names.Count);
        Assert.Equal("Global Viewers", names[0]);
        Assert.Contains(names, n => n.EndsWith(" Editors"));
        Assert.Contains(names, n => n.EndsWith(" Reviewers"));

        var (client, id, role) = await RegisterAsync(f, "new@example.com", issued!.Token);
        Assert.Equal(Member, role);
        Assert.True(InGroup(f, hbEditors, id));
        Assert.True(InGroup(f, engReviewers, id));
        Assert.True(InGroup(f, BuiltInGroups.GlobalViewersId, id));
        // And it works: the private space is theirs to edit.
        var hb = await client.GetFromJsonAsync<JsonElement>("/api/spaces/HB");
        Assert.True(hb.GetProperty("canEdit").GetBoolean());

        // Each assignment is audited as the inviter's, and the global one alerts.
        Assert.Equal(2, Scoped(f, db => db.AuditLogs.CountAsync(a => a.Action == "space.group_member_added" && a.ActorId == owner.Id
            && a.MetadataJson!.Contains("invite"))));
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "group.member_added" && a.TargetId == BuiltInGroups.GlobalViewersId)));
        Assert.True(Scoped(f, db => db.SecurityAlerts.AnyAsync(a => a.Kind == "group.global_member_added")));
    }

    [Fact]
    public async Task An_invite_can_make_an_administrator_only_for_someone_who_may_promote()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var admin = await PersonAsync(f);
        await MakeAdminAsync(f, admin.Id);

        // An administrator without Promote to administrator (not theirs by default).
        var refused = await InviteAsync(admin.Client, "boss@example.com", AdminTier);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("permission_required", await refused.Content.ReadAsStringAsync());
        // Nobody becomes the owner by invite, and an administrator's invite names its address.
        Assert.Equal(HttpStatusCode.BadRequest, (await InviteAsync(owner.Client, "boss@example.com", 2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await InviteAsync(owner.Client, null, AdminTier)).StatusCode);
        Assert.Equal(0, Scoped(f, db => db.Invites.CountAsync()));

        var issued = await (await InviteAsync(owner.Client, "boss@example.com", AdminTier)).Content.ReadFromJsonAsync<Issued>();
        var (_, id, role) = await RegisterAsync(f, "boss@example.com", issued!.Token);
        Assert.Equal(AdminTier, role);
        Assert.Equal(UserRole.Admin, Scoped(f, db => db.Users.Where(u => u.Id == id).Select(u => u.Role).FirstAsync()));
        Assert.True(Scoped(f, db => db.SecurityAlerts.AnyAsync(a => a.Kind == "admin.promoted")));
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "user.role_changed" && a.TargetId == id)));
    }

    [Fact]
    public async Task What_the_inviter_can_no_longer_give_is_skipped_and_recorded()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await GrantUsersAsync(f, InstancePermissions.InvitesCreate);
        await alice.Client.CreateSpaceAsync("TEAM");
        await alice.Client.MakePrivateAsync("TEAM");
        var admins = await alice.Client.SpaceGroupAsync("TEAM", Admins);
        var editors = await alice.Client.SpaceGroupAsync("TEAM", Editors);
        await owner.Client.CreateSpaceAsync("KEEP");
        var keepViewers = await owner.Client.SpaceGroupAsync("KEEP", Viewers);

        var fromAlice = await (await InviteAsync(alice.Client, "late@example.com", null, editors)).Content.ReadFromJsonAsync<Issued>();
        // The owner's invite: administrator and a global group, then the owner keeps both rights.
        var fromOwner = await (await InviteAsync(owner.Client, "kept@example.com", AdminTier, keepViewers, BuiltInGroups.GlobalReviewersId))
            .Content.ReadFromJsonAsync<Issued>();

        // Alice hands the space to Bob and leaves its Admins.
        (await alice.Client.PostAsJsonAsync($"/api/groups/{admins}/members", new { UserId = bob.Id })).EnsureSuccessStatusCode();
        (await bob.Client.DeleteAsync($"/api/groups/{admins}/members/{alice.Id}")).EnsureSuccessStatusCode();

        var (_, late, _) = await RegisterAsync(f, "late@example.com", fromAlice!.Token);
        Assert.False(InGroup(f, editors, late));
        var skipped = Scoped(f, db => db.AuditLogs.Where(a => a.Action == "invite.assignment_skipped" && a.TargetId == late).ToListAsync());
        Assert.Single(skipped);
        Assert.Contains(" Editors", skipped[0].MetadataJson); // the group by name, "Space n Editors"
        Assert.Contains("no longer administers", skipped[0].MetadataJson);

        // The other invite lands whole.
        var (_, kept, role) = await RegisterAsync(f, "kept@example.com", fromOwner!.Token);
        Assert.Equal(AdminTier, role);
        Assert.True(InGroup(f, keepViewers, kept));
        Assert.True(InGroup(f, BuiltInGroups.GlobalReviewersId, kept));
        Assert.False(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "invite.assignment_skipped" && a.TargetId == kept)));
    }

    [Fact]
    public async Task A_suspended_inviter_gives_nothing()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        await GrantUsersAsync(f, InstancePermissions.InvitesCreate);
        await alice.Client.CreateSpaceAsync("SUSP");
        var viewers = await alice.Client.SpaceGroupAsync("SUSP", Viewers);
        var issued = await (await InviteAsync(alice.Client, "x@example.com", null, viewers)).Content.ReadFromJsonAsync<Issued>();

        (await owner.Client.PutAsJsonAsync($"/api/admin/users/{alice.Id}/status", new { Status = 1 })).EnsureSuccessStatusCode();
        var (_, id, _) = await RegisterAsync(f, "x@example.com", issued!.Token);
        Assert.False(InGroup(f, viewers, id));
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "invite.assignment_skipped" && a.TargetId == id)));
    }

    [Fact]
    public async Task A_global_group_or_an_administrator_needs_the_password_again_at_invite_time()
    {
        using var f = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" });
        var owner = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("PLAIN");
        var viewers = await owner.Client.SpaceGroupAsync("PLAIN", Viewers);

        foreach (var res in new[]
        {
            await InviteAsync(owner.Client, "g@example.com", null, BuiltInGroups.GlobalViewersId),
            await InviteAsync(owner.Client, "g@example.com", null, BuiltInGroups.GlobalReviewersId),
            await InviteAsync(owner.Client, "g@example.com", AdminTier),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Contains("reauth_required", await res.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, Scoped(f, db => db.Invites.CountAsync()));

        // A space's group, or none, needs no more than the right to invite.
        (await InviteAsync(owner.Client, null, null, viewers)).EnsureSuccessStatusCode();

        // Global groups need an address too, and Manage Groups.
        using var fresh = new TestAppFactory();
        var o = await PersonAsync(fresh);
        var m = await PersonAsync(fresh);
        await GrantUsersAsync(fresh, InstancePermissions.InvitesCreate);
        Assert.Equal(HttpStatusCode.BadRequest, (await InviteAsync(o.Client, null, null, BuiltInGroups.GlobalViewersId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await InviteAsync(m.Client, "g@example.com", null, BuiltInGroups.GlobalViewersId)).StatusCode);
    }

    [Fact]
    public async Task An_inviter_sees_and_gives_only_the_spaces_they_administer_explicitly()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await GrantUsersAsync(f, InstancePermissions.InvitesCreate);

        await alice.Client.CreateSpaceAsync("MINE");                 // open, and hers explicitly
        await bob.Client.CreateSpaceAsync("OPEN");                   // open: Alice administers it only implicitly
        await bob.Client.CreateSpaceAsync("HIDDEN");
        await bob.Client.MakePrivateAsync("HIDDEN");                 // she cannot see it at all
        var openEditors = await bob.Client.SpaceGroupAsync("OPEN", Editors);
        var hiddenEditors = await bob.Client.SpaceGroupAsync("HIDDEN", Editors);
        var mineEditors = await alice.Client.SpaceGroupAsync("MINE", Editors);
        var mineViewers = await alice.Client.SpaceGroupAsync("MINE", Viewers);

        var spaces = (await alice.Client.GetFromJsonAsync<JsonElement>("/api/admin/invites/spaces")).EnumerateArray().ToList();
        Assert.Equal(["MINE"], spaces.Select(s => s.GetProperty("key").GetString()));
        Assert.Equal([Admins, Editors, Viewers, Reviewers],
            spaces[0].GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("role").GetInt32()));

        // A hidden space's group is refused in the same words as one that does not exist.
        async Task<string> Refusal(params Guid[] groups)
        {
            var res = await InviteAsync(alice.Client, "n@example.com", null, groups);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            return await res.Content.ReadAsStringAsync();
        }
        var hidden = await Refusal(hiddenEditors);
        Assert.Equal(await Refusal(Guid.NewGuid()), hidden);
        Assert.DoesNotContain("HIDDEN", hidden);
        Assert.Equal(hidden, await Refusal(openEditors));
        // Two places in one space, and a group an invite does not carry.
        await Refusal(mineEditors, mineViewers);
        await Refusal(BuiltInGroups.UsersId);
        Assert.Equal(0, Scoped(f, db => db.Invites.CountAsync()));

        // Bob invites into his hidden space; the owner, managing invites,
        // sees that a group is waiting but not the space's name.
        var bobsInvite = await InviteAsync(bob.Client, "h@example.com", null, hiddenEditors);
        bobsInvite.EnsureSuccessStatusCode();
        var listed = (await owner.Client.GetFromJsonAsync<JsonElement>("/api/admin/invites")).EnumerateArray().Single();
        var name = listed.GetProperty("groups")[0].GetProperty("name").GetString();
        Assert.Equal("A group in a space you cannot see", name);
    }

    [Fact]
    public async Task Revoking_an_invite_or_deleting_its_space_removes_its_places()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("GONE");
        var viewers = await owner.Client.SpaceGroupAsync("GONE", Viewers);
        var issued = await (await InviteAsync(owner.Client, "a@example.com", null, viewers, BuiltInGroups.GlobalViewersId))
            .Content.ReadFromJsonAsync<Issued>();
        Assert.Equal(2, Scoped(f, db => db.InviteGroups.CountAsync()));

        var deleted = await owner.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/spaces/GONE")
        {
            Content = JsonContent.Create(new { ConfirmKey = "GONE", Password = "supersecret" }),
        });
        deleted.EnsureSuccessStatusCode();
        Assert.Equal(1, Scoped(f, db => db.InviteGroups.CountAsync()));

        // The rest of the invite still works.
        var (_, id, _) = await RegisterAsync(f, "a@example.com", issued!.Token);
        Assert.True(InGroup(f, BuiltInGroups.GlobalViewersId, id));

        var other = await (await InviteAsync(owner.Client, "b@example.com", null, BuiltInGroups.GlobalViewersId)).Content.ReadFromJsonAsync<Issued>();
        var row = (await owner.Client.GetFromJsonAsync<JsonElement>("/api/admin/invites")).EnumerateArray()
            .First(i => i.GetProperty("email").GetString() == "b@example.com");
        var revoked = row.GetProperty("id").GetGuid();
        Assert.Equal(1, Scoped(f, db => db.InviteGroups.CountAsync(x => x.InviteId == revoked)));
        (await owner.Client.DeleteAsync($"/api/admin/invites/{revoked}")).EnsureSuccessStatusCode();
        Assert.Equal(0, Scoped(f, db => db.InviteGroups.CountAsync(x => x.InviteId == revoked)));
        _ = other;
    }
}
