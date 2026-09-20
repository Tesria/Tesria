using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Instance roles (dev-plan 0.1). The decision under test is that an admin is
/// NOT a permission bypass: they see what their grants allow, and reach a
/// private space only through the audited recover-access action, which leaves
/// a normal, revocable grant behind. See docs/architecture.md, "Roles and
/// administrators".
/// </summary>
public class RoleTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record RecoverDto(Guid SpaceId, string Key, string Name, bool AlreadyHadAccess);

    private const int Member = 0;
    private const int Admin = 1;
    private const int Owner = 2;

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email)
    {
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UserDto>())!;
    }

    [Fact]
    public async Task First_registered_user_owns_the_instance_and_the_second_is_a_member()
    {
        using var factory = new TestAppFactory();

        var first = await RegisterAsync(factory.CreateClient(), "first@example.com");
        Assert.Equal(Owner, first.Role);

        var second = await RegisterAsync(factory.CreateClient(), "second@example.com");
        Assert.Equal(Member, second.Role);

        // /auth/me reports it too — the SPA reads the role from there.
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "first@example.com", Password = "supersecret" });
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal(Owner, me!.Role);
    }

    [Fact]
    public async Task First_oidc_provisioned_user_on_an_empty_instance_owns_it()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider
            .GetRequiredService<Tesria.Api.Infrastructure.Auth.IOidcUserProvisioner>();

        var first = await provisioner.ResolveOrProvisionAsync("sub-1", "sso-first@example.com", true, "SSO First");
        Assert.Equal(UserRole.Owner, first.Role);

        var second = await provisioner.ResolveOrProvisionAsync("sub-2", "sso-second@example.com", true, "SSO Second");
        Assert.Equal(UserRole.Member, second.Role);
    }

    [Fact]
    public async Task A_member_is_refused_on_an_admin_route()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "owner@example.com"); // takes the admin slot

        var member = factory.CreateClient();
        await RegisterAsync(member, "member@example.com");
        var space = await member.CreateSpaceAsync();
        Assert.NotEqual(Guid.Empty, space);

        var spaces = await member.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == space).Key;

        var res = await member.PostAsync($"/api/admin/spaces/{key}/recover-access", null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_on_an_admin_route()
    {
        using var factory = new TestAppFactory();
        var res = await factory.CreateClient().PostAsync("/api/admin/spaces/ANY/recover-access", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_read_a_private_space_until_they_recover_access()
    {
        using var factory = new TestAppFactory();

        // The admin registers first (taking the role), then a member creates a
        // space and locks it down to themselves.
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        var member = factory.CreateClient();
        var memberUser = await RegisterAsync(member, "member@example.com");
        var spaceId = await member.CreateSpaceAsync();
        var spaces = await member.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;

        // One grant flips the space from default-open to deny-by-default.
        var grant = await member.PostAsJsonAsync($"/api/spaces/{key}/permissions",
            new { PrincipalType = 0, PrincipalId = memberUser.Id, Operation = 2 });
        grant.EnsureSuccessStatusCode();

        // 404, not 403 — an admin they may not see gets the same masking as
        // anyone else, so the role does not confirm the space's existence.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/spaces/{key}")).StatusCode);

        var recovered = await admin.PostAsync($"/api/admin/spaces/{key}/recover-access", null);
        recovered.EnsureSuccessStatusCode();
        var body = await recovered.Content.ReadFromJsonAsync<RecoverDto>();
        Assert.False(body!.AlreadyHadAccess);

        // Now readable, through an ordinary explicit grant.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/spaces/{key}")).StatusCode);

        // Audited, so the access is not silent.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.AuditLogs
                .Where(a => a.Action == "space.access_recovered" && a.TargetId == spaceId)
                .ToListAsync());
        }

        // Idempotent: a retry is a no-op, not a duplicate grant or a second entry.
        var again = await admin.PostAsync($"/api/admin/spaces/{key}/recover-access", null);
        again.EnsureSuccessStatusCode();
        Assert.True((await again.Content.ReadFromJsonAsync<RecoverDto>())!.AlreadyHadAccess);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.AuditLogs
                .Where(a => a.Action == "space.access_recovered" && a.TargetId == spaceId)
                .ToListAsync());
            Assert.Single(await db.SpacePermissions
                .Where(p => p.SpaceId == spaceId && p.Operation == SpaceOperation.Admin
                            && p.PrincipalId != memberUser.Id)
                .ToListAsync());
        }
    }

    [Fact]
    public async Task Revoking_the_recovered_grant_returns_the_admin_to_no_access()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        var adminUser = await RegisterAsync(admin, "admin@example.com");

        var member = factory.CreateClient();
        var memberUser = await RegisterAsync(member, "member@example.com");
        var spaceId = await member.CreateSpaceAsync();
        var spaces = await member.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;
        await member.PostAsJsonAsync($"/api/spaces/{key}/permissions",
            new { PrincipalType = 0, PrincipalId = memberUser.Id, Operation = 2 });

        await admin.PostAsync($"/api/admin/spaces/{key}/recover-access", null);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/spaces/{key}")).StatusCode);

        Guid grantId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            grantId = (await db.SpacePermissions
                .FirstAsync(p => p.SpaceId == spaceId && p.PrincipalId == adminUser.Id)).Id;
        }

        var revoke = await member.DeleteAsync($"/api/spaces/{key}/permissions/{grantId}");
        revoke.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/spaces/{key}")).StatusCode);
    }
}
