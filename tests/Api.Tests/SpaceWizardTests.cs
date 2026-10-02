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
/// Creating a space as a wizard (dev-plan 21.2): one create call that makes
/// the space, its four groups, what everyone signed in gets, and who is in
/// each group, together or not at all.
/// </summary>
public class SpaceWizardTests
{
    private const int View = 0, Edit = 1, Admin = 2;
    private const int Viewers = 0, Editors = 1, Admins = 2, Reviewers = 3;

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

    private static List<Guid> MembersOf(JsonElement access, int role) =>
        access.GetProperty("groups").EnumerateArray()
            .Single(g => g.GetProperty("role").GetInt32() == role)
            .GetProperty("members").EnumerateArray().Select(m => m.GetProperty("userId").GetGuid()).ToList();

    [Fact]
    public async Task One_call_makes_the_space_its_level_for_everyone_and_its_members()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        var carol = await PersonAsync(f);
        var dan = await PersonAsync(f);

        var res = await alice.Client.PostAsJsonAsync("/api/spaces", new
        {
            Key = "TEAM",
            Name = "Team",
            EveryoneAccess = (int?)null,
            Members = new object[]
            {
                // Alice names herself in Admins too: not an error, and not twice.
                new { Role = Admins, UserIds = new[] { alice.Id, bob.Id } },
                new { Role = Editors, UserIds = new[] { carol.Id, carol.Id } },
                new { Role = Viewers, UserIds = new[] { dan.Id } },
                new { Role = Reviewers, UserIds = new[] { dan.Id } },
            },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var access = await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/TEAM/permissions");
        Assert.Equal(JsonValueKind.Null, access.GetProperty("everyoneAccess").ValueKind);
        Assert.True(access.GetProperty("canManageAdmins").GetBoolean());
        Assert.Equal(new[] { alice.Id, bob.Id }.Order(), MembersOf(access, Admins).Order());
        Assert.Equal([carol.Id], MembersOf(access, Editors));
        Assert.Equal([dan.Id], MembersOf(access, Viewers));
        Assert.Equal([dan.Id], MembersOf(access, Reviewers));
        Assert.Empty(access.GetProperty("grants").EnumerateArray());

        // Closed to everyone else: the owner, in none of its groups, does not see it.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/spaces/TEAM")).StatusCode);
        // Carol may edit; Dan may only read.
        var space = await carol.Client.GetFromJsonAsync<JsonElement>("/api/spaces/TEAM");
        Assert.True(space.GetProperty("canEdit").GetBoolean());
        Assert.False(space.GetProperty("canAdmin").GetBoolean());
        space = await dan.Client.GetFromJsonAsync<JsonElement>("/api/spaces/TEAM");
        Assert.False(space.GetProperty("canEdit").GetBoolean());

        // Each membership is audited against the space, with the creation.
        var spaceId = space.GetProperty("id").GetGuid();
        var added = Scoped(f, db => db.AuditLogs.Where(a => a.Action == "space.group_member_added" && a.TargetId == spaceId).CountAsync());
        Assert.Equal(4, added); // Bob, Carol, Dan twice; Alice's own place is the creation's
        Assert.True(Scoped(f, db => db.AuditLogs.AnyAsync(a => a.Action == "space.created" && a.TargetId == spaceId)));
    }

    [Theory]
    [InlineData(View)]
    [InlineData(Edit)]
    [InlineData(Admin)]
    public async Task Each_level_for_everyone_is_set_at_creation_without_the_password(int level)
    {
        // Every session past the sudo window: widening later would ask, creating does not.
        using var f = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" });
        var alice = await PersonAsync(f);
        var res = await alice.Client.PostAsJsonAsync("/api/spaces", new { Key = "LVL", Name = "Level", EveryoneAccess = level });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var access = await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/LVL/permissions");
        Assert.Equal(level, access.GetProperty("everyoneAccess").GetInt32());
    }

    [Fact]
    public async Task Leaving_out_everyoneAccess_keeps_a_new_space_open_as_before()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);
        var bob = await PersonAsync(f);
        (await alice.Client.PostAsJsonAsync("/api/spaces", new { Key = "OLD", Name = "Old", Description = "As API clients send it" }))
            .EnsureSuccessStatusCode();

