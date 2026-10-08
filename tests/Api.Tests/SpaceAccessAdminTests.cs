using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Who has access to what, from Administration (dev-plan 21.5): each space's
/// access in counts and in full, each person's across every space (agreeing
/// with the permission check), and the review of spaces everyone may
/// administer that nobody chose to leave that way.
/// </summary>
public class SpaceAccessAdminTests
{
    private const int View = 0, Edit = 1, Admin = 2;
    private const int Viewers = 0, Admins = 2;

    private sealed record Person(HttpClient Client, Guid Id);

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

    /// <summary>A space made in the wizard's terms: everyone's level said outright (null for nobody else), and its Viewers.</summary>
    private static async Task CreateAsync(HttpClient c, string key, int? everyone, params Guid[] viewers)
    {
        var res = await c.PostAsJsonAsync("/api/spaces", new
        {
            Key = key, Name = $"Space {key}", EveryoneAccess = everyone,
            Members = new[] { new { Role = Viewers, UserIds = viewers } },
        });
        res.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> SpaceRowAsync(HttpClient c, string key) =>
        (await c.GetFromJsonAsync<List<JsonElement>>("/api/admin/spaces"))!
            .Single(s => s.GetProperty("key").GetString() == key);

    private static Task<HttpResponseMessage> ReviewAsync(HttpClient c, string key, string choice, params Guid[] admins) =>
        c.PostAsJsonAsync($"/api/admin/spaces/{key}/access-review", new { Choice = choice, Admins = admins });

    private static Task<HttpResponseMessage> EveryoneAsync(HttpClient c, string key, int? access) =>
        c.PutAsJsonAsync($"/api/spaces/{key}/permissions/everyone", new { Access = access });

    /// <summary>Empties a space's Admins group, as every space open before 21.1 was left.</summary>
    private static void EmptyAdmins(TestAppFactory f, string key) => Scoped(f, async db =>
    {
        var group = await db.Groups.SingleAsync(g => g.Space!.Key == key && g.SpaceRole == SpaceGroupRole.Admins);
        db.UserGroups.RemoveRange(db.UserGroups.Where(ug => ug.GroupId == group.Id));
        return await db.SaveChangesAsync();
    });

    // -- the list ---------------------------------------------------------------

    [Fact]
    public async Task The_spaces_list_counts_who_can_get_in_and_whether_anyone_chose_open()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("OLDOPEN");            // left out: the old default, Admin
        await CreateAsync(owner.Client, "CHOSEN", Admin);           // asked for by name
        await CreateAsync(owner.Client, "PRIV", null, bob.Id);

        var open = (await SpaceRowAsync(owner.Client, "OLDOPEN")).GetProperty("access");
        Assert.Equal(Admin, open.GetProperty("everyoneAccess").GetInt32());
        Assert.False(open.GetProperty("everyoneAdminConfirmed").GetBoolean());
        Assert.Equal(1, open.GetProperty("admins").GetInt32());
        Assert.True(open.GetProperty("hasExplicitAdmin").GetBoolean());

        Assert.True((await SpaceRowAsync(owner.Client, "CHOSEN")).GetProperty("access").GetProperty("everyoneAdminConfirmed").GetBoolean());

        var priv = (await SpaceRowAsync(owner.Client, "PRIV")).GetProperty("access");
        Assert.Equal(JsonValueKind.Null, priv.GetProperty("everyoneAccess").ValueKind);
        Assert.Equal((1, 1, 0), (priv.GetProperty("admins").GetInt32(), priv.GetProperty("viewers").GetInt32(), priv.GetProperty("otherGrants").GetInt32()));

        // A space open since before 21.1 has nobody in its Admins.
        EmptyAdmins(f, "OLDOPEN");
        open = (await SpaceRowAsync(owner.Client, "OLDOPEN")).GetProperty("access");
        Assert.Equal(0, open.GetProperty("admins").GetInt32());
        Assert.False(open.GetProperty("hasExplicitAdmin").GetBoolean());
    }

    // -- one space in full ------------------------------------------------------

