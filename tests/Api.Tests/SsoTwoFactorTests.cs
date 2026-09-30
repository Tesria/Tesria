using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Features.Auth;
using Tesria.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Single sign-on asks for Tesria's own two-factor code when the account has
/// it on (t2-020). Driving a real provider needs one, so these start where
/// the OpenID Connect handler hands over: the account it resolved.
/// </summary>
public class SsoTwoFactorTests
{
    private record RegisteredDto(Guid Id, List<string> RecoveryCodes);
    private record SetupDto(string Secret, string OtpauthUri);
    private record UserDto(Guid Id, string Email, bool TotpEnabled, bool TotpRequired);

    private static string CodeFor(string base32, int stepOffset = 0) =>
        new Totp(Base32Encoding.ToBytes(base32)).ComputeTotp(DateTime.UtcNow.AddSeconds(30 * stepOffset));

    private static async Task<(HttpClient Client, RegisteredDto Account)> RegisterAsync(TestAppFactory factory, string email)
    {
        var client = factory.CreateClient();
        var account = (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" })).Content.ReadFromJsonAsync<RegisteredDto>())!;
        return (client, account);
    }

    private static async Task<string> EnrollAsync(HttpClient client)
    {
        var setup = (await (await client.PostAsJsonAsync("/api/auth/me/totp/setup", new { }))
            .Content.ReadFromJsonAsync<SetupDto>())!;
        (await client.PostAsJsonAsync("/api/auth/me/totp/enable", new { Code = CodeFor(setup.Secret) })).EnsureSuccessStatusCode();
        return setup.Secret;
    }

    /// <summary>What OnTicketReceived does with the account: the redirect, if any, and the challenge cookie it set.</summary>
    private static async Task<(string? Redirect, string? Cookie)> SingleSignOnAsync(TestAppFactory factory, Guid userId, string returnUrl)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        var redirect = await AuthEndpoints.AfterSingleSignOnAsync(http, user, returnUrl);
        var cookie = http.Response.Headers.SetCookie.ToString();
        return (redirect, cookie.Length == 0 ? null : cookie.Split(';')[0]);
    }

    private static Task<HttpResponseMessage> CodeStepAsync(TestAppFactory factory, string? cookie, string code, HttpClient? client = null)
    {
        client ??= factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { Challenge = "", Code = code }),
        };
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return client.SendAsync(req);
    }

    [Fact]
    public async Task With_two_factor_on_single_sign_on_goes_to_the_code_step_first()
    {
        using var factory = new TestAppFactory();
        var (client, account) = await RegisterAsync(factory, "sam@example.com");
        var secret = await EnrollAsync(client);

        var (redirect, cookie) = await SingleSignOnAsync(factory, account.Id, "/spaces/DEMO#top");
        Assert.Equal("/login?sso=code&returnUrl=%2Fspaces%2FDEMO%23top", redirect);
        Assert.StartsWith(AuthEndpoints.SsoChallengeCookie + "=", cookie);

        // No session was made: that waits for the code.
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserSessions
                .CountAsync(s => s.UserId == account.Id && s.RevokedAt == null));

        // A wrong code is refused and counted, as after a password.
        Assert.Equal(HttpStatusCode.Unauthorized, (await CodeStepAsync(factory, cookie, "000000")).StatusCode);
        // No challenge, no sign-in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await CodeStepAsync(factory, null, CodeFor(secret, 1))).StatusCode);

        var browser = factory.CreateClient();
        var ok = await CodeStepAsync(factory, cookie, CodeFor(secret, 1), browser);
        ok.EnsureSuccessStatusCode();
        Assert.Equal("sam@example.com", (await browser.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Email);

        // Spent: the same challenge does not sign in twice.
        Assert.Equal(HttpStatusCode.Unauthorized, (await CodeStepAsync(factory, cookie, CodeFor(secret, -1))).StatusCode);

        // Audited as a password sign-in's second step is, marked as single sign-on.
        using (var scope = factory.Services.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
                .Where(a => a.Action == "user.login" && a.ActorId == account.Id).Select(a => a.MetadataJson).ToListAsync();
            Assert.Contains(rows, m => m!.Contains("\"Totp\":true") && m.Contains("\"Sso\":true"));
            Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
                .AnyAsync(a => a.Action == "user.login_failed" && a.MetadataJson!.Contains("totp")));
        }
    }

    [Fact]
    public async Task A_recovery_code_works_at_the_single_sign_on_code_step()
    {
        using var factory = new TestAppFactory();
        var (client, account) = await RegisterAsync(factory, "sam@example.com");
        await EnrollAsync(client);

        var (_, cookie) = await SingleSignOnAsync(factory, account.Id, "/spaces");
        (await CodeStepAsync(factory, cookie, account.RecoveryCodes[0])).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Without_two_factor_single_sign_on_signs_in_at_once_and_is_recorded()
    {
        using var factory = new TestAppFactory();
        var (_, account) = await RegisterAsync(factory, "sam@example.com");

        var (redirect, cookie) = await SingleSignOnAsync(factory, account.Id, "/spaces");
        Assert.Null(redirect);
        Assert.Null(cookie);
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
            .Where(a => a.Action == "user.login" && a.ActorId == account.Id).Select(a => a.MetadataJson).ToListAsync();
        Assert.Contains(rows, m => m!.Contains("\"Totp\":false") && m.Contains("\"Sso\":true"));
    }

    [Fact]
    public async Task The_code_page_is_never_sent_off_the_site()
    {
        using var factory = new TestAppFactory();
        var (client, account) = await RegisterAsync(factory, "sam@example.com");
        await EnrollAsync(client);

        var (redirect, _) = await SingleSignOnAsync(factory, account.Id, "//evil.example/");
        Assert.Equal("/login?sso=code&returnUrl=%2Fspaces", redirect);
    }

    [Fact]
    public async Task An_administrator_with_no_tesria_password_can_meet_require_two_factor()
    {
        using var factory = new TestAppFactory();
        var (owner, _) = await RegisterAsync(factory, "owner@example.com");
        var (admin, adminAccount) = await RegisterAsync(factory, "sso.admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminAccount.Id}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync("/api/admin/settings", new { RequireTotpForAdmins = true })).EnsureSuccessStatusCode();

        // The shape of an account made by single sign-on: no Tesria password.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == adminAccount.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, (string?)null));

        // Held back until enrolled, as with a password...
        Assert.True((await admin.GetFromJsonAsync<UserDto>("/api/auth/me"))!.TotpRequired);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/users")).StatusCode);

        // ...and now able to enroll, with a recent sign-in as the proof.
        var secret = await EnrollAsync(admin);
        Assert.False((await admin.GetFromJsonAsync<UserDto>("/api/auth/me"))!.TotpRequired);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/users")).StatusCode);

        // From then on single sign-on asks for the code.
        var (redirect, cookie) = await SingleSignOnAsync(factory, adminAccount.Id, "/admin");
        Assert.NotNull(redirect);
        (await CodeStepAsync(factory, cookie, CodeFor(secret, 1))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task With_no_tesria_password_setting_up_two_factor_needs_a_recent_sign_in()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Auth:FreshLoginMinutes"] = "0" });
        var (client, account) = await RegisterAsync(factory, "sso@example.com");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == account.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, (string?)null));
        await Task.Delay(1100);

        var res = await client.PostAsJsonAsync("/api/auth/me/totp/setup", new { });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("sign in again", await res.Content.ReadAsStringAsync());
    }
}
