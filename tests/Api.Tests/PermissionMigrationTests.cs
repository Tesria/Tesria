using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The 21.1 move, before and after (the review's essential test 1): every
/// kind of space as it was before 21.1, then the seed, then everyone must be
/// able to do exactly what they could before, page by page. The "before" is
/// the old rules themselves (<see cref="LegacyPermissionService"/>) on the
/// old shape of the data, so this cannot pass by comparing the new code with
/// itself.
/// </summary>
public class PermissionMigrationTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}""";

    /// <summary>The instance as 0.8.7 would have left it, and who and what is in it.</summary>
    private sealed record World(
        TestAppFactory Factory,
        HttpClient Owner,
        Dictionary<string, Guid> People,
        Dictionary<string, Guid> Spaces,
        HashSet<Guid> OpenBefore,
        Guid Team,
        Guid LookAlike);

    private static async Task<(HttpClient Client, Guid Id)> PersonAsync(TestAppFactory f, string name)
    {
        var c = f.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/register",
            new { Email = $"{name}@example.com", DisplayName = name, Password = "supersecret" });
        res.EnsureSuccessStatusCode();
        return (c, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private static async Task<Guid> PageAsync(HttpClient c, Guid spaceId, string title, Guid? parent = null)
    {
        var res = await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static SpacePermission Grant(Guid space, PrincipalType type, Guid principal, SpaceOperation op) => new()
    {
        Id = Guid.NewGuid(), SpaceId = space, PrincipalType = type, PrincipalId = principal, Operation = op,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
    };

    private static PageRestriction Restrict(Guid page, PrincipalType type, Guid principal, PageOperation op) => new()
    {
        Id = Guid.NewGuid(), PageId = page, PrincipalType = type, PrincipalId = principal, Operation = op,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Every case the review lists: an open space, group-only, a person with
    /// View and Edit, a person with Admin, a person and a group, a restriction
    /// naming a moved person, a grant to Users of Admin, a suspended member,
    /// public, archived; and two spaces of one name beside a custom group
    /// already called "&lt;that name&gt; Viewers" (essential test 9).
    /// </summary>
    private static async Task<World> BeforeAsync()
    {
        var f = new TestAppFactory();
        var (owner, ownerId) = await PersonAsync(f, "owner");
        var (alice, aliceId) = await PersonAsync(f, "alice");
        var (_, bobId) = await PersonAsync(f, "bob");
        var (_, carolId) = await PersonAsync(f, "carol");
        var (_, daveId) = await PersonAsync(f, "dave");
        var (_, erinId) = await PersonAsync(f, "erin");
        (await owner.PutAsJsonAsync($"/api/admin/users/{erinId}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true })).EnsureSuccessStatusCode();
        var team = (await (await owner.PostAsJsonAsync("/api/groups", new { Name = "Team" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var lookAlike = (await (await owner.PostAsJsonAsync("/api/groups", new { Name = "Handbook Viewers" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var member in new[] { aliceId, bobId })
            (await owner.PostAsJsonAsync($"/api/groups/{team}/members", new { UserId = member })).EnsureSuccessStatusCode();

        var spaces = new Dictionary<string, Guid>();
        var pages = new Dictionary<string, (Guid Plain, Guid Restricted, Guid Child, Guid EditOnly)>();
        foreach (var (key, name) in new[]
        {
            ("OPEN", "Open"), ("GROUPONLY", "Group only"), ("PERSON", "Person"), ("ADMINP", "Person admin"),
            ("MIXED", "Mixed"), ("RESTRICT", "Restricted"), ("USERSADMIN", "Users admin"), ("PUBLIC", "Public"),
            ("ARCHIVED", "Archived"), ("HBONE", "Handbook"), ("HBTWO", "Handbook"),
        })
        {
            var res = await owner.PostAsJsonAsync("/api/spaces", new { Key = key, Name = name });
            res.EnsureSuccessStatusCode();
            var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            spaces[key] = id;
            var restricted = await PageAsync(owner, id, $"{key} restricted");
            pages[key] = (await PageAsync(owner, id, $"{key} plain"), restricted,
                await PageAsync(owner, id, $"{key} child", restricted), await PageAsync(owner, id, $"{key} edit only"));
            // A draft that is not the readers' own: readable only by those who may edit.
            (await alice.PostAsJsonAsync("/api/pages/draft", new { SpaceId = id, ParentPageId = (Guid?)null })).EnsureSuccessStatusCode();
        }

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Back to the shape before 21.1: no space groups, no EveryoneAccess.
            var spaceGroups = await db.Groups.Where(g => g.SpaceId != null).Select(g => g.Id).ToListAsync();
            db.SpacePermissions.RemoveRange(await db.SpacePermissions.ToListAsync());
            db.Groups.RemoveRange(await db.Groups.Where(g => spaceGroups.Contains(g.Id)).ToListAsync());
            foreach (var s in await db.Spaces.ToListAsync()) s.EveryoneAccess = null;
            await db.SaveChangesAsync();

            db.SpacePermissions.AddRange(
                Grant(spaces["GROUPONLY"], PrincipalType.Group, team, SpaceOperation.View),
                Grant(spaces["PERSON"], PrincipalType.User, aliceId, SpaceOperation.View),
                Grant(spaces["PERSON"], PrincipalType.User, aliceId, SpaceOperation.Edit),
                Grant(spaces["PERSON"], PrincipalType.User, bobId, SpaceOperation.Edit),
                // Names no account: grants nothing, before or after.
                Grant(spaces["PERSON"], PrincipalType.User, Guid.NewGuid(), SpaceOperation.View),
                Grant(spaces["ADMINP"], PrincipalType.User, carolId, SpaceOperation.Admin),
                Grant(spaces["MIXED"], PrincipalType.User, daveId, SpaceOperation.Admin),
                Grant(spaces["MIXED"], PrincipalType.Group, team, SpaceOperation.Edit),
                Grant(spaces["MIXED"], PrincipalType.User, carolId, SpaceOperation.View),
                Grant(spaces["RESTRICT"], PrincipalType.User, aliceId, SpaceOperation.Admin),
                Grant(spaces["RESTRICT"], PrincipalType.User, bobId, SpaceOperation.View),
                Grant(spaces["RESTRICT"], PrincipalType.User, carolId, SpaceOperation.Edit),
                Grant(spaces["USERSADMIN"], PrincipalType.Group, BuiltInGroups.UsersId, SpaceOperation.Admin),
                Grant(spaces["ARCHIVED"], PrincipalType.User, carolId, SpaceOperation.View),
                Grant(spaces["ARCHIVED"], PrincipalType.User, ownerId, SpaceOperation.Admin),
                Grant(spaces["HBTWO"], PrincipalType.User, aliceId, SpaceOperation.Admin));
            foreach (var (key, p) in pages)
            {
                // A View restriction naming a person whose grant moves, and an
                // Edit restriction, in every space.
                db.PageRestrictions.Add(Restrict(p.Restricted, PrincipalType.User, bobId, PageOperation.View));
                db.PageRestrictions.Add(Restrict(p.EditOnly, PrincipalType.User, carolId, PageOperation.Edit));
            }
            db.PageRestrictions.Add(Restrict(pages["GROUPONLY"].Plain, PrincipalType.Group, team, PageOperation.View));
            var publicSpace = await db.Spaces.SingleAsync(s => s.Id == spaces["PUBLIC"]);
            publicSpace.IsPublic = true;
            publicSpace.PublicSince = DateTimeOffset.UtcNow;
            (await db.Spaces.SingleAsync(s => s.Id == spaces["ARCHIVED"])).Archived = true;
            (await db.Users.SingleAsync(u => u.Id == daveId)).Status = UserStatus.Suspended;
            await db.SaveChangesAsync();
        }

        var people = new Dictionary<string, Guid>
        {
            ["owner"] = ownerId, ["alice"] = aliceId, ["bob"] = bobId, ["carol"] = carolId,
            ["dave"] = daveId, ["erin"] = erinId,
        };
        return new World(f, owner, people, spaces, [spaces["OPEN"], spaces["PUBLIC"], spaces["HBONE"]], team, lookAlike);
    }

    private sealed record SpaceAnswer(bool View, bool Edit, bool Admin, bool ExplicitAdmin);
    private sealed record PageAnswer(bool View, bool Edit, bool Read);

    private sealed record Answers(
        Dictionary<(Guid User, Guid Space), SpaceAnswer> Spaces,
        Dictionary<(Guid User, Guid Page), PageAnswer> Pages,
        Dictionary<Guid, HashSet<Guid>> Viewable,
        Dictionary<Guid, bool> PublicSpaces,
        Dictionary<Guid, bool> PublicPages,
        HashSet<Guid> AnonymousViewable);

    private static async Task<(List<Guid> Spaces, List<Guid> Pages)> EverythingAsync(AppDbContext db) =>
        (await db.Spaces.AsNoTracking().Select(s => s.Id).ToListAsync(),
         await db.Pages.IgnoreQueryFilters().AsNoTracking().Select(p => p.Id).ToListAsync());

    private static async Task<Answers> BeforeRulesAsync(World w)
    {
        using var scope = w.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        // With 21.6's restriction rule, which binds explicit admins too: that
        // change is deliberate and separate, and this test is about the move.
        var legacy = new LegacyPermissionService(db, sp.GetRequiredService<CurrentUser>(), sp.GetRequiredService<ISiteSettingsService>())
        {
            AdminsPassRestrictions = false,
        };
        var (spaces, pages) = await EverythingAsync(db);
        var answers = new Answers([], [], [], [], [], []);
        foreach (var user in w.People.Values)
        {
            var as_ = legacy.AsUser(user);
            foreach (var s in spaces)
                answers.Spaces[(user, s)] = new(await as_.CanViewSpaceAsync(s), await as_.CanEditSpaceAsync(s),
                    await as_.CanAdminSpaceAsync(s), await as_.HasExplicitSpaceAdminAsync(s));
            foreach (var p in pages)
                answers.Pages[(user, p)] = new(await as_.CanViewPageAsync(p), await as_.CanEditPageAsync(p), await as_.CanReadPageAsync(p));
            answers.Viewable[user] = await as_.ViewableSpaceIdsAsync();
        }
        var anon = legacy.AsAnonymous();
        foreach (var s in spaces) answers.PublicSpaces[s] = await anon.IsPubliclyViewableSpaceAsync(s);
        foreach (var p in pages) answers.PublicPages[p] = await anon.IsPubliclyViewablePageAsync(p);
        answers.AnonymousViewable.UnionWith(await anon.ViewableSpaceIdsAsync());
        return answers;
    }

    private static async Task<Answers> AfterRulesAsync(World w)
    {
        using var scope = w.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var perms = sp.GetRequiredService<IPermissionService>();
        var (spaces, pages) = await EverythingAsync(db);
        var answers = new Answers([], [], [], [], [], []);
        foreach (var user in w.People.Values)
        {
            var as_ = perms.AsUser(user);
            foreach (var s in spaces)
                answers.Spaces[(user, s)] = new(await as_.CanViewSpaceAsync(s), await as_.CanEditSpaceAsync(s),
                    await as_.CanAdminSpaceAsync(s), await as_.IsExplicitSpaceAdminAsync(s));
            foreach (var p in pages)
                answers.Pages[(user, p)] = new(await as_.CanViewPageAsync(p), await as_.CanEditPageAsync(p), await as_.CanReadPageAsync(p));
            answers.Viewable[user] = await as_.ViewableSpaceIdsAsync();
        }
        var anon = perms.AsAnonymous();
        foreach (var s in spaces) answers.PublicSpaces[s] = await anon.IsPubliclyViewableSpaceAsync(s);
        foreach (var p in pages) answers.PublicPages[p] = await anon.IsPubliclyViewablePageAsync(p);
        answers.AnonymousViewable.UnionWith(await anon.ViewableSpaceIdsAsync());
        return answers;
    }

    private static async Task<int> SeedAsync(World w)
    {
        using var scope = w.Factory.Services.CreateScope();
        return await SpaceGroupSeed.EnsureAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            NullLogger.Instance);
    }

    [Fact]
    public async Task Everyone_can_do_exactly_what_they_could_before_the_move()
    {
        var w = await BeforeAsync();
        using var _ = w.Factory;
        var before = await BeforeRulesAsync(w);

        Assert.Equal(w.Spaces.Count, await SeedAsync(w));
        var after = await AfterRulesAsync(w);

        // The one deliberate tightening (21.1): a suspended account is not
        // "signed in", so a space everyone signed in could use no longer lets
        // it in, even for a check made on its behalf. Everything else is
        // unchanged, to the page.
        var dave = w.People["dave"];
        bool Tightened(Guid user, Guid space) => user == dave && w.OpenBefore.Contains(space);
        var pageSpace = new Dictionary<Guid, Guid>();
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var p in await db.Pages.IgnoreQueryFilters().Select(p => new { p.Id, p.SpaceId }).ToListAsync())
                pageSpace[p.Id] = p.SpaceId;
        }

        var differences = new List<string>();
        foreach (var (key, was) in before.Spaces)
        {
            var expected = Tightened(key.User, key.Space) ? new SpaceAnswer(false, false, false, false) : was;
            if (after.Spaces[key] != expected) differences.Add($"space {key}: {was} -> {after.Spaces[key]}");
        }
        foreach (var (key, was) in before.Pages)
        {
            var expected = Tightened(key.User, pageSpace[key.Page]) ? new PageAnswer(false, false, false) : was;
            if (after.Pages[key] != expected) differences.Add($"page {key}: {was} -> {after.Pages[key]}");
        }
        foreach (var (user, was) in before.Viewable)
        {
            var expected = was.Where(s => !Tightened(user, s)).ToHashSet();
            if (!after.Viewable[user].SetEquals(expected)) differences.Add($"viewable for {user}");
        }
        Assert.Empty(differences);
        Assert.Equal(before.PublicSpaces, after.PublicSpaces);
        Assert.Equal(before.PublicPages, after.PublicPages);
        Assert.True(before.AnonymousViewable.SetEquals(after.AnonymousViewable));

        // And the tightening is real, not a quirk of this data.
        Assert.True(before.Spaces[(dave, w.Spaces["OPEN"])].View);
        Assert.False(after.Spaces[(dave, w.Spaces["OPEN"])].View);

        // What the move left behind.
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var everyone = await db.Spaces.ToDictionaryAsync(s => s.Id, s => s.EveryoneAccess);
            foreach (var (key, id) in w.Spaces)
                Assert.Equal(w.OpenBefore.Contains(id) ? SpaceOperation.Admin : null, everyone[id]);
            // No grant to a person remains, except the one naming nobody.
            Assert.Single(await db.SpacePermissions.Where(p => p.PrincipalType == PrincipalType.User).ToListAsync());
            // Group grants stayed grants, Users: Admin included.
            Assert.True(await db.SpacePermissions.AnyAsync(p => p.SpaceId == w.Spaces["USERSADMIN"]
                && p.PrincipalId == BuiltInGroups.UsersId && p.Operation == SpaceOperation.Admin));
            Assert.True(await db.SpacePermissions.AnyAsync(p => p.SpaceId == w.Spaces["MIXED"] && p.PrincipalId == w.Team));
            // Every moved grant is on record, with what it was.
            var moves = await db.SpaceGrantMoves.ToListAsync();
            Assert.Equal(12, moves.Count);
            var aliceAdmin = moves.Single(m => m.SpaceId == w.Spaces["RESTRICT"] && m.UserId == w.People["alice"]);
            Assert.Equal(SpaceOperation.Admin, aliceAdmin.Operation);
            Assert.Equal(SpaceGroupRole.Admins, (await db.Groups.SingleAsync(g => g.Id == aliceAdmin.GroupId)).SpaceRole);
            Assert.True(await db.UserGroups.AnyAsync(ug => ug.GroupId == aliceAdmin.GroupId && ug.UserId == w.People["alice"]));
            // One audit entry per space.
            Assert.Equal(w.Spaces.Count, await db.AuditLogs.CountAsync(a => a.Action == "space.groups_created"));
        }
    }

    [Fact]
    public async Task Running_the_seed_again_changes_nothing()
    {
        var w = await BeforeAsync();
        using var _ = w.Factory;
        await SeedAsync(w);
        var first = await AfterRulesAsync(w);
        int Groups, Memberships, Moves, Audits, Grants;
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (Groups, Memberships, Moves, Audits, Grants) = (await db.Groups.CountAsync(), await db.UserGroups.CountAsync(),
                await db.SpaceGrantMoves.CountAsync(), await db.AuditLogs.CountAsync(), await db.SpacePermissions.CountAsync());
        }

        Assert.Equal(0, await SeedAsync(w));

        var second = await AfterRulesAsync(w);
        Assert.Equal(first.Spaces, second.Spaces);
        Assert.Equal(first.Pages, second.Pages);
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(Groups, await db.Groups.CountAsync());
            Assert.Equal(Memberships, await db.UserGroups.CountAsync());
            Assert.Equal(Moves, await db.SpaceGrantMoves.CountAsync());
            Assert.Equal(Audits, await db.AuditLogs.CountAsync());
            Assert.Equal(Grants, await db.SpacePermissions.CountAsync());
            Assert.Equal(w.Spaces.Count * 4, await db.Groups.CountAsync(g => g.SpaceId != null));
        }
    }

    [Fact]
    public async Task A_space_someone_already_changed_is_never_recomputed()
    {
        var w = await BeforeAsync();
        using var _ = w.Factory;
        await SeedAsync(w);
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // An administrator closes the open space after the move; a second
            // start must not see "no grants" and open it again.
            (await db.Spaces.SingleAsync(s => s.Id == w.Spaces["OPEN"])).EveryoneAccess = null;
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, await SeedAsync(w));
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.Spaces.SingleAsync(s => s.Id == w.Spaces["OPEN"])).EveryoneAccess);
        }
    }

    [Fact]
    public async Task Two_spaces_of_one_name_and_a_look_alike_group_migrate_side_by_side()
    {
        // Essential test 9: the names people see are derived, so nothing
        // stored can collide with a group someone already made.
        var w = await BeforeAsync();
        using var _ = w.Factory;
        await SeedAsync(w);

        var owner = w.Owner;
        var groups = await owner.GetFromJsonAsync<List<JsonElement>>("/api/groups");
        var named = groups!.Where(g => g.GetProperty("name").GetString() == "Handbook Viewers").ToList();
        // The custom one, and the open Handbook's: the owner cannot see the
        // second Handbook (only Alice administers it), so not its groups either.
        Assert.Equal(2, named.Count);
        Assert.Single(named, g => g.GetProperty("spaceId").ValueKind == JsonValueKind.Null && g.GetProperty("id").GetGuid() == w.LookAlike);
        Assert.Equal(w.Spaces["HBONE"], named.Single(g => g.GetProperty("spaceId").ValueKind != JsonValueKind.Null)
            .GetProperty("spaceId").GetGuid());
        using (var scope = w.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.Groups.CountAsync(g => g.SpaceRole == SpaceGroupRole.Viewers
                && (g.SpaceId == w.Spaces["HBONE"] || g.SpaceId == w.Spaces["HBTWO"])));
        }

        // And a new custom group may still not take a name another custom group has.
        Assert.Equal(System.Net.HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync("/api/groups", new { Name = "handbook viewers" })).StatusCode);
    }
}
