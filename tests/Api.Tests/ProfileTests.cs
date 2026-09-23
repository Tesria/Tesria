using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Editing your own profile (dev-plan 1.1). The load-bearing piece is
/// <see cref="User.SecurityStamp"/>: it is what lets a stateless cookie scheme
/// revoke a session, and suspension (2.2), force-logout (3.3) and 2FA (3.5)
/// all reuse it.
/// </summary>
public class ProfileTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role, string? AvatarHash, int? AvatarVariant);

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email, string password = "supersecret")
    {
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = "Original Name", Password = password });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UserDto>())!;
    }

    [Fact]
    public async Task Display_name_can_be_changed_and_the_session_reflects_it_immediately()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        var res = await client.PutAsJsonAsync("/api/auth/me", new { DisplayName = "  New Name  " });
        res.EnsureSuccessStatusCode();
        Assert.Equal("New Name", (await res.Content.ReadFromJsonAsync<UserDto>())!.DisplayName); // trimmed

        // The cookie carries the name, so it must be re-issued: otherwise the
        // topbar would show the old one until the next sign-in.
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal("New Name", me!.DisplayName);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/auth/me", new { DisplayName = "   " })).StatusCode);
    }

    [Fact]
    public async Task Changing_email_needs_the_current_password_and_refuses_a_collision()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        var taken = factory.CreateClient();
        await RegisterAsync(taken, "taken@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/auth/me/email",
            new { CurrentPassword = "wrong", Email = "new@example.com" })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/auth/me/email",
            new { CurrentPassword = "supersecret", Email = "taken@example.com" })).StatusCode);

        var ok = await client.PutAsJsonAsync("/api/auth/me/email",
            new { CurrentPassword = "supersecret", Email = "  NEW@Example.COM  " });
        ok.EnsureSuccessStatusCode();
        // Stored lower-cased, like registration.
        Assert.Equal("new@example.com", (await ok.Content.ReadFromJsonAsync<UserDto>())!.Email);

        // And the new address is the one that signs in.
        var fresh = factory.CreateClient();
        var login = await fresh.PostAsJsonAsync("/api/auth/login",
            new { Email = "new@example.com", Password = "supersecret" });
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_sessions_but_not_this_one()
    {
        using var factory = new TestAppFactory();

        var first = factory.CreateClient();
        await RegisterAsync(first, "a@example.com");

        // A second, independent session for the same account: the "other
        // device" whose cookie a password change is supposed to kill.
        var second = factory.CreateClient();
        (await second.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();
        (await second.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var change = await first.PutAsJsonAsync("/api/auth/me/password",
            new { CurrentPassword = "supersecret", NewPassword = "an-even-better-secret" });
        change.EnsureSuccessStatusCode();

        // The session that made the change keeps working…
        (await first.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        // …and the other one stops, on its very next request.
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);

        // The new password is the one that works now.
        var fresh = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" })).StatusCode);
        (await fresh.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "an-even-better-secret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_one_and_enforces_the_minimum()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/auth/me/password",
            new { CurrentPassword = "wrong", NewPassword = "a-long-enough-one" })).StatusCode);

        // Same minimum as registration, from the same constant.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/auth/me/password",
            new { CurrentPassword = "supersecret", NewPassword = "short" })).StatusCode);
    }

    [Fact]
    public async Task Rotating_the_stamp_out_of_band_invalidates_a_live_session()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var user = await RegisterAsync(client, "a@example.com");
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        // Exactly what suspension, force-logout and 2FA enrollment will do.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(x => x.SecurityStamp, Guid.NewGuid().ToString("N")));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_suspended_account_loses_its_live_session_too()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var user = await RegisterAsync(client, "a@example.com");
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(x => x.Status, UserStatus.Suspended));
        }

        // Login already refused suspended accounts; now an existing cookie is
        // refused as well, which is what makes 2.2's suspend action immediate.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_account_with_no_local_password_cannot_change_its_email_or_password()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var user = await RegisterAsync(client, "sso@example.com");

        // Sign in first, then remove the local password: the shape an
        // OIDC-provisioned account has (PasswordHash null, the identity
        // provider owning the credentials). Driving a real OIDC callback would
        // need an identity provider; this exercises the same guard.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, (string?)null));
        }

        var email = await client.PutAsJsonAsync("/api/auth/me/email",
            new { CurrentPassword = "supersecret", Email = "other@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, email.StatusCode);
        // A message the UI can show, not a bare 400: the fields are rendered
        // read-only with this explanation rather than failing on submit.
        Assert.Contains("identity provider", await email.Content.ReadAsStringAsync());

        var password = await client.PutAsJsonAsync("/api/auth/me/password",
            new { CurrentPassword = "supersecret", NewPassword = "a-long-enough-one" });
        Assert.Equal(HttpStatusCode.BadRequest, password.StatusCode);
        Assert.Contains("identity provider", await password.Content.ReadAsStringAsync());

        // Display name is still theirs to change.
        (await client.PutAsJsonAsync("/api/auth/me", new { DisplayName = "SSO User" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_oidc_provisioned_account_gets_a_security_stamp()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider
            .GetRequiredService<Tesria.Api.Infrastructure.Auth.IOidcUserProvisioner>();

        var user = await provisioner.ResolveOrProvisionAsync("sub-1", "sso@example.com", true, "SSO User");

        Assert.Null(user.PasswordHash);
        // Without one, the cookie validation below would reject every SSO
        // session outright.
        Assert.False(string.IsNullOrEmpty(user.SecurityStamp));
    }

    [Fact]
    public async Task Profile_endpoints_require_authentication()
    {
        using var factory = new TestAppFactory();
        var anon = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PutAsJsonAsync("/api/auth/me", new { DisplayName = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PutAsJsonAsync("/api/auth/me/password",
                new { CurrentPassword = "a", NewPassword = "bbbbbbbbbb" })).StatusCode);
    }
}
