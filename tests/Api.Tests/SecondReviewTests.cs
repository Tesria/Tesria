using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The findings of the second pre-release security review (dev-plan 14.1,
/// 2026-09-24), each held here once fixed.
/// </summary>
public class SecondReviewTests
{
    private const int Admin = 1;
    private record UserDto(Guid Id);
    private record Created(string Token);
    private record Settings(string? SmtpHost, bool SmtpPasswordSet, string? BaseUrl);

    private static async Task<(HttpClient Client, Guid Id)> RegisterAsync(TestAppFactory f, string email)
    {
        var c = f.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/register", new { Email = email, DisplayName = email.Split('@')[0], Password = "supersecret" });
        res.EnsureSuccessStatusCode();
        return (c, (await res.Content.ReadFromJsonAsync<UserDto>())!.Id);
    }

    private static async Task<(HttpClient Owner, HttpClient Admin, Guid OwnerId, Guid AdminId)> OwnerAndAdminAsync(TestAppFactory f)
    {
        var (owner, ownerId) = await RegisterAsync(f, "owner@example.com");
        var (admin, adminId) = await RegisterAsync(f, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new { Role = Admin })).EnsureSuccessStatusCode();
        return (owner, admin, ownerId, adminId);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        try { return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var c) ? c.GetString() : null; }
        catch (JsonException) { return null; }
    }

    [Fact]
    public async Task Moving_the_mail_server_forgets_the_saved_password()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        (await owner.PutAsJsonAsync("/api/admin/settings",
            new { SmtpHost = "smtp.example.com", SmtpPort = 587, SmtpUsername = "wiki", SmtpPassword = "hunter22" })).EnsureSuccessStatusCode();
        Assert.True((await owner.GetFromJsonAsync<Settings>("/api/admin/settings"))!.SmtpPasswordSet);

        // Saving the same server again keeps it.
        (await owner.PutAsJsonAsync("/api/admin/settings", new { SmtpHost = "smtp.example.com", SmtpPort = 587 })).EnsureSuccessStatusCode();
        Assert.True((await owner.GetFromJsonAsync<Settings>("/api/admin/settings"))!.SmtpPasswordSet);

        // Another host, and the password is not handed to it.
        (await owner.PutAsJsonAsync("/api/admin/settings", new { SmtpHost = "mail.attacker.example" })).EnsureSuccessStatusCode();
        Assert.False((await owner.GetFromJsonAsync<Settings>("/api/admin/settings"))!.SmtpPasswordSet);

        // A new password with the move is kept.
        (await owner.PutAsJsonAsync("/api/admin/settings", new { SmtpHost = "smtp.other.example", SmtpPassword = "fresh-one" })).EnsureSuccessStatusCode();
        Assert.True((await owner.GetFromJsonAsync<Settings>("/api/admin/settings"))!.SmtpPasswordSet);
    }

    [Fact]
    public async Task Only_the_owner_can_move_the_public_address()
    {
        using var f = new TestAppFactory();
        var (owner, admin, _, _) = await OwnerAndAdminAsync(f);

        var refused = await admin.PutAsJsonAsync("/api/admin/settings", new { BaseUrl = "https://attacker.example" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("owner_only", await CodeOf(refused));

        (await owner.PutAsJsonAsync("/api/admin/settings", new { BaseUrl = "https://wiki.example.com" })).EnsureSuccessStatusCode();
        // The same value sent back by an administrator's save is not a change.
        (await admin.PutAsJsonAsync("/api/admin/settings", new { BaseUrl = "https://wiki.example.com" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_token_cannot_reset_or_sign_out_its_own_account_through_administration()
    {
        using var f = new TestAppFactory();
        var (owner, ownerId) = await RegisterAsync(f, "owner@example.com");
        var made = await (await owner.PostAsJsonAsync("/api/api-tokens", new { Name = "full" })).Content.ReadFromJsonAsync<Created>();
        var bearer = f.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", made!.Token);

        foreach (var action in new[] { "reset-password", "revoke-sessions", "revoke-tokens", "unlock" })
        {
            var res = await bearer.PostAsync($"/api/admin/users/{ownerId}/{action}", null);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Equal("token_not_allowed", await CodeOf(res));
        }
    }

    [Fact]
    public async Task An_administrator_cannot_reactivate_an_administrator_the_owner_suspended()
    {
        using var f = new TestAppFactory();
        var (owner, admin, _, _) = await OwnerAndAdminAsync(f);
        var (_, otherId) = await RegisterAsync(f, "other@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{otherId}/role", new { Role = Admin })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/admin/users/{otherId}/status", new { Status = 1 })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutAsJsonAsync($"/api/admin/users/{otherId}/status", new { Status = 0 })).StatusCode);
    }

    [Fact]
    public async Task An_administrator_cannot_clear_the_owners_lockout()
    {
        using var f = new TestAppFactory();
        var (_, admin, ownerId, _) = await OwnerAndAdminAsync(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/admin/users/{ownerId}/unlock", null)).StatusCode);
    }

    [Fact]
    public async Task A_closed_instance_does_not_say_which_addresses_have_accounts()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        (await owner.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = false })).EnsureSuccessStatusCode();

        var stranger = f.CreateClient();
        var existing = await stranger.PostAsJsonAsync("/api/auth/register", new { Email = "owner@example.com", DisplayName = "x", Password = "supersecret" });
        var unknown = await stranger.PostAsJsonAsync("/api/auth/register", new { Email = "nobody@example.com", DisplayName = "x", Password = "supersecret" });
        Assert.Equal(HttpStatusCode.Forbidden, existing.StatusCode);
        Assert.Equal(unknown.StatusCode, existing.StatusCode);
    }

    [Fact]
    public async Task An_invite_to_a_malformed_address_is_refused_before_it_is_made()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        var res = await owner.PostAsJsonAsync("/api/admin/invites", new { Email = "not an address" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<List<JsonElement>>("/api/admin/invites"))!);
    }
}