        var access = await alice.Client.GetFromJsonAsync<JsonElement>("/api/spaces/OLD/permissions");
        Assert.Equal(Admin, access.GetProperty("everyoneAccess").GetInt32());
        Assert.Equal([alice.Id], MembersOf(access, Admins));
        var asBob = await bob.Client.GetFromJsonAsync<JsonElement>("/api/spaces/OLD");
        Assert.True(asBob.GetProperty("canAdmin").GetBoolean());
    }

    [Fact]
    public async Task A_refused_create_makes_nothing()
    {
        using var f = new TestAppFactory();
        var alice = await PersonAsync(f);

        // Someone who has no account.
        var res = await alice.Client.PostAsJsonAsync("/api/spaces", new
        {
            Key = "GONE", Name = "Gone", Members = new[] { new { Role = Editors, UserIds = new[] { Guid.NewGuid() } } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        // A level that does not exist, and one given as words.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Client.PostAsJsonAsync("/api/spaces",
            new { Key = "GONE", Name = "Gone", EveryoneAccess = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Client.PostAsJsonAsync("/api/spaces",
            new { Key = "GONE", Name = "Gone", EveryoneAccess = "everyone" })).StatusCode);
        // A group that does not exist.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Client.PostAsJsonAsync("/api/spaces",
            new { Key = "GONE", Name = "Gone", Members = new[] { new { Role = 9, UserIds = new[] { alice.Id } } } })).StatusCode);

        Assert.False(Scoped(f, db => db.Spaces.AnyAsync(s => s.Key == "GONE")));
        Assert.Equal(0, Scoped(f, db => db.Groups.CountAsync(g => g.SpaceId != null)));
    }

    [Fact]
    public async Task Creating_with_members_needs_the_right_to_create_spaces_and_nothing_more()
    {
        using var f = new TestAppFactory();
        var owner = await PersonAsync(f);
        var member = await PersonAsync(f);
        var other = await PersonAsync(f);
        var admin = await PersonAsync(f);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.FirstAsync(u => u.Id == admin.Id);
            row.Role = UserRole.Admin;
            row.RoleId = await RoleSeed.BuiltInIdAsync(db, UserRole.Admin);
            await db.SaveChangesAsync();
        }

        // A member, holding Create spaces as users do by default, may fill
        // every group of their new space, Admins included: they are its
        // explicit administrator.
        (await member.Client.PostAsJsonAsync("/api/spaces", new
        {
            Key = "MINE", Name = "Mine", EveryoneAccess = (int?)null,
            Members = new[] { new { Role = Admins, UserIds = new[] { other.Id } } },
        })).EnsureSuccessStatusCode();
        var access = await other.Client.GetFromJsonAsync<JsonElement>("/api/spaces/MINE/permissions");
        Assert.True(access.GetProperty("canManageAdmins").GetBoolean());

        // An administrator likewise.
        (await admin.Client.PostAsJsonAsync("/api/spaces", new
        {
            Key = "ADMINS", Name = "Admins' space", EveryoneAccess = View,
            Members = new[] { new { Role = Editors, UserIds = new[] { member.Id } } },
        })).EnsureSuccessStatusCode();

        // Without the right, nothing, members or not.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.Key == Role.Keys.For(UserRole.Member));
            role.Permissions.RemoveAll(p => p.Key == InstancePermissions.SpacesCreate);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<PermissionCache>().Invalidate();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsJsonAsync("/api/spaces", new
        {
            Key = "NOPE", Name = "Nope", Members = new[] { new { Role = Admins, UserIds = new[] { other.Id } } },
        })).StatusCode);
        Assert.False(Scoped(f, db => db.Spaces.AnyAsync(s => s.Key == "NOPE")));
    }
}
