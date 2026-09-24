using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
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
    private record InviteDto(string Token, string Path, string? Email, DateTimeOffset ExpiresAt, bool Emailed = false, string? EmailError = null,
        string? TailnetUrl = null);
    private record InviteEmailDto(bool Enabled, string Subject, string Message);
    private record InviteRow(Guid Id, string? Email, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, DateTimeOffset CreatedAt,
        string? UsedByName = null);

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
        Assert.Equal("wanted@example.com", issued!.Email); // normalized

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

        // Default is open, so this is the unchanged behavior.
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_invite_is_spent_and_names_its_account_whether_or_not_registration_is_open(bool open)
    {
        // Found by the owner, 2026-09-22: with registration open the invite
        // was never looked at, so it stayed "Unused" and could be used again.
        using var factory = new TestAppFactory();
        var admin = open ? factory.CreateClient() : await ClosedInstanceAsync(factory);
        if (open) (await RegisterAsync(admin, "admin@example.com")).EnsureSuccessStatusCode();
        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites", new { }))
            .Content.ReadFromJsonAsync<InviteDto>();

        (await RegisterAsync(factory.CreateClient(), "invited@example.com", issued!.Token)).EnsureSuccessStatusCode();

        var row = Assert.Single(await admin.GetFromJsonAsync<List<InviteRow>>("/api/admin/invites") ?? []);
        Assert.NotNull(row.UsedAt);
        Assert.Equal("invited@example.com", row.UsedByName);
    }

    [Fact]
    public async Task With_registration_open_a_bad_invite_token_does_not_stop_anyone()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        (await RegisterAsync(admin, "admin@example.com")).EnsureSuccessStatusCode();

        (await RegisterAsync(factory.CreateClient(), "someone@example.com", "not-a-real-token")).EnsureSuccessStatusCode();
    }

    // -- emailing an invite --------------------------------------------------

    private static async Task<(HttpClient Admin, RecordingEmailSender Outbox)> ClosedInstanceWithEmailAsync(TestAppFactory factory)
    {
        var admin = await ClosedInstanceAsync(factory);
        (await admin.PutAsJsonAsync("/api/admin/settings",
            new { EmailEnabled = true, BaseUrl = "https://wiki.example.com", InstanceName = "Acme Wiki" })).EnsureSuccessStatusCode();
        return (admin, factory.Services.GetRequiredService<RecordingEmailSender>());
    }

    [Fact]
    public async Task An_emailed_invite_carries_the_inviters_message_and_a_link_that_works()
    {
        using var factory = new TestAppFactory();
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);

        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites",
            new { Email = "Dana@Example.com", SendEmail = true, Message = "Welcome aboard, Dana!\nSee you Monday." }))
            .Content.ReadFromJsonAsync<InviteDto>();
        Assert.True(issued!.Emailed);

        var mail = Assert.Single(outbox.Sent);
        Assert.Equal("dana@example.com", mail.To);
        Assert.StartsWith("[Acme Wiki] ", mail.Subject);
        // The message first, then the link, which the inviter cannot edit out.
        Assert.StartsWith("Welcome aboard, Dana!\nSee you Monday.\n\n", mail.Text);
        Assert.Contains($"https://wiki.example.com/register?invite={issued.Token}", mail.Text);
        Assert.Contains("for dana@example.com", mail.Text);

        (await RegisterAsync(factory.CreateClient(), "dana@example.com", issued.Token)).EnsureSuccessStatusCode();
    }

    // -- Tailscale ----------------------------------------------------------

    /// <summary>An instance whose Tailscale sidecar reports this address.</summary>
    private static TestAppFactory WithTailnet(string dnsName)
    {
        var status = Path.Combine(Path.GetTempPath(), $"ts-{Guid.NewGuid():N}.json");
        File.WriteAllText(status, $$$"""{"BackendState":"Running","Self":{"DNSName":"{{{dnsName}}}.","Online":true}}""");
        return new TestAppFactory(new Dictionary<string, string?> { ["Tailscale:StatusFile"] = status });
    }

    [Fact]
    public async Task With_Tailscale_an_invite_also_carries_the_tailnet_link()
    {
        using var factory = WithTailnet("tesria.example-tailnet.ts.net");
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);

        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites",
            new { Email = "sam@example.com", SendEmail = true }))
            .Content.ReadFromJsonAsync<InviteDto>();

        var tailnet = $"https://tesria.example-tailnet.ts.net/register?invite={issued!.Token}";
        Assert.Equal(tailnet, issued.TailnetUrl);
        // The email has both: the usual address first, then the tailnet one.
        var text = Assert.Single(outbox.Sent).Text;
        var usual = text.IndexOf($"https://wiki.example.com/register?invite={issued.Token}", StringComparison.Ordinal);
        Assert.True(usual >= 0 && text.IndexOf(tailnet, StringComparison.Ordinal) > usual);
        Assert.Contains("through Tailscale", text);
    }

    [Fact]
    public async Task Without_Tailscale_an_invite_has_one_link()
    {
        using var factory = new TestAppFactory();
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);

        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites",
            new { Email = "sam@example.com", SendEmail = true }))
            .Content.ReadFromJsonAsync<InviteDto>();

        Assert.Null(issued!.TailnetUrl);
        Assert.DoesNotContain("Tailscale", Assert.Single(outbox.Sent).Text);
    }

    [Fact]
    public async Task An_empty_message_sends_the_default_one()
    {
        using var factory = new TestAppFactory();
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);
        var template = await admin.GetFromJsonAsync<InviteEmailDto>("/api/admin/invites/email");
        Assert.True(template!.Enabled);
        Assert.Contains("Acme Wiki", template.Message);

        (await admin.PostAsJsonAsync("/api/admin/invites", new { Email = "lee@example.com", SendEmail = true, Message = "  " }))
            .EnsureSuccessStatusCode();
        Assert.StartsWith(template.Message, Assert.Single(outbox.Sent).Text);
    }

    [Fact]
    public async Task An_invite_email_needs_an_address_and_a_message_of_sensible_length()
    {
        using var factory = new TestAppFactory();
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/invites", new { SendEmail = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/invites",
                new { Email = "lee@example.com", SendEmail = true, Message = new string('x', 2001) })).StatusCode);
        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task A_mail_server_that_refuses_leaves_the_invite_usable_and_says_why()
    {
        using var factory = new TestAppFactory();
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);
        outbox.Fail = true;

        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites",
            new { Email = "sam@example.com", SendEmail = true })).Content.ReadFromJsonAsync<InviteDto>();
        Assert.False(issued!.Emailed);
        Assert.Equal("simulated failure", issued.EmailError);

        (await RegisterAsync(factory.CreateClient(), "sam@example.com", issued.Token)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Without_being_asked_nothing_is_emailed()
    {
        using var factory = new TestAppFactory();
        var (admin, outbox) = await ClosedInstanceWithEmailAsync(factory);
        var issued = await (await admin.PostAsJsonAsync("/api/admin/invites", new { Email = "pat@example.com" }))
            .Content.ReadFromJsonAsync<InviteDto>();
        Assert.False(issued!.Emailed);
        Assert.Empty(outbox.Sent);
    }
}
