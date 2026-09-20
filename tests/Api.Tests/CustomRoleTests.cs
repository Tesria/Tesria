using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Custom roles (dev-plan 11.2): a named set of rights inside a tier. The
/// tier still decides who may act on whom, so the interesting cases are the
/// boundaries: who may create one, who may put someone in it, and what stops
/// a role being deleted out from under the people holding it.
/// </summary>
public class CustomRoleTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role, string[] Permissions, string RoleName);
    private record AdminUserDto(Guid Id, string Email, string DisplayName, int Role, int Status,
        Guid? RoleId, string RoleName);
    private record RoleDto(Guid Id, string? Key, string Name, string? Description, int Tier, bool BuiltIn,
        string[] Permissions, int Members, bool Editable);
    private record MatrixDto(List<RoleDto> Roles);
    private const int Member = 0, Admin = 1, Owner = 2;

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>())!;

    private static async Task<List<RoleDto>> RolesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MatrixDto>("/api/admin/roles"))!.Roles;

    private static async Task<RoleDto> CreateAsync(
        HttpClient client, string name, int tier = Member, Guid? copyFrom = null)
    {
        var res = await client.PostAsJsonAsync("/api/admin/roles",
            new { Name = name, Description = "made by a test", Tier = tier, CopyFrom = copyFrom });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RoleDto>())!;
    }

    /// <summary>An owner and an administrator, the two callers these rules distinguish.</summary>
    private static async Task<(HttpClient Owner, HttpClient Admin, UserDto AdminUser)> InstanceAsync(TestAppFactory factory)
    {
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Admin }))
            .EnsureSuccessStatusCode();
        return (owner, adminClient, admin);
    }

    // --- Creating.

    [Fact]
    public async Task A_new_role_copies_the_tiers_built_in_unless_told_otherwise()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);

        var editors = await CreateAsync(owner, "Editors");
        Assert.Equal(Member, editors.Tier);
        Assert.False(editors.BuiltIn);
        Assert.Null(editors.Key);
        Assert.Equal(0, editors.Members);
        Assert.Equal(
            (await RolesAsync(owner)).Single(r => r.Key == "user").Permissions.Order(),
            editors.Permissions.Order());

        // Copying names a source explicitly.
        var strict = (await RolesAsync(owner)).Single(r => r.Key == "user");
        var copy = await CreateAsync(owner, "Readers", Member, strict.Id);
        Assert.Equal(strict.Permissions.Order(), copy.Permissions.Order());
    }

    [Fact]
    public async Task Names_are_unique_and_required()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        await CreateAsync(owner, "Editors");

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/admin/roles",
            new { Name = "editors", Tier = Member })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/admin/roles",
            new { Name = "  ", Tier = Member })).StatusCode);
        // One account owns the instance, so an owner-tier role describes nobody.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Co-owner", Tier = Owner })).StatusCode);
    }

    [Fact]
    public async Task An_administrator_may_create_user_roles_but_not_administrator_ones()
    {
        using var factory = new TestAppFactory();
        var (owner, adminClient, _) = await InstanceAsync(factory);

        await CreateAsync(adminClient, "Editors");

        var refused = await adminClient.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Deputies", Tier = Admin });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Only the owner", await refused.Content.ReadAsStringAsync());

        // ...and cannot climb the tier by copying an administrator role.
        var adminRole = (await RolesAsync(adminClient)).Single(r => r.Key == "admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Sneaky", Tier = Member, CopyFrom = adminRole.Id })).StatusCode);

        (await owner.PostAsJsonAsync("/api/admin/roles", new { Name = "Deputies", Tier = Admin }))
            .EnsureSuccessStatusCode();
    }

    // --- Renaming and deleting.

    [Fact]
    public async Task Built_in_roles_cannot_be_renamed_or_deleted()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        var user = (await RolesAsync(owner)).Single(r => r.Key == "user");

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(
            $"/api/admin/roles/{user.Id}", new { Name = "Peasants" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.DeleteAsync($"/api/admin/roles/{user.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_role_someone_holds_cannot_be_deleted_until_they_are_moved()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");
        var editors = await CreateAsync(owner, "Editors");

        (await owner.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = editors.Id }))
            .EnsureSuccessStatusCode();

        var refused = await owner.DeleteAsync($"/api/admin/roles/{editors.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("account holds this role", await refused.Content.ReadAsStringAsync());

        // Moved back to the built-in, the role goes.
        var builtIn = (await RolesAsync(owner)).Single(r => r.Key == "user");
        (await owner.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = builtIn.Id }))
            .EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/admin/roles/{editors.Id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await RolesAsync(owner), r => r.Name == "Editors");
    }

    [Fact]
    public async Task Renaming_keeps_the_rights_and_the_members()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");
        var editors = await CreateAsync(owner, "Editors");
        (await owner.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = editors.Id }))
            .EnsureSuccessStatusCode();

        (await owner.PutAsJsonAsync($"/api/admin/roles/{editors.Id}",
            new { Name = "Writers", Description = "people who write" })).EnsureSuccessStatusCode();

        var renamed = (await RolesAsync(owner)).Single(r => r.Id == editors.Id);
        Assert.Equal("Writers", renamed.Name);
        Assert.Equal("people who write", renamed.Description);
        Assert.Equal(1, renamed.Members);
        Assert.Equal(editors.Permissions.Order(), renamed.Permissions.Order());
    }

    // --- Assigning.

    [Fact]
    public async Task A_custom_roles_rights_apply_to_whoever_holds_it()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        var memberClient = factory.CreateClient();
        var member = await RegisterAsync(memberClient, "member@example.com");

        // A user role without "create spaces".
        var readers = await CreateAsync(owner, "Readers");
        (await owner.PutAsJsonAsync($"/api/admin/roles/{readers.Id}/permissions",
            new { Permissions = readers.Permissions.Where(p => p != InstancePermissions.SpacesCreate).ToArray() }))
            .EnsureSuccessStatusCode();

        (await memberClient.PostAsJsonAsync("/api/spaces", new { Key = "BEFORE", Name = "Before" }))
            .EnsureSuccessStatusCode();

        (await owner.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = readers.Id }))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync(
            "/api/spaces", new { Key = "AFTER", Name = "After" })).StatusCode);

        // The session reports the role by name, and the tier is untouched.
        var me = await memberClient.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("Readers", me!.RoleName);
        Assert.Equal(Member, me.Role);
        Assert.DoesNotContain(InstancePermissions.SpacesCreate, me.Permissions);
    }

    [Fact]
    public async Task Assigning_within_a_tier_needs_the_assign_right_and_never_crosses_tiers()
    {
        using var factory = new TestAppFactory();
        var (owner, adminClient, _) = await InstanceAsync(factory);
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");
        var editors = await CreateAsync(owner, "Editors");
        var deputies = (await (await owner.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Deputies", Tier = Admin })).Content.ReadFromJsonAsync<RoleDto>())!;

        // An administrator holds users.assign_roles by default, so moving a
        // user between user-tier roles is theirs to do.
        (await adminClient.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = editors.Id }))
            .EnsureSuccessStatusCode();

        // An administrator-tier role is a promotion, and that is not.
        var refused = await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{member.Id}/role", new { RoleId = deputies.Id });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The owner may, and the account's tier moves with the role.
        (await owner.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = deputies.Id }))
            .EnsureSuccessStatusCode();
        var row = (await owner.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!
            .Single(u => u.Id == member.Id);
        Assert.Equal(Admin, row.Role);
        Assert.Equal("Deputies", row.RoleName);

        // Moving an administrator between administrator roles is the owner's.
        var adminBuiltIn = (await RolesAsync(owner)).Single(r => r.Key == "admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{member.Id}/role", new { RoleId = adminBuiltIn.Id })).StatusCode);
        (await owner.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { RoleId = adminBuiltIn.Id }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_owners_own_role_is_still_out_of_reach()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        var ownerUser = (await owner.GetFromJsonAsync<UserDto>("/api/auth/me"))!;
        var editors = await CreateAsync(owner, "Editors");

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(
            $"/api/admin/users/{ownerUser.Id}/role", new { RoleId = editors.Id })).StatusCode);
    }

    [Fact]
    public async Task A_transfer_hands_over_the_built_in_roles()
    {
        using var factory = new TestAppFactory();
        var (owner, _, admin) = await InstanceAsync(factory);
        // The outgoing owner is sitting in a custom administrator role first.
        var deputies = (await (await owner.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Deputies", Tier = Admin })).Content.ReadFromJsonAsync<RoleDto>())!;
        (await owner.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { RoleId = deputies.Id }))
            .EnsureSuccessStatusCode();

        (await owner.PostAsync($"/api/admin/users/{admin.Id}/transfer-ownership", null))
            .EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Users.AsNoTracking().Include(u => u.InstanceRole).ToListAsync();
        Assert.Equal("Owner", rows.Single(u => u.Id == admin.Id).InstanceRole!.Name);
        Assert.Equal("Administrator", rows.Single(u => u.Email == "owner@example.com").InstanceRole!.Name);
    }

    [Fact]
    public async Task Creating_and_deleting_a_role_is_audited()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await InstanceAsync(factory);
        var editors = await CreateAsync(owner, "Editors");
        (await owner.DeleteAsync($"/api/admin/roles/{editors.Id}")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action.StartsWith("role."))
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Contains("role.created", actions);
        Assert.Contains("role.deleted", actions);
    }
}
