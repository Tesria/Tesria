using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The Owner role (dev-plan 10.1): exactly one, it cannot be demoted,
/// suspended or taken over, and only it changes roles. The guards matter more
/// than the happy path: each one is a way an administrator could otherwise
/// take the instance.
/// </summary>
public class OwnerTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role);
    private record AdminUserDto(Guid Id, string Email, string DisplayName, int Role, int Status);
    private record AlertDto(Guid Id, string Kind, int Severity, string Key);
    private const int Member = 0, Admin = 1, Owner = 2;
    private const int Active = 0, Suspended = 1;

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>())!;

    private static async Task<T> InScopeAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task The_first_account_owns_the_instance()
    {
        using var factory = new TestAppFactory();
        Assert.Equal(Owner, (await RegisterAsync(factory.CreateClient(), "first@example.com")).Role);
        Assert.Equal(Member, (await RegisterAsync(factory.CreateClient(), "second@example.com")).Role);
    }

    [Fact]
    public async Task The_first_sso_account_owns_the_instance()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<IOidcUserProvisioner>();

        var first = await provisioner.ResolveOrProvisionAsync("sub-1", "a@example.com", true, "A");
        var second = await provisioner.ResolveOrProvisionAsync("sub-2", "b@example.com", true, "B");

        Assert.Equal(UserRole.Owner, first.Role);
        Assert.Equal(UserRole.Member, second.Role);
    }

    // --- Upgrading an instance that has administrators but no owner.

    [Fact]
    public async Task The_seed_promotes_the_longest_standing_active_administrator()
    {
        using var factory = new TestAppFactory();
        var owner = await RegisterAsync(factory.CreateClient(), "founder@example.com");
        var second = await RegisterAsync(factory.CreateClient(), "second@example.com");
        var third = await RegisterAsync(factory.CreateClient(), "third@example.com");

        // The instance as it looked before 10.1: administrators, no owner, and
        // the oldest of them suspended.
        await InScopeAsync(factory, async db =>
        {
            var users = await db.Users.ToListAsync();
            foreach (var u in users) u.Role = UserRole.Admin;
            users.Single(u => u.Id == owner.Id).Status = UserStatus.Suspended;
            users.Single(u => u.Id == second.Id).CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
            users.Single(u => u.Id == third.Id).CreatedAt = DateTimeOffset.UtcNow.AddDays(-20);
            await db.SaveChangesAsync();
            return 0;
        });

        var promoted = await SeedAsync(factory);

        // The oldest administrator is suspended, so the oldest active one wins.
        Assert.Equal(third.Id, promoted!.Id);
        Assert.Equal(1, await InScopeAsync(factory, db => db.Users.CountAsync(u => u.Role == UserRole.Owner)));
        Assert.True(await InScopeAsync(factory, db =>
            db.AuditLogs.AnyAsync(a => a.Action == "owner.assigned" && a.TargetId == third.Id)));

        // An instance that predates the setup wizard must not be sent through it.
        Assert.NotNull(await InScopeAsync(factory, async db =>
            (await db.SiteSettings.AsNoTracking().FirstAsync()).SetupCompletedAt));

        // Idempotent: a second start finds an owner and changes nothing.
        Assert.Null(await SeedAsync(factory));
    }

    [Fact]
    public async Task The_seed_does_nothing_on_an_empty_instance()
    {
        using var factory = new TestAppFactory();
        Assert.Null(await SeedAsync(factory));
        // No users, so no wizard has been skipped: setup is still to be done.
        Assert.Null(await InScopeAsync(factory, async db =>
            (await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync())?.SetupCompletedAt));
    }

    // --- Only the owner changes roles.

    [Fact]
    public async Task An_administrator_cannot_change_roles_but_the_owner_can()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Admin }))
            .EnsureSuccessStatusCode();

        // An administrator reaches every other admin route and not this one.
        (await adminClient.GetAsync("/api/admin/users")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{member.Id}/role", new { Role = Admin })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PostAsync(
            $"/api/admin/users/{member.Id}/transfer-ownership", null)).StatusCode);

        // And the owner can demote the administrator again.
        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Member }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task The_owner_cannot_be_demoted_suspended_or_assigned()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        var owner = await RegisterAsync(ownerClient, "owner@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PutAsJsonAsync(
            $"/api/admin/users/{owner.Id}/role", new { Role = Member })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PutAsJsonAsync(
            $"/api/admin/users/{owner.Id}/status", new { Status = Suspended })).StatusCode);
        // Ownership is not a role anyone can hand out.
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PutAsJsonAsync(
            $"/api/admin/users/{member.Id}/role", new { Role = Owner })).StatusCode);

        Assert.Equal(UserRole.Owner, (await InScopeAsync(factory, db =>
            db.Users.AsNoTracking().FirstAsync(u => u.Id == owner.Id))).Role);
    }

    // --- Transfer.

    [Fact]
    public async Task Transferring_swaps_both_roles_audits_and_alerts()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        var owner = await RegisterAsync(ownerClient, "owner@example.com");
        var successorClient = factory.CreateClient();
        var successor = await RegisterAsync(successorClient, "successor@example.com");

        var moved = await (await ownerClient.PostAsync(
            $"/api/admin/users/{successor.Id}/transfer-ownership", null))
            .Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.Equal(Owner, moved!.Role);

        var users = await InScopeAsync(factory, db => db.Users.AsNoTracking().ToListAsync());
        Assert.Equal(UserRole.Owner, users.Single(u => u.Id == successor.Id).Role);
        Assert.Equal(UserRole.Admin, users.Single(u => u.Id == owner.Id).Role);
        Assert.Single(users, u => u.Role == UserRole.Owner);

        Assert.True(await InScopeAsync(factory, db =>
            db.AuditLogs.AnyAsync(a => a.Action == "owner.transferred" && a.TargetId == successor.Id)));
        var alerts = await ownerClient.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        Assert.Contains(alerts!, a => a.Kind == "owner.transferred");

        // Both sessions keep working and mean something different at once: the
        // new owner changes roles, the old one no longer can.
        (await successorClient.PutAsJsonAsync($"/api/admin/users/{owner.Id}/role", new { Role = Member }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.PostAsync(
            $"/api/admin/users/{successor.Id}/transfer-ownership", null)).StatusCode);
    }

    [Fact]
    public async Task Ownership_cannot_go_to_yourself_or_to_a_suspended_account()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        var owner = await RegisterAsync(ownerClient, "owner@example.com");
        var other = await RegisterAsync(factory.CreateClient(), "other@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsync(
            $"/api/admin/users/{owner.Id}/transfer-ownership", null)).StatusCode);

        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{other.Id}/status",
            new { Status = Suspended })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsync(
            $"/api/admin/users/{other.Id}/transfer-ownership", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.PostAsync(
            $"/api/admin/users/{Guid.NewGuid()}/transfer-ownership", null)).StatusCode);
    }

    [Fact]
    public async Task Transferring_ownership_asks_for_the_password_again()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" });
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");
        var other = await RegisterAsync(factory.CreateClient(), "other@example.com");

        var denied = await ownerClient.PostAsync($"/api/admin/users/{other.Id}/transfer-ownership", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("reauth_required", await denied.Content.ReadAsStringAsync());
    }

    // --- An administrator cannot get at the owner's account sideways.

    [Fact]
    public async Task An_administrator_cannot_reset_or_revoke_the_owners_account()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        var owner = await RegisterAsync(ownerClient, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await ownerClient.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Admin }))
            .EnsureSuccessStatusCode();

        // A reset link would be a way into the account, and signing the owner
        // out on a loop would keep them out of their own instance.
        foreach (var path in new[] { "reset-password", "revoke-sessions", "revoke-tokens" })
            Assert.Equal(HttpStatusCode.Forbidden,
                (await adminClient.PostAsync($"/api/admin/users/{owner.Id}/{path}", null)).StatusCode);

        // The owner may do all three to their own account. Sessions last:
        // revoking them rotates the security stamp, which kills the very
        // cookie making these calls.
        foreach (var path in new[] { "reset-password", "revoke-tokens", "revoke-sessions" })
            (await ownerClient.PostAsync($"/api/admin/users/{owner.Id}/{path}", null)).EnsureSuccessStatusCode();
    }

    // --- The owner is an administrator too.

    [Fact]
    public async Task The_owner_passes_the_admin_policy_and_its_two_factor_rule()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAsync(ownerClient, "owner@example.com");

        foreach (var route in new[] { "/api/admin/users", "/api/admin/spaces", "/api/admin/settings",
                     "/api/admin/dashboard", "/api/admin/backups", "/api/admin/security/overview" })
            (await ownerClient.GetAsync(route)).EnsureSuccessStatusCode();

        // Requiring two-factor for administrators binds the owner as well, and
        // says so on /auth/me so the SPA can send them to enroll.
        (await ownerClient.PutAsJsonAsync("/api/admin/settings", new { RequireTotpForAdmins = true }))
            .EnsureSuccessStatusCode();
        var me = await ownerClient.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.True(me!.TotpRequired);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task The_owner_is_counted_and_notified_as_an_administrator()
    {
        using var factory = new TestAppFactory();
        var ownerClient = factory.CreateClient();
        var owner = await RegisterAsync(ownerClient, "owner@example.com");

        var dashboard = await ownerClient.GetFromJsonAsync<DashboardDto>("/api/admin/dashboard");
        Assert.Equal(1, dashboard!.People.Admins);

        // The owner leads the user list, where their role is the thing an
        // administrator came to check.
        var users = await ownerClient.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users");
        Assert.Equal(Owner, users!.First().Role);

        // Security alerts are addressed to administrators; the owner is one.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<Infrastructure.Security.ISecurityDetector>()
                .BackupProblemAsync("backup.failed", Domain.SecuritySeverity.Warning, "logical", null);
            await db.SaveChangesAsync();
        }
        Assert.True(await InScopeAsync(factory, db =>
            db.Notifications.AnyAsync(n => n.UserId == owner.Id && n.Action == "security.alert")));
    }

    private record MeDto(Guid Id, int Role, bool TotpRequired);
    private record PeopleDto(int Total, int Admins);
    private record DashboardDto(PeopleDto People);

    private static async Task<User?> SeedAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await OwnerSeed.EnsureAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
            scope.ServiceProvider.GetRequiredService<ISiteSettingsService>(),
            NullLogger.Instance);
    }
}
