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
/// Dev-plan 21.6, after Confluence: default access for new spaces (Tesria's
/// administrators administer and everyone signed in edits, on a new
/// instance), and page restrictions that bind a space's administrators,
/// who may lift them instead (audited, and the author is told).
/// </summary>
public class NewSpaceDefaultsTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}""";
    private const int View = 0, Edit = 1, Admin = 2;
    private const int Editors = 1;
    private static readonly Guid AdminsGroup = BuiltInGroups.AdminsId;

    private sealed record Person(HttpClient Client, Guid Id);

    /// <summary>An instance with the defaults a new one ships with, not the tests' legacy ones.</summary>
    private static TestAppFactory Fresh() => new() { LegacySpaceDefaults = false };

    private static async Task<Person> PersonAsync(TestAppFactory f)
    {
        var c = f.CreateClient();
        return new Person(c, await c.RegisterAndSignInAsync());
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

    private static T Scoped<T>(TestAppFactory f, Func<AppDbContext, Task<T>> read)
    {
        using var scope = f.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>()).GetAwaiter().GetResult();
    }

    private static async Task<Guid> PageAsync(HttpClient c, Guid spaceId, string title)
    {
        var res = await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, Title = title, ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PermissionsAsync(HttpClient c, string key) =>
        await c.GetFromJsonAsync<JsonElement>($"/api/spaces/{key}/permissions");

    // -- defaults ---------------------------------------------------------------

    [Fact]
    public async Task A_new_instance_lets_its_administrators_administer_and_everyone_edit_a_new_space()
    {
        using var f = Fresh();
        var owner = await PersonAsync(f);
        var alex = await PersonAsync(f);
        var mei = await PersonAsync(f);
        await MakeAdminAsync(f, alex.Id);

        var defaults = await mei.Client.GetFromJsonAsync<JsonElement>("/api/space-defaults");
        Assert.Equal(Edit, defaults.GetProperty("everyoneAccess").GetInt32());
        var group = Assert.Single(defaults.GetProperty("groups").EnumerateArray());
        Assert.Equal((AdminsGroup, Admin, "Tesria administrators"),
            (group.GetProperty("groupId").GetGuid(), group.GetProperty("level").GetInt32(), group.GetProperty("name").GetString()));

        // Mei makes a space saying nothing about access: it starts from the defaults.
        (await mei.Client.PostAsJsonAsync("/api/spaces", new { Key = "MEIS", Name = "Mei's" })).EnsureSuccessStatusCode();
        // Alex, a Tesria administrator, administers it without Get Access...
        Assert.Equal(HttpStatusCode.OK, (await alex.Client.GetAsync("/api/spaces/MEIS/permissions")).StatusCode);
        var access = await PermissionsAsync(alex.Client, "MEIS");
        Assert.Equal(Edit, access.GetProperty("everyoneAccess").GetInt32());
        Assert.Contains(access.GetProperty("grants").EnumerateArray(), g =>
            g.GetProperty("principalId").GetGuid() == AdminsGroup && g.GetProperty("operation").GetInt32() == Admin);
        // ...and as an explicit one, so he may choose its Admins.
        Assert.True(access.GetProperty("canManageAdmins").GetBoolean());

        // Someone else signed in edits it but does not manage it.
        var sam = await PersonAsync(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await sam.Client.GetAsync("/api/spaces/MEIS/permissions")).StatusCode);
    }

    [Fact]
    public async Task What_a_create_says_overrides_the_defaults()
    {
        using var f = Fresh();
        var owner = await PersonAsync(f);
        var alex = await PersonAsync(f);
        var mei = await PersonAsync(f);
        await MakeAdminAsync(f, alex.Id);

        // An HR space: nobody else, and no administrators of Tesria.
        (await mei.Client.PostAsJsonAsync("/api/spaces", new { Key = "HR", Name = "HR", EveryoneAccess = (int?)null, Groups = Array.Empty<object>() }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await alex.Client.GetAsync("/api/spaces/HR")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mei.Client.GetAsync("/api/spaces/HR")).StatusCode);

        // A space's own group, or the built-in Users, is not one to grant here.
        var bad = await mei.Client.PostAsJsonAsync("/api/spaces", new
        {
            Key = "BAD", Name = "Bad", Groups = new[] { new { GroupId = BuiltInGroups.UsersId, Level = Admin } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Changing_the_defaults_needs_Manage_spaces_and_changes_no_existing_space()
    {
        using var f = Fresh();
        var owner = await PersonAsync(f);
        var mei = await PersonAsync(f);
        (await owner.Client.PostAsJsonAsync("/api/spaces", new { Key = "BEFORE", Name = "Before" })).EnsureSuccessStatusCode();

        var change = new { EveryoneAccess = (int?)View, Groups = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.Forbidden, (await mei.Client.PutAsJsonAsync("/api/admin/space-defaults", change)).StatusCode);
        var res = await owner.Client.PutAsJsonAsync("/api/admin/space-defaults", change);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "settings.space_defaults_changed")));

        // The earlier space keeps what it was made with.
        var before = await PermissionsAsync(owner.Client, "BEFORE");
        Assert.Equal(Edit, before.GetProperty("everyoneAccess").GetInt32());
        Assert.Contains(before.GetProperty("grants").EnumerateArray(), g => g.GetProperty("principalId").GetGuid() == AdminsGroup);
        // A new one takes the new defaults.
        (await owner.Client.PostAsJsonAsync("/api/spaces", new { Key = "AFTER", Name = "After" })).EnsureSuccessStatusCode();
        var after = await PermissionsAsync(owner.Client, "AFTER");
        Assert.Equal(View, after.GetProperty("everyoneAccess").GetInt32());
        Assert.DoesNotContain(after.GetProperty("grants").EnumerateArray(), g => g.GetProperty("principalId").GetGuid() == AdminsGroup);

        // A space's own group cannot be a default.
        var spaceGroup = await owner.Client.SpaceGroupAsync("AFTER", Editors);
        var refused = await owner.Client.PutAsJsonAsync("/api/admin/space-defaults",
            new { EveryoneAccess = (int?)null, Groups = new[] { new { GroupId = spaceGroup, Level = Edit } } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // -- restrictions bind administrators -----------------------------------------

    [Fact]
    public async Task A_space_admin_cannot_read_a_restricted_page_but_may_lift_it_and_its_author_is_told()
    {
        using var f = Fresh();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        // Alice makes the space (she is in its Admins); everyone may edit it.
        (await alice.Client.PostAsJsonAsync("/api/spaces", new { Key = "TEAM", Name = "Team", EveryoneAccess = Edit, Groups = Array.Empty<object>() }))
            .EnsureSuccessStatusCode();
        var spaceId = Scoped(f, db => db.Spaces.Where(s => s.Key == "TEAM").Select(s => s.Id).SingleAsync());
        var page = await PageAsync(bob.Client, spaceId, "Bob's review notes");
        (await bob.Client.PostAsJsonAsync($"/api/pages/{page}/restrictions", new { PrincipalType = 0, PrincipalId = bob.Id, Operation = 0 }))
            .EnsureSuccessStatusCode();

        // Restrictions bind her: she cannot read it, by any door.
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.GetAsync($"/api/pages/{page}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.GetAsync($"/api/pages/{page}/export?format=markdown")).StatusCode);

        // She sees that it is restricted, and to whom.
        var listed = await alice.Client.GetFromJsonAsync<List<JsonElement>>("/api/spaces/TEAM/restricted-pages");
        var row = Assert.Single(listed!);
        Assert.Equal("Bob's review notes", row.GetProperty("title").GetString());
        Assert.False(row.GetProperty("youCanRead").GetBoolean());

        // Bob is not its administrator: not for him to list or lift.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.GetAsync("/api/spaces/TEAM/restricted-pages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.PostAsJsonAsync($"/api/pages/{page}/restrictions/lift", new { })).StatusCode);

        // She lifts it: recorded, Bob is told, and now she can read it.
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PostAsJsonAsync($"/api/pages/{page}/restrictions/lift", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/pages/{page}")).StatusCode);
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "page.restrictions_lifted" && a.TargetId == page)));
        var told = await bob.Client.GetFromJsonAsync<List<JsonElement>>("/api/notifications");
        Assert.Contains(told!, n => n.GetProperty("action").GetString() == "page.restrictions_lifted");
        Assert.Empty((await alice.Client.GetFromJsonAsync<List<JsonElement>>("/api/spaces/TEAM/restricted-pages"))!);
    }

    [Fact]
    public async Task Where_everyone_administers_nobody_may_lift_restrictions_by_being_everyone()
    {
        using var f = Fresh();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        (await alice.Client.PostAsJsonAsync("/api/spaces", new { Key = "WIDE", Name = "Wide", EveryoneAccess = Admin, Groups = Array.Empty<object>() }))
            .EnsureSuccessStatusCode();
        var spaceId = Scoped(f, db => db.Spaces.Where(s => s.Key == "WIDE").Select(s => s.Id).SingleAsync());
        var page = await PageAsync(alice.Client, spaceId, "Private plan");
        (await alice.Client.PostAsJsonAsync($"/api/pages/{page}/restrictions", new { PrincipalType = 0, PrincipalId = alice.Id, Operation = 0 }))
            .EnsureSuccessStatusCode();

        // Bob may administer the space as everyone does, but that is not explicit.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.GetAsync("/api/spaces/WIDE/restricted-pages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.PostAsJsonAsync($"/api/pages/{page}/restrictions/lift", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/pages/{page}")).StatusCode);
    }

    [Fact]
    public async Task The_review_can_let_Tesrias_administrators_administer_a_space()
    {
        using var f = Fresh();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        (await alice.Client.PostAsJsonAsync("/api/spaces", new { Key = "OLD", Name = "Old", EveryoneAccess = Admin, Groups = Array.Empty<object>() }))
            .EnsureSuccessStatusCode();
        // As every space open since before 0.9 is: nobody chose it, nobody in Admins.
        Scoped(f, async db =>
        {
            var space = await db.Spaces.SingleAsync(s => s.Key == "OLD");
            space.EveryoneAdminConfirmedAt = null;
            var admins = await db.Groups.SingleAsync(g => g.SpaceId == space.Id && g.SpaceRole == SpaceGroupRole.Admins);
            db.UserGroups.RemoveRange(db.UserGroups.Where(ug => ug.GroupId == admins.Id));
            return await db.SaveChangesAsync();
        });

        // Private, with Tesria's administrators as its administrators: enough on its own.
        var res = await owner.Client.PostAsJsonAsync("/api/admin/spaces/OLD/access-review",
            new { Choice = "private", Admins = Array.Empty<Guid>(), TesriaAdministrators = true });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var access = await PermissionsAsync(owner.Client, "OLD");
        Assert.Equal(JsonValueKind.Null, access.GetProperty("everyoneAccess").ValueKind);
        Assert.Contains(access.GetProperty("grants").EnumerateArray(), g =>
            g.GetProperty("principalId").GetGuid() == AdminsGroup && g.GetProperty("operation").GetInt32() == Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Client.GetAsync("/api/spaces/OLD")).StatusCode);
    }
}
