using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Instance rights (dev-plan 11.1): the catalogue's defaults, the routes that
/// name a right, and what happens when a role loses one. The refusals matter
/// more than the grants: each is something an administrator could otherwise do.
/// </summary>
public class InstancePermissionTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role, string[] Permissions, string RoleName);
    private record RoleDto(Guid Id, string? Key, string Name, string? Description, int Tier, bool BuiltIn,
        string[] Permissions, int Members, bool Editable);
    private record PermissionDto(string Key, string Area, string Label, string Description, string Scope);
    private record MatrixDto(List<PermissionDto> Catalogue, List<PermissionDto> Reserved, List<RoleDto> Roles,
        DateTimeOffset? ReviewedAt, string? ReviewedByName);
    private record AlertDto(Guid Id, string Kind, int Severity, string Key);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDto(Guid Id, string Title);
    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>())!;

    private static async Task<MatrixDto> MatrixAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MatrixDto>("/api/admin/roles"))!;

    private static async Task<T> InScopeAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Takes one right away from a role and waits out the cache.</summary>
    private static async Task RevokeAsync(TestAppFactory factory, UserRole tier, string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.Key == Role.Keys.For(tier));
        role.Permissions.RemoveAll(p => p.Key == key);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<PermissionCache>().Invalidate();
    }

    private static async Task GrantAsync(TestAppFactory factory, UserRole tier, string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.Include(r => r.Permissions).FirstAsync(r => r.Key == Role.Keys.For(tier));
        if (role.Permissions.All(p => p.Key != key))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Key = key });
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<PermissionCache>().Invalidate();
    }

    // --- The catalogue and the seed.

    [Fact]
    public void The_defaults_are_what_the_plan_says()
    {
        var user = InstancePermissions.DefaultsFor(UserRole.Member).ToHashSet();
        var admin = InstancePermissions.DefaultsFor(UserRole.Admin).ToHashSet();

        Assert.Equal(
            ["pages.delete_own", "pages.export", "spaces.create", "tokens.use"],
            user.Order());
        // The one thing an upgrade changes for users.
        Assert.DoesNotContain(InstancePermissions.PagesDeleteAny, user);
        Assert.Contains(InstancePermissions.PagesDeleteAny, admin);
        // Administrators keep everything they could do before 11.1.
        Assert.True(user.IsSubsetOf(admin));
        Assert.Equal(InstancePermissions.All.Count, admin.Count);
        // The owner's extra three are never stored as grants.
        Assert.All(InstancePermissions.Reserved, p => Assert.False(InstancePermissions.IsAssignable(p.Key)));
    }

    [Fact]
    public async Task Every_administrative_route_names_a_right_the_catalogue_defines()
    {
        using var factory = new TestAppFactory();
        _ = factory.CreateClient(); // builds the host, and with it the routes

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/admin") == true)
            .ToList();
        Assert.NotEmpty(endpoints);

        var unnamed = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var key = endpoint.Metadata.GetMetadata<PermissionRequirement>()?.Key;
            if (key is null)
            {
                // The two documented exceptions, both checked inside their
                // handlers because one route is not one right:
                //   settings  - the read needs any settings right, and the
                //               write is checked field by field.
                //   roles     - reaching the matrix is "may see it or may edit
                //               any row", and the owner's edit right is
                //               reserved, so they can always undo a change
                //               that took permissions.view away from them.
                if (endpoint.RoutePattern.RawText is "/api/admin/settings"
                    || endpoint.RoutePattern.RawText?.StartsWith("/api/admin/roles") == true) continue;
                unnamed.Add(endpoint.RoutePattern.RawText!);
                continue;
            }
            Assert.True(InstancePermissions.IsAssignable(key) || InstancePermissions.IsReserved(key),
                $"{endpoint.RoutePattern.RawText} names '{key}', which is not in the catalogue.");
        }
        Assert.Empty(unnamed);
    }

    [Fact]
    public async Task The_seed_creates_the_built_ins_and_attaches_everyone()
    {
        using var factory = new TestAppFactory();
        var owner = await RegisterAsync(factory.CreateClient(), "owner@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        var roles = await InScopeAsync(factory, db => db.Roles.AsNoTracking().ToListAsync());
        Assert.Equal(3, roles.Count);
        Assert.All(roles, r => Assert.True(r.BuiltIn));

        var users = await InScopeAsync(factory, db => db.Users.AsNoTracking().ToListAsync());
        Assert.All(users, u => Assert.NotNull(u.RoleId));
        // Each account's role belongs to its own tier.
        foreach (var u in users)
            Assert.Equal(u.Role, roles.Single(r => r.Id == u.RoleId).Tier);
        Assert.Equal("Owner", roles.Single(r => r.Id == users.Single(u => u.Id == owner.Id).RoleId).Name);
        Assert.Equal("User", roles.Single(r => r.Id == users.Single(u => u.Id == member.Id).RoleId).Name);
    }

    [Fact]
    public async Task The_seed_leaves_an_edited_role_alone()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "owner@example.com");
        await RevokeAsync(factory, UserRole.Admin, InstancePermissions.BackupsPolicy);

        using var scope = factory.Services.CreateScope();
        await RoleSeed.EnsureAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<PermissionCache>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var admin = await InScopeAsync(factory, db => db.Roles.AsNoTracking().Include(r => r.Permissions)
            .FirstAsync(r => r.Key == Role.Keys.Admin));
        Assert.DoesNotContain(admin.Permissions, p => p.Key == InstancePermissions.BackupsPolicy);
        Assert.Equal(3, await InScopeAsync(factory, db => db.Roles.CountAsync()));
    }

    // --- What the owner sees and may change.

    [Fact]
    public async Task The_matrix_reports_the_catalogue_the_roles_and_who_may_edit_them()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = 1 }))
            .EnsureSuccessStatusCode();

        var asOwner = await MatrixAsync(ownerClient);
        Assert.Equal(InstancePermissions.All.Count, asOwner.Catalogue.Count);
        Assert.Equal(3, asOwner.Reserved.Count);
        Assert.All(asOwner.Roles, r => Assert.True(r.Editable));
        Assert.Null(asOwner.ReviewedAt);

        // An administrator may shape user roles and not their own.
        var asAdmin = await MatrixAsync(adminClient);
        Assert.True(asAdmin.Roles.Single(r => r.Key == "user").Editable);
        Assert.False(asAdmin.Roles.Single(r => r.Key == "admin").Editable);
        Assert.False(asAdmin.Roles.Single(r => r.Key == "owner").Editable);
    }

    [Fact]
    public async Task An_administrator_cannot_widen_their_own_role()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = 1 }))
            .EnsureSuccessStatusCode();

        var matrix = await MatrixAsync(adminClient);
        var adminRole = matrix.Roles.Single(r => r.Key == "admin");
        var userRole = matrix.Roles.Single(r => r.Key == "user");

        var refused = await adminClient.PutAsJsonAsync(
            $"/api/admin/roles/{adminRole.Id}/permissions", new { Permissions = adminRole.Permissions });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Only the owner", await refused.Content.ReadAsStringAsync());

        // The user row is theirs to shape.
        (await adminClient.PutAsJsonAsync($"/api/admin/roles/{userRole.Id}/permissions",
            new { Permissions = userRole.Permissions.Where(p => p != InstancePermissions.SpacesCreate).ToArray() }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Saving_the_matrix_audits_a_diff_alerts_and_records_the_review()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var matrix = await MatrixAsync(ownerClient);
        var userRole = matrix.Roles.Single(r => r.Key == "user");

        // A user-tier role gaining an administration right is the quiet
        // escalation this alert exists for.
        (await ownerClient.PutAsJsonAsync($"/api/admin/roles/{userRole.Id}/permissions",
            new { Permissions = userRole.Permissions.Append(InstancePermissions.UsersView).ToArray() }))
            .EnsureSuccessStatusCode();

        var entry = await InScopeAsync(factory, db => db.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.Action == "permissions.changed"));
        Assert.Contains("users.view", entry.MetadataJson);
        Assert.Contains("Added", entry.MetadataJson);

        var alerts = await ownerClient.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        Assert.Contains(alerts!, a => a.Kind == "permissions.expanded");

        Assert.NotNull((await MatrixAsync(ownerClient)).ReviewedAt);
    }

    [Fact]
    public async Task The_owner_can_take_a_right_from_their_own_role_and_put_it_back()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var matrix = await MatrixAsync(ownerClient);
        var ownerRole = matrix.Roles.Single(r => r.Key == "owner");

        (await ownerClient.PutAsJsonAsync($"/api/admin/roles/{ownerRole.Id}/permissions",
            new { Permissions = ownerRole.Permissions.Where(p => p != InstancePermissions.BackupsPolicy).ToArray() }))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.PutAsJsonAsync(
            "/api/admin/backups/policy", new { Enabled = true, KeepCount = 5, KeepDays = 30 })).StatusCode);

        // The way back is never closed: editing the matrix is reserved.
        (await ownerClient.PostAsync($"/api/admin/roles/{ownerRole.Id}/reset", null)).EnsureSuccessStatusCode();
        (await ownerClient.PutAsJsonAsync("/api/admin/backups/policy",
            new { Enabled = true, KeepCount = 5, KeepDays = 30 })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_reserved_rights_cannot_be_removed_from_the_owner()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var other = await RegisterAsync(factory.CreateClient(), "other@example.com");
        var ownerRole = (await MatrixAsync(ownerClient)).Roles.Single(r => r.Key == "owner");

        // An empty set strips every assignable right; the three reserved ones
        // are not grants and survive.
        (await ownerClient.PutAsJsonAsync($"/api/admin/roles/{ownerRole.Id}/permissions",
            new { Permissions = Array.Empty<string>() })).EnsureSuccessStatusCode();

        var me = await ownerClient.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Contains(InstancePermissions.RolesAssignTier, me!.Permissions);
        Assert.Contains(InstancePermissions.PermissionsEditAdminTier, me.Permissions);
        var promote = await ownerClient.PutAsJsonAsync($"/api/admin/users/{other.Id}/role", new { Role = 1 });
        Assert.True(promote.IsSuccessStatusCode, await promote.Content.ReadAsStringAsync());
        (await ownerClient.PostAsync($"/api/admin/roles/{ownerRole.Id}/reset", null)).EnsureSuccessStatusCode();
    }

    // --- Enforcement, right by right.

    [Fact]
    public async Task Administration_routes_follow_the_administrator_role()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = 1 }))
            .EnsureSuccessStatusCode();

        (await adminClient.GetAsync("/api/admin/backups")).EnsureSuccessStatusCode();
        (await adminClient.GetAsync("/api/admin/dashboard")).EnsureSuccessStatusCode();

        await RevokeAsync(factory, UserRole.Admin, InstancePermissions.BackupsPolicy);
        await RevokeAsync(factory, UserRole.Admin, InstancePermissions.DashboardView);

        // The retention example from the plan: still sees backups, cannot
        // change the policy.
        (await adminClient.GetAsync("/api/admin/backups")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync(
            "/api/admin/backups/policy", new { Enabled = true, KeepCount = 9, KeepDays = 9 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.GetAsync("/api/admin/dashboard")).StatusCode);

        // The owner is unaffected by a change to the administrator role.
        (await ownerClient.GetAsync("/api/admin/dashboard")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Settings_are_checked_field_by_field()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = 1 }))
            .EnsureSuccessStatusCode();

        await RevokeAsync(factory, UserRole.Admin, InstancePermissions.SettingsRegistration);

        // The email server is still theirs to fix...
        (await adminClient.PutAsJsonAsync("/api/admin/settings", new { SmtpHost = "smtp.example.com" }))
            .EnsureSuccessStatusCode();

        // ...and registration is not, named in the refusal.
        var refused = await adminClient.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = false });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("settings.registration", await refused.Content.ReadAsStringAsync());

        // Nothing in a refused request is applied, not even the part they may change.
        var refusedPair = await adminClient.PutAsJsonAsync("/api/admin/settings",
            new { InstanceName = "Renamed", AllowPublicRegistration = false });
        Assert.Equal(HttpStatusCode.Forbidden, refusedPair.StatusCode);
        var settings = await InScopeAsync(factory, db => db.SiteSettings.AsNoTracking().FirstAsync());
        Assert.NotEqual("Renamed", settings.InstanceName);
        Assert.True(settings.AllowPublicRegistration);
    }

    [Fact]
    public async Task A_user_deletes_their_own_pages_and_not_other_peoples()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var memberClient = factory.CreateClient();
        await RegisterAsync(memberClient, "member@example.com");

        var space = (await (await ownerClient.PostAsJsonAsync("/api/spaces",
            new { Key = "SHARED", Name = "Shared" })).Content.ReadFromJsonAsync<SpaceDto>())!;
        // The member needs somewhere they may edit.
        (await ownerClient.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions", new
        {
            PrincipalType = 0,
            PrincipalId = (await memberClient.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id,
            Operation = 1,
        })).EnsureSuccessStatusCode();

        var mine = (await (await memberClient.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, Title = "Mine", ContentJson = Doc })).Content.ReadFromJsonAsync<PageDto>())!;
        var theirs = (await (await ownerClient.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, Title = "Theirs", ContentJson = Doc })).Content.ReadFromJsonAsync<PageDto>())!;

        // Someone else's page: refused by the role, named in the body.
        var refused = await memberClient.DeleteAsync($"/api/pages/{theirs.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("pages.delete_any", await refused.Content.ReadAsStringAsync());

        // Their own: allowed.
        (await memberClient.DeleteAsync($"/api/pages/{mine.Id}")).EnsureSuccessStatusCode();

        // Take that away too and even their own is refused.
        await RevokeAsync(factory, UserRole.Member, InstancePermissions.PagesDeleteOwn);
        var second = (await (await memberClient.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, Title = "Second", ContentJson = Doc })).Content.ReadFromJsonAsync<PageDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.DeleteAsync($"/api/pages/{second.Id}")).StatusCode);

        // Granting delete_any does not reach a space they cannot edit. Spaces
        // are default-open, so this one is given an explicit grant, which is
        // what closes it to everyone else.
        await GrantAsync(factory, UserRole.Member, InstancePermissions.PagesDeleteAny);
        var closed = (await (await ownerClient.PostAsJsonAsync("/api/spaces",
            new { Key = "CLOSED", Name = "Closed" })).Content.ReadFromJsonAsync<SpaceDto>())!;
        (await ownerClient.PostAsJsonAsync($"/api/spaces/{closed.Key}/permissions", new
        {
            PrincipalType = 0,
            PrincipalId = (await ownerClient.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id,
            Operation = 2,
        })).EnsureSuccessStatusCode();
        var hidden = (await (await ownerClient.PostAsJsonAsync("/api/pages",
            new { SpaceId = closed.Id, Title = "Hidden", ContentJson = Doc })).Content.ReadFromJsonAsync<PageDto>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/pages/{hidden.Id}")).StatusCode);
    }

    [Fact]
    public async Task Creating_spaces_and_tokens_follows_the_user_role()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "owner@example.com");
        var memberClient = factory.CreateClient();
        await RegisterAsync(memberClient, "member@example.com");

        (await memberClient.PostAsJsonAsync("/api/spaces", new { Key = "ONE", Name = "One" }))
            .EnsureSuccessStatusCode();
        var token = await memberClient.PostAsJsonAsync("/api/api-tokens", new { Name = "cli" });
        token.EnsureSuccessStatusCode();
        var raw = (await token.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;

        await RevokeAsync(factory, UserRole.Member, InstancePermissions.SpacesCreate);
        await RevokeAsync(factory, UserRole.Member, InstancePermissions.TokensUse);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync(
            "/api/spaces", new { Key = "TWO", Name = "Two" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync(
            "/api/api-tokens", new { Name = "cli2" })).StatusCode);

        // The token they already had goes inert rather than being deleted...
        var bearer = factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new("Bearer", raw);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/auth/me")).StatusCode);

        // ...and works again when the right comes back.
        await GrantAsync(factory, UserRole.Member, InstancePermissions.TokensUse);
        (await bearer.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_anonymous_reader_exports_only_what_a_user_may()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var space = (await (await ownerClient.PostAsJsonAsync("/api/spaces",
            new { Key = "PUB", Name = "Public" })).Content.ReadFromJsonAsync<SpaceDto>())!;
        var page = (await (await ownerClient.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, Title = "Open", ContentJson = Doc })).Content.ReadFromJsonAsync<PageDto>())!;
        (await ownerClient.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true }))
            .EnsureSuccessStatusCode();
        (await ownerClient.PutAsJsonAsync($"/api/admin/spaces/{space.Key}/public", new { IsPublic = true }))
            .EnsureSuccessStatusCode();

        var anon = factory.CreateClient();
        (await anon.GetAsync($"/api/pages/{page.Id}/export?format=markdown")).EnsureSuccessStatusCode();

        await RevokeAsync(factory, UserRole.Member, InstancePermissions.PagesExport);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await anon.GetAsync($"/api/pages/{page.Id}/export?format=markdown")).StatusCode);
        // The owner still may: their own role kept the right.
        (await ownerClient.GetAsync($"/api/pages/{page.Id}/export?format=markdown")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_session_carries_the_rights_and_the_role_name()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var memberClient = factory.CreateClient();
        await RegisterAsync(memberClient, "member@example.com");

        var owner = await ownerClient.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("Owner", owner!.RoleName);
        Assert.Contains(InstancePermissions.BackupsPolicy, owner.Permissions);
        Assert.Contains(InstancePermissions.OwnershipTransfer, owner.Permissions);

        var member = await memberClient.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("User", member!.RoleName);
        Assert.Contains(InstancePermissions.SpacesCreate, member.Permissions);
        Assert.DoesNotContain(InstancePermissions.PagesDeleteAny, member.Permissions);
        Assert.DoesNotContain(InstancePermissions.UsersView, member.Permissions);

        await RevokeAsync(factory, UserRole.Member, InstancePermissions.SpacesCreate);
        var after = await memberClient.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.DoesNotContain(InstancePermissions.SpacesCreate, after!.Permissions);
    }

    [Fact]
    public async Task A_member_reaches_none_of_the_administration_routes()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "owner@example.com");
        var memberClient = factory.CreateClient();
        await RegisterAsync(memberClient, "member@example.com");

        foreach (var route in new[]
                 {
                     "/api/admin/users", "/api/admin/spaces", "/api/admin/dashboard", "/api/admin/backups",
                     "/api/admin/security/overview", "/api/admin/invites", "/api/admin/roles", "/api/audit",
                 })
            Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync(route)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/admin/settings")).StatusCode);
    }
}
