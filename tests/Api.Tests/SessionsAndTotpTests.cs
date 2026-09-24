using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Isopoh.Cryptography.Argon2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Sessions, two-factor sign-in, sudo mode and password-hash upgrades
/// (dev-plan 3.5).
/// </summary>
public class SessionsAndTotpTests
{
    private record RegisteredDto(Guid Id, List<string> RecoveryCodes);
    private record UserDto(Guid Id, int Role, bool TotpEnabled, bool TotpRequired);
    private record SessionDto(Guid Id, bool Current, DateTimeOffset? RevokedAt);
    private record SetupDto(string Secret, string OtpauthUri);
    private record ChallengeDto(bool RequiresTotp, string Challenge);

    private static async Task<RegisteredDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<RegisteredDto>())!;

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = "supersecret") =>
        client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });

    private static string CodeFor(string base32, int stepOffset = 0) =>
        new Totp(Base32Encoding.ToBytes(base32)).ComputeTotp(DateTime.UtcNow.AddSeconds(30 * stepOffset));

    /// <summary>Enrolls the client's account and returns the secret so the test can act as the authenticator.</summary>
    private static async Task<string> EnrollAsync(HttpClient client)
    {
        var setup = await (await client.PostAsJsonAsync("/api/auth/me/totp/setup", new { }))
            .Content.ReadFromJsonAsync<SetupDto>();
        Assert.StartsWith("otpauth://totp/", setup!.OtpauthUri);
        (await client.PostAsJsonAsync("/api/auth/me/totp/enable", new { Code = CodeFor(setup.Secret) }))
            .EnsureSuccessStatusCode();
        return setup.Secret;
    }

    [Fact]
    public async Task An_administrator_can_turn_off_a_users_two_factor_but_not_the_owners()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerAccount = await RegisterAsync(owner, "owner@example.com");
        var admin = factory.CreateClient();
        var adminAccount = await RegisterAsync(admin, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminAccount.Id}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        var member = factory.CreateClient();
        var memberAccount = await RegisterAsync(member, "member@example.com");
        await EnrollAsync(member);
        await EnrollAsync(admin);
        await EnrollAsync(owner);

        // The one who lost the phone: turned off, signed out, and back in with the password alone.
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/admin/users/{memberAccount.Id}/disable-two-factor", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/me")).StatusCode);
        var again = factory.CreateClient();
        var login = await LoginAsync(again, "member@example.com");
        login.EnsureSuccessStatusCode();
        Assert.DoesNotContain("requiresTotp\":true", await login.Content.ReadAsStringAsync());

        // Never the owner, and another administrator only by the owner.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsync($"/api/admin/users/{ownerAccount.Id}/disable-two-factor", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsync($"/api/admin/users/{adminAccount.Id}/disable-two-factor", null)).StatusCode);
    }

    [Fact]
    public async Task Sessions_are_listed_and_revocable_one_at_a_time()
    {
        using var factory = new TestAppFactory();
        var laptop = factory.CreateClient();
        await RegisterAsync(laptop, "a@example.com");
        var phone = factory.CreateClient();
        (await LoginAsync(phone, "a@example.com")).EnsureSuccessStatusCode();

        var sessions = await laptop.GetFromJsonAsync<List<SessionDto>>("/api/auth/me/sessions");
        Assert.Equal(2, sessions!.Count(s => s.RevokedAt == null));
        var other = Assert.Single(sessions, s => !s.Current);

        (await laptop.DeleteAsync($"/api/auth/me/sessions/{other.Id}")).EnsureSuccessStatusCode();

        // The phone is out; the laptop, which did the revoking, is not.
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/auth/me")).StatusCode);
        (await laptop.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_list_keeps_every_live_session_but_only_a_few_ended_ones()
    {
        // A script that signs in often ends many sessions; the list showed them
        // all, hundreds deep.
        using var factory = new TestAppFactory();
        var laptop = factory.CreateClient();
        await RegisterAsync(laptop, "a@example.com");
        for (var n = 0; n < Tesria.Api.Features.Auth.AuthEndpoints.RecentlyEndedShown + 3; n++)
            (await LoginAsync(factory.CreateClient(), "a@example.com")).EnsureSuccessStatusCode();
        (await laptop.DeleteAsync("/api/auth/me/sessions/others")).EnsureSuccessStatusCode();
        var phone = factory.CreateClient();
        (await LoginAsync(phone, "a@example.com")).EnsureSuccessStatusCode();

        var sessions = await laptop.GetFromJsonAsync<List<SessionDto>>("/api/auth/me/sessions");
        Assert.Equal(2, sessions!.Count(s => s.RevokedAt == null));
        Assert.Equal(Tesria.Api.Features.Auth.AuthEndpoints.RecentlyEndedShown, sessions.Count(s => s.RevokedAt != null));
        // Live ones first.
        Assert.All(sessions.Take(2), s => Assert.Null(s.RevokedAt));
    }

    [Fact]
    public async Task Signing_out_kills_a_copied_cookie()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        // Sign out, then present the cookie the container still holds.
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_session_ends_at_its_absolute_lifetime_however_active()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SessionAbsoluteDays"] = "0" });
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        // Zero days: the cookie is past its lifetime on its very next use.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Two_factor_sign_in_needs_the_code_and_refuses_reuse()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");
        var secret = await EnrollAsync(client);

        var fresh = factory.CreateClient();
        var first = await LoginAsync(fresh, "a@example.com");
        first.EnsureSuccessStatusCode();
        var challenge = await first.Content.ReadFromJsonAsync<ChallengeDto>();
        Assert.True(challenge!.RequiresTotp);
        // The password alone signed nobody in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login/totp",
            new { challenge.Challenge, Code = "000000" })).StatusCode);

        var code = CodeFor(secret, stepOffset: 1); // a step not yet used by enrollment
        (await fresh.PostAsJsonAsync("/api/auth/login/totp", new { challenge.Challenge, Code = code }))
            .EnsureSuccessStatusCode();
        (await fresh.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        // The same code again, from another browser: refused. One step, one use.
        var again = factory.CreateClient();
        var c2 = await (await LoginAsync(again, "a@example.com")).Content.ReadFromJsonAsync<ChallengeDto>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("/api/auth/login/totp",
            new { c2!.Challenge, Code = code })).StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_stands_in_for_the_authenticator()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var registered = await RegisterAsync(client, "a@example.com");
        await EnrollAsync(client);

        var fresh = factory.CreateClient();
        var challenge = await (await LoginAsync(fresh, "a@example.com")).Content.ReadFromJsonAsync<ChallengeDto>();
        (await fresh.PostAsJsonAsync("/api/auth/login/totp",
            new { challenge!.Challenge, Code = registered.RecoveryCodes[0] })).EnsureSuccessStatusCode();

        // Spent.
        var later = factory.CreateClient();
        var c2 = await (await LoginAsync(later, "a@example.com")).Content.ReadFromJsonAsync<ChallengeDto>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await later.PostAsJsonAsync("/api/auth/login/totp",
            new { c2!.Challenge, Code = registered.RecoveryCodes[0] })).StatusCode);
    }

    [Fact]
    public async Task Enabling_two_factor_signs_other_sessions_out_but_not_this_one()
    {
        using var factory = new TestAppFactory();
        var laptop = factory.CreateClient();
        await RegisterAsync(laptop, "a@example.com");
        var phone = factory.CreateClient();
        (await LoginAsync(phone, "a@example.com")).EnsureSuccessStatusCode();

        await EnrollAsync(laptop);

        (await laptop.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Disabling_needs_the_password_or_a_code_not_just_a_session()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");
        await EnrollAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/me/totp/disable", new { })).StatusCode);
        (await client.PostAsJsonAsync("/api/auth/me/totp/disable", new { CurrentPassword = "supersecret" }))
            .EnsureSuccessStatusCode();
        Assert.False((await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.TotpEnabled);
    }

    [Fact]
    public async Task Administrators_can_be_required_to_enroll_before_administering()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings", new { RequireTotpForAdmins = true })).EnsureSuccessStatusCode();

        // Locked out of administration, but told why, and the way out is open.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/users")).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<UserDto>("/api/auth/me"))!.TotpRequired);

        await EnrollAsync(admin);
        (await admin.GetAsync("/api/admin/users")).EnsureSuccessStatusCode();
        Assert.False((await admin.GetFromJsonAsync<UserDto>("/api/auth/me"))!.TotpRequired);

        // And cannot switch it back off while the rule stands.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/auth/me/totp/disable", new { CurrentPassword = "supersecret" })).StatusCode);
    }

    [Fact]
    public async Task Destructive_administration_asks_for_the_password_again()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" });
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        // Zero-minute window: every session is past it.
        var denied = await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("reauth_required", await denied.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await admin.PostAsJsonAsync("/api/auth/reauth", new { Password = "wrong" })).StatusCode);
        (await admin.PostAsJsonAsync("/api/auth/reauth", new { Password = "supersecret" })).EnsureSuccessStatusCode();

        // Re-authenticated a moment ago, but the window is zero, so this is
        // refused again; what the test can show is that reauth itself works
        // and a wrong password does not. The positive path is every other
        // test in the suite, which runs inside the default five minutes.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 })).StatusCode);
    }

    [Fact]
    public async Task Reauthentication_extends_the_window_within_a_real_one()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "1", ["Auth:FreshLoginMinutes"] = "0" });
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        (await admin.PostAsJsonAsync("/api/auth/reauth", new { Password = "supersecret" })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_old_weak_hash_is_upgraded_on_sign_in()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync();
            // The kind of hash an older build produced: small memory, one lane.
            user.PasswordHash = Argon2.Hash("supersecret", 2, 8192, 1, Argon2Type.HybridAddressing, 32);
            await db.SaveChangesAsync();
        }

        (await LoginAsync(factory.CreateClient(), "a@example.com")).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = (await db.Users.FirstAsync()).PasswordHash!;
            Assert.Contains($"$m={Argon2PasswordHasher.MemoryCostKiB},t={Argon2PasswordHasher.TimeCost},p={Argon2PasswordHasher.Parallelism}$", hash);
            Assert.False(new Argon2PasswordHasher().NeedsRehash(hash));
        }
    }
}