    [Fact]
    public async Task A_spaces_access_is_shown_in_full_even_where_the_admin_cannot_open_it()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await CreateAsync(alice.Client, "HIDDEN", null, bob.Id);
        var spaceId = Scoped(f, db => db.Spaces.Where(s => s.Key == "HIDDEN").Select(s => s.Id).SingleAsync());
        var page = await alice.Client.PostAsJsonAsync("/api/pages", new
        {
            SpaceId = spaceId, Title = "Salaries",
            ContentJson = """{"type":"doc","content":[{"type":"paragraph"}]}""",
        });
        var pageId = (await page.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await alice.Client.PostAsJsonAsync($"/api/pages/{pageId}/restrictions", new { PrincipalType = 0, PrincipalId = alice.Id, Operation = 0 }))
            .EnsureSuccessStatusCode();

        var res = await owner.Client.GetAsync("/api/admin/spaces/hidden/access");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var groups = body.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(alice.Id, groups.Single(g => g.GetProperty("role").GetInt32() == Admins).GetProperty("members")[0].GetProperty("id").GetGuid());
        Assert.Equal(bob.Id, groups.Single(g => g.GetProperty("role").GetInt32() == Viewers).GetProperty("members")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, body.GetProperty("restrictedPages").GetInt32());
        Assert.False(body.GetProperty("youCanAdminister").GetBoolean());
        // Counts only: no page title travels.
        Assert.DoesNotContain("Salaries", await res.Content.ReadAsStringAsync());

