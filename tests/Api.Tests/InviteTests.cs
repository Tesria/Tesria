using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Closed registration and invites (dev-plan 1.4). Together these make an
/// instance private without needing an email server to add anyone to it.
/// </summary>
public class InviteTests
{
    private record RegisteredDto(Guid Id, string Email, string DisplayName, int Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword, List<string> RecoveryCodes);
    private record InviteDto(string Token, string Path, string? Email, DateTimeOffset ExpiresAt);
    private record InviteRow(Guid Id, string? Email, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, DateTimeOffset CreatedAt);

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string? invite = null) =>
        client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret", InviteToken = invite });

    /// <summary>Registers the admin, then closes public registration.</summary>
    private static async Task<HttpClient> ClosedInstanceAsync(TestAppFactory factory)
    {
        var admin = factory.CreateClient();
        (await RegisterAsync(admin, "admin@example.com")).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = false }))
            .EnsureSuccessStatusCode();
        return admin;
    }

    [Fact]
    public async Task An_invite_lets_someone_register_on_a_closed_instance()
    {
        using var factory = new TestAppFactory();
        var admin = await ClosedInstanceAsync(factory);

        // Closed means closed without one.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await RegisterAsync(factory.CreateClient(), "nobody@example.com")).StatusCode);

        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites", new { }))
            .Content.ReadFromJsonAsync<InviteDto>();
        Assert.Contains(issued!.Token, issued.Path);

        (await RegisterAsync(factory.CreateClient(), "invited@example.com", issued.Token))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_invite_is_single_use()
    {
        using var factory = new TestAppFactory();
        var admin = await ClosedInstanceAsync(factory);
        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites", new { }))
            .Content.ReadFromJsonAsync<InviteDto>();

        (await RegisterAsync(factory.CreateClient(), "first@example.com", issued!.Token))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await RegisterAsync(factory.CreateClient(), "second@example.com", issued.Token)).StatusCode);

        var rows = await admin.GetFromJsonAsync<List<InviteRow>>("/api/admin/invites");
        Assert.NotNull(rows!.Single().UsedAt);
    }

    [Fact]
    public async Task An_address_bound_invite_only_works_for_that_address()
    {
        using var factory = new TestAppFactory();
        var admin = await ClosedInstanceAsync(factory);

        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites",
            new { Email = "Wanted@Example.com" })).Content.ReadFromJsonAsync<InviteDto>();
        Assert.Equal("wanted@example.com", issued!.Email); // normalised

        // A forwarded link must not become a registration for whoever received it.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await RegisterAsync(factory.CreateClient(), "someone.else@example.com", issued.Token)).StatusCode);

        (await RegisterAsync(factory.CreateClient(), "wanted@example.com", issued.Token))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_revoked_invite_stops_working()
    {
        using var factory = new TestAppFactory();
        var admin = await ClosedInstanceAsync(factory);
        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites", new { }))
            .Content.ReadFromJsonAsync<InviteDto>();

        var rows = await admin.GetFromJsonAsync<List<InviteRow>>("/api/admin/invites");
        (await admin.DeleteAsync($"/api/admin/invites/{rows!.Single().Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await RegisterAsync(factory.CreateClient(), "invited@example.com", issued!.Token)).StatusCode);
    }

    [Fact]
    public async Task An_invite_is_not_needed_while_registration_is_open()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        (await RegisterAsync(admin, "admin@example.com")).EnsureSuccessStatusCode();

        // Default is open, so this is the unchanged behaviour.
        (await RegisterAsync(factory.CreateClient(), "anyone@example.com")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Only_admins_manage_invites()
    {
        using var factory = new TestAppFactory();
        await ClosedInstanceAsync(factory);

        var invited = factory.CreateClient();
        // A member exists only via an invite on a closed instance, so make one.
        var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login",
            new { Email = "admin@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();
        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites", new { }))
            .Content.ReadFromJsonAsync<InviteDto>();
        (await RegisterAsync(invited, "member@example.com", issued!.Token)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await invited.GetAsync("/api/admin/invites")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await invited.PostAsJsonAsync("/api/admin/invites", new { })).StatusCode);
    }

    [Fact]
    public async Task An_invite_for_an_existing_address_is_refused()
    {
        using var factory = new TestAppFactory();
        var admin = await ClosedInstanceAsync(factory);

        // Pointless and confusing: the address already has an account.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/admin/invites", new { Email = "admin@example.com" })).StatusCode);
    }
}
