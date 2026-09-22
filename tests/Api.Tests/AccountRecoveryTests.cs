using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Offline account recovery (dev-plan 1.3). The recovery path that works with
/// no email server, which for a self-hosted wiki is the usual case.
/// </summary>
public class AccountRecoveryTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role, string? AvatarHash, int? AvatarVariant);
    private record RegisteredDto(Guid Id, string Email, string DisplayName, int Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword, List<string> RecoveryCodes);
    private record CodesDto(List<string> Codes);
    private record StatusDto(int Remaining);
    private record SessionDto(Guid Id, string Email, string DisplayName, int Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword, int RecoveryCodesRemaining);
    private record ResetDto(string Token, string Path, DateTimeOffset ExpiresAt);

    private static async Task<RegisteredDto> RegisterAsync(
        HttpClient client, string email, string password = "supersecret")
    {
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = password });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RegisteredDto>())!;
    }

    [Fact]
    public async Task Registration_issues_eight_codes_once()
    {
        using var factory = new TestAppFactory();
        var registered = await RegisterAsync(factory.CreateClient(), "a@example.com");

        Assert.Equal(AccountRecoveryService.CodeCount, registered.RecoveryCodes.Count);
        Assert.Equal(registered.RecoveryCodes.Count, registered.RecoveryCodes.Distinct().Count());
        // Issued up front, not on request: codes are only useful if they exist
        // before they are needed.
        Assert.All(registered.RecoveryCodes, c => Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", c));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.RecoveryCodes.AsNoTracking().ToListAsync();
        Assert.Equal(AccountRecoveryService.CodeCount, stored.Count);
        // Only hashes are kept: the plaintext existed once, in that response.
        Assert.All(stored, c => Assert.DoesNotContain(
            registered.RecoveryCodes[0].Replace("-", ""), c.CodeHash, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_code_resets_the_password_once_and_signs_other_sessions_out()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var registered = await RegisterAsync(owner, "a@example.com");
        var code = registered.RecoveryCodes[0];

        // A live session belonging to whoever locked the owner out.
        (await owner.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var anon = factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "a@example.com", Code = code, NewPassword = "a-brand-new-secret" });
        res.EnsureSuccessStatusCode();

        // Recovery is for when someone else has your account: every existing
        // session must stop working, or the recovery achieved nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/auth/me")).StatusCode);

        var fresh = factory.CreateClient();
        (await fresh.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "a-brand-new-secret" })).EnsureSuccessStatusCode();

        // Single use.
        var replay = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "a@example.com", Code = code, NewPassword = "another-secret-entirely" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Codes_work_however_they_were_written_down()
    {
        using var factory = new TestAppFactory();
        var registered = await RegisterAsync(factory.CreateClient(), "a@example.com");

        // People copy these off paper months later; dashes and case should not
        // be the reason a valid code is refused.
        var messy = registered.RecoveryCodes[0].Replace("-", "").ToLowerInvariant();

        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "a@example.com", Code = $"  {messy}  ", NewPassword = "a-brand-new-secret" });
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_code_are_indistinguishable()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");

        var wrongCode = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "a@example.com", Code = "AAAA-BBBB-CCCC", NewPassword = "a-brand-new-secret" });
        var noSuchUser = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "nobody@example.com", Code = "AAAA-BBBB-CCCC", NewPassword = "a-brand-new-secret" });

        // Identical, so this cannot be used to discover which addresses exist.
        Assert.Equal(wrongCode.StatusCode, noSuchUser.StatusCode);
        Assert.Equal(await wrongCode.Content.ReadAsStringAsync(), await noSuchUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Repeated_guesses_against_one_account_are_throttled()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");
        var anon = factory.CreateClient();

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < RecoveryAttemptLimiter.MaxAttempts + 2; i++)
        {
            var res = await anon.PostAsJsonAsync("/api/auth/recover/code",
                new { Email = "a@example.com", Code = "AAAA-BBBB-CCCC", NewPassword = "a-brand-new-secret" });
            last = res.StatusCode;
        }

        // Keyed on the email, not the IP: behind the proxy every request shares
        // one address today, so an IP limiter would throttle everybody at once.
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Regenerating_codes_needs_the_password_and_invalidates_the_old_set()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var registered = await RegisterAsync(client, "a@example.com");
        var oldCode = registered.RecoveryCodes[0];

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/auth/me/recovery-codes", new { CurrentPassword = "wrong" })).StatusCode);

        var res = await client.PostAsJsonAsync("/api/auth/me/recovery-codes",
            new { CurrentPassword = "supersecret" });
        res.EnsureSuccessStatusCode();
        var fresh = await res.Content.ReadFromJsonAsync<CodesDto>();
        Assert.Equal(AccountRecoveryService.CodeCount, fresh!.Codes.Count);
        Assert.DoesNotContain(oldCode, fresh.Codes);

        // The old set must stop working, or "my codes leaked" has no remedy.
        var replay = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "a@example.com", Code = oldCode, NewPassword = "a-brand-new-secret" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task A_fresh_login_can_generate_codes_without_retyping_the_password()
    {
        using var factory = new TestAppFactory();

        // An account that predates recovery codes: registered, then stripped of
        // them, which is exactly the state every existing account is in.
        var setup = factory.CreateClient();
        await RegisterAsync(setup, "a@example.com");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecoveryCodes.RemoveRange(db.RecoveryCodes);
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" });
        login.EnsureSuccessStatusCode();

        // The session reports the gap, which is what triggers the prompt.
        Assert.Equal(0, (await login.Content.ReadFromJsonAsync<SessionDto>())!.RecoveryCodesRemaining);

        // The sign-in is the re-authentication: no password is sent, and
        // asking for it seconds after it was typed proves nothing.
        var generated = await client.PostAsJsonAsync("/api/auth/me/recovery-codes", new { });
        generated.EnsureSuccessStatusCode();
        Assert.Equal(AccountRecoveryService.CodeCount,
            (await generated.Content.ReadFromJsonAsync<CodesDto>())!.Codes.Count);

        Assert.Equal(AccountRecoveryService.CodeCount,
            (await client.GetFromJsonAsync<SessionDto>("/api/auth/me"))!.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected_even_in_a_fresh_session()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com"); // registration is a fresh sign-in

        // Freshness substitutes for *omitting* the password, not for getting it
        // wrong: accepting this would tell someone their password was right
        // when it was not.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/auth/me/recovery-codes", new { CurrentPassword = "definitely-wrong" })).StatusCode);

        // Omitting it entirely is the supported path while fresh.
        (await client.PostAsJsonAsync("/api/auth/me/recovery-codes", new { }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_session_that_is_no_longer_fresh_must_supply_the_password()
    {
        // Zero-minute window: the sign-in below is stale the instant it happens,
        // which is the state of a browser tab left open since this morning.
        using var factory = new TestAppFactory(freshLoginMinutes: 0);
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        // Without this, a borrowed unlocked laptop mints a set of codes that
        // work as a permanent password reset for the real owner's account.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/me/recovery-codes", new { })).StatusCode);

        (await client.PostAsJsonAsync("/api/auth/me/recovery-codes",
            new { CurrentPassword = "supersecret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Remaining_codes_are_reported_so_the_profile_can_warn()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var registered = await RegisterAsync(client, "a@example.com");

        Assert.Equal(AccountRecoveryService.CodeCount,
            (await client.GetFromJsonAsync<StatusDto>("/api/auth/me/recovery-codes"))!.Remaining);

        await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "a@example.com", Code = registered.RecoveryCodes[0], NewPassword = "a-brand-new-secret" });

        var after = factory.CreateClient();
        await after.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "a-brand-new-secret" });
        Assert.Equal(AccountRecoveryService.CodeCount - 1,
            (await after.GetFromJsonAsync<StatusDto>("/api/auth/me/recovery-codes"))!.Remaining);
    }

    [Fact]
    public async Task An_admin_can_issue_a_one_time_reset_link()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com"); // first account is admin

        var member = factory.CreateClient();
        var registered = await RegisterAsync(member, "member@example.com");

        var issued = await admin.PostAsync($"/api/admin/users/{registered.Id}/reset-password", null);
        issued.EnsureSuccessStatusCode();
        var reset = await issued.Content.ReadFromJsonAsync<ResetDto>();
        Assert.Contains(reset!.Token, reset.Path);
        Assert.True(reset.ExpiresAt > DateTimeOffset.UtcNow);

        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = reset.Token, NewPassword = "a-brand-new-secret" });
        res.EnsureSuccessStatusCode();

        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Email = "member@example.com", Password = "a-brand-new-secret" })).EnsureSuccessStatusCode();

        // Single use.
        var replay = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = reset.Token, NewPassword = "yet-another-secret" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Issuing_a_second_reset_link_invalidates_the_first()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        var first = await (await admin.PostAsync($"/api/admin/users/{member.Id}/reset-password", null))
            .Content.ReadFromJsonAsync<ResetDto>();
        var second = await (await admin.PostAsync($"/api/admin/users/{member.Id}/reset-password", null))
            .Content.ReadFromJsonAsync<ResetDto>();

        // Otherwise a link handed out earlier still works after it was reissued.
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/recover/token", new { Token = first!.Token, NewPassword = "a-brand-new-secret" })).StatusCode);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = second!.Token, NewPassword = "a-brand-new-secret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Only_admins_can_issue_a_reset()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "admin@example.com");

        var member = factory.CreateClient();
        var registered = await RegisterAsync(member, "member@example.com");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsync($"/api/admin/users/{registered.Id}/reset-password", null)).StatusCode);
    }
}