        // Not for someone without the rights to see people and spaces.
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.GetAsync("/api/admin/spaces/HIDDEN/access")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/admin/spaces/NOSUCH/access")).StatusCode);
    }

    // -- the review ---------------------------------------------------------------

    [Fact]
    public async Task Keeping_an_open_space_records_the_choice_and_changes_nobodys_access()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("KEEPME");

        Assert.Equal(HttpStatusCode.NoContent, (await ReviewAsync(owner.Client, "KEEPME", "keep")).StatusCode);
        var access = (await SpaceRowAsync(owner.Client, "KEEPME")).GetProperty("access");
        Assert.Equal(Admin, access.GetProperty("everyoneAccess").GetInt32());
        Assert.True(access.GetProperty("everyoneAdminConfirmed").GetBoolean());
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "space.open_access_kept")));
    }

    [Fact]
    public async Task Making_an_open_space_private_needs_someone_left_to_administer_it()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await alice.Client.CreateSpaceAsync("LEGACY");
        EmptyAdmins(f, "LEGACY");

        // Nobody chosen and nobody explicit: refused, and nothing changed.
        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(owner.Client, "LEGACY", "private")).StatusCode);
        // Only its creator or the caller, from Administration.
        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(owner.Client, "LEGACY", "private", bob.Id)).StatusCode);
        Assert.Equal(Admin, (await SpaceRowAsync(owner.Client, "LEGACY")).GetProperty("access").GetProperty("everyoneAccess").GetInt32());

        // Its creator, then: private, Alice administers it, Bob is out.
        Assert.Equal(HttpStatusCode.NoContent, (await ReviewAsync(owner.Client, "LEGACY", "private", alice.Id)).StatusCode);
        var access = (await SpaceRowAsync(owner.Client, "LEGACY")).GetProperty("access");
        Assert.Equal(JsonValueKind.Null, access.GetProperty("everyoneAccess").ValueKind);
        Assert.Equal(1, access.GetProperty("admins").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync("/api/spaces/LEGACY")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync("/api/spaces/LEGACY")).StatusCode);
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "space.open_access_reviewed")));
        // No alert: nobody gained reach but the creator, named in the audit entry.
        Assert.False(Scoped(f, db => db.SecurityAlerts.AnyAsync(a => a.Kind == "space.access_recovered")));

        // Done once; a second review finds nothing to do.
        Assert.Equal(HttpStatusCode.Conflict, (await ReviewAsync(owner.Client, "LEGACY", "keep")).StatusCode);
    }

    [Fact]
    public async Task Letting_everyone_edit_with_yourself_as_admin_is_Get_Access_and_alerts()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await alice.Client.CreateSpaceAsync("EDITME");
        EmptyAdmins(f, "EDITME");

        Assert.Equal(HttpStatusCode.NoContent, (await ReviewAsync(owner.Client, "EDITME", "edit", owner.Id)).StatusCode);
        var access = (await SpaceRowAsync(owner.Client, "EDITME")).GetProperty("access");
        Assert.Equal(Edit, access.GetProperty("everyoneAccess").GetInt32());
        Assert.True(Scoped(f, db => db.SecurityAlerts.AnyAsync(a => a.Kind == "space.access_recovered")));
        // Bob still edits, but no longer manages it.
        var spaceId = Scoped(f, db => db.Spaces.Where(s => s.Key == "EDITME").Select(s => s.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.GetAsync("/api/spaces/EDITME/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bob.Client.PostAsJsonAsync("/api/pages", new
        {
            SpaceId = spaceId, Title = "Still editing", ContentJson = """{"type":"doc","content":[{"type":"paragraph"}]}""",
        })).StatusCode);
    }

    [Fact]
    public async Task The_review_needs_Manage_spaces()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await owner.Client.CreateSpaceAsync("GUARDED");
        Assert.Equal(HttpStatusCode.Forbidden, (await ReviewAsync(bob.Client, "GUARDED", "keep")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(owner.Client, "GUARDED", "lock")).StatusCode);
    }

    [Fact]
    public async Task Choosing_Administer_in_the_Permissions_tab_counts_as_reviewed_and_narrowing_clears_it()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        await CreateAsync(owner.Client, "TABBED", Edit);

        (await EveryoneAsync(owner.Client, "TABBED", Admin)).EnsureSuccessStatusCode();
        Assert.True((await SpaceRowAsync(owner.Client, "TABBED")).GetProperty("access").GetProperty("everyoneAdminConfirmed").GetBoolean());

        (await EveryoneAsync(owner.Client, "TABBED", View)).EnsureSuccessStatusCode();
        Assert.False((await SpaceRowAsync(owner.Client, "TABBED")).GetProperty("access").GetProperty("everyoneAdminConfirmed").GetBoolean());
        Assert.Null(Scoped(f, db => db.Spaces.Where(s => s.Key == "TABBED").Select(s => s.EveryoneAdminConfirmedAt).SingleAsync()));
    }

    // -- one person, every space ------------------------------------------------

    [Fact]
    public async Task A_persons_access_to_every_space_agrees_with_the_permission_check()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        await alice.Client.CreateSpaceAsync("WIDE");               // everyone: Admin
        await CreateAsync(alice.Client, "TEAM", null, bob.Id);     // Bob in its Viewers
        await CreateAsync(alice.Client, "CLOSED", null);           // not Bob

        var res = await owner.Client.GetAsync($"/api/admin/users/{bob.Id}/access");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var spaces = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("spaces").EnumerateArray()
            .ToDictionary(s => s.GetProperty("key").GetString()!);

        Assert.Equal(Admin, spaces["WIDE"].GetProperty("level").GetInt32());
        Assert.False(spaces["WIDE"].GetProperty("explicitAdmin").GetBoolean());
        Assert.Equal("everyone", spaces["WIDE"].GetProperty("reasons")[0].GetProperty("kind").GetString());

        Assert.Equal(View, spaces["TEAM"].GetProperty("level").GetInt32());
        var reason = Assert.Single(spaces["TEAM"].GetProperty("reasons").EnumerateArray());
        Assert.Equal(("group", "space"), (reason.GetProperty("kind").GetString(), reason.GetProperty("groupKind").GetString()));
        Assert.EndsWith(" Viewers", reason.GetProperty("label").GetString());

        Assert.Equal(JsonValueKind.Null, spaces["CLOSED"].GetProperty("level").ValueKind);
        Assert.Empty(spaces["CLOSED"].GetProperty("reasons").EnumerateArray());

        // Each level is what Bob himself is allowed.
        foreach (var (key, row) in spaces)
        {
            var reads = await bob.Client.GetAsync($"/api/spaces/{key}");
            Assert.Equal(row.GetProperty("level").ValueKind != JsonValueKind.Null, reads.StatusCode == HttpStatusCode.OK);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Client.GetAsync($"/api/admin/users/{alice.Id}/access")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/admin/users/{Guid.NewGuid()}/access")).StatusCode);
    }
}
