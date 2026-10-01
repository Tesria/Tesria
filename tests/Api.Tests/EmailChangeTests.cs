using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Changing the sign-in email waits for a link sent to the new address, and
/// tells the old one (t2-009). Without email on the instance it is at once.
/// </summary>
public class EmailChangeTests
{
    private record UserDto(Guid Id, string Email, string? PendingEmail, DateTimeOffset? PendingEmailExpiresAt);
    private record AuditDto(string Action, string? MetadataJson);

    private static async Task<HttpClient> RegisterAsync(TestAppFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" })).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>The owner, with email on (recorded, not sent), and a member who will change address.</summary>
    private static async Task<(HttpClient Owner, HttpClient Member, RecordingEmailSender Outbox)> InstanceAsync(
        TestAppFactory factory, bool emailOn = true)
    {
        var owner = await RegisterAsync(factory, "owner@example.com");
        (await owner.PutAsJsonAsync("/api/admin/settings",
            new { EmailEnabled = emailOn, BaseUrl = "https://wiki.example.com", LoginRateLimitPerMinute = 1000 }))
            .EnsureSuccessStatusCode();
        var member = await RegisterAsync(factory, "sam@example.com");
        var outbox = factory.Services.GetRequiredService<RecordingEmailSender>();
        outbox.Sent.Clear();
        return (owner, member, outbox);
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string email) =>
        client.PutAsJsonAsync("/api/auth/me/email", new { CurrentPassword = "supersecret", Email = email });

    private static string TokenIn(EmailMessage m) =>
        Regex.Match(m.Text, @"/confirm-email\?token=([0-9a-f]{64})").Groups[1].Value;

    private static Task<HttpResponseMessage> ConfirmAsync(TestAppFactory factory, string token) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/email/confirm", new { Token = token });

    private static async Task<HttpStatusCode> SignInAsync(TestAppFactory factory, string email) =>
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "supersecret" })).StatusCode;

    [Fact]
    public async Task Asking_again_and_again_is_limited_so_the_instance_is_no_mail_relay()
    {
        // t2-R01: each request emails the typed address, and there was no
        // limit, so any signed-in account could send the instance's mail to
        // a stranger as fast as it liked. Ten an hour, changes and resends
        // together.
        using var factory = new TestAppFactory();
        var (_, member, outbox) = await InstanceAsync(factory);
        for (var i = 0; i < 9; i++)
            (await ChangeAsync(member, $"sam{i}@example.com")).EnsureSuccessStatusCode();
        (await member.PostAsync("/api/auth/me/email/resend", null)).EnsureSuccessStatusCode();
        var sent = outbox.Sent.Count;
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ChangeAsync(member, "stranger@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await member.PostAsync("/api/auth/me/email/resend", null)).StatusCode);
        Assert.Equal(sent, outbox.Sent.Count);
        Assert.DoesNotContain(outbox.Sent, m => m.To == "stranger@example.com");
    }

    [Fact]
    public async Task The_change_waits_for_the_link_and_both_addresses_hear_of_it()
    {
        using var factory = new TestAppFactory();
        var (owner, member, outbox) = await InstanceAsync(factory);

        var res = await ChangeAsync(member, "  Sam.New@Example.COM ");
        res.EnsureSuccessStatusCode();
        var pending = (await res.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("sam@example.com", pending.Email);
        Assert.Equal("sam.new@example.com", pending.PendingEmail);
        Assert.True(pending.PendingEmailExpiresAt > DateTimeOffset.UtcNow.AddHours(23));

        // A link to the new address, which does not name the old one, and a
        // notice to the old address saying how to stop it.
        var link = Assert.Single(outbox.Sent, m => m.To == "sam.new@example.com");
        Assert.Contains("https://wiki.example.com/confirm-email?token=", link.Text);
        Assert.DoesNotContain("sam@example.com", link.Text);
        var notice = Assert.Single(outbox.Sent, m => m.To == "sam@example.com");
        Assert.Contains("sam.new@example.com", notice.Text);
        Assert.Contains("change your password", notice.Text);
        Assert.Equal(2, outbox.Sent.Count);

        // Until the link is opened, the old address signs in and the new one does not.
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(factory, "sam@example.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(factory, "sam.new@example.com"));

        var confirmed = await ConfirmAsync(factory, TokenIn(link));
        confirmed.EnsureSuccessStatusCode();
        Assert.Contains("sam.new@example.com", await confirmed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(factory, "sam.new@example.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(factory, "sam@example.com"));
        var me = (await member.GetFromJsonAsync<UserDto>("/api/auth/me"))!;
        Assert.Equal("sam.new@example.com", me.Email);
        Assert.Null(me.PendingEmail);

        // Once only.
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(factory, TokenIn(link))).StatusCode);

        // Audited: asked for, then changed, with the old address.
        var audit = await owner.GetStringAsync("/api/audit?take=50");
        Assert.Contains("user.email_change_requested", audit);
        Assert.Contains("user.email_changed", audit);
        Assert.Contains("confirmed by link", audit);
    }

    [Fact]
    public async Task A_wrong_or_expired_link_changes_nothing()
    {
        using var factory = new TestAppFactory();
        var (_, member, outbox) = await InstanceAsync(factory);
        (await ChangeAsync(member, "sam.new@example.com")).EnsureSuccessStatusCode();
        var token = TokenIn(Assert.Single(outbox.Sent, m => m.To == "sam.new@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(factory, new string('0', 64))).StatusCode);

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .Where(u => u.PendingEmail != null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PendingEmailExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(factory, token)).StatusCode);
        Assert.Equal("sam@example.com", (await member.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Email);
    }

    [Fact]
    public async Task The_address_is_checked_again_when_the_link_is_opened()
    {
        using var factory = new TestAppFactory();
        var (_, member, outbox) = await InstanceAsync(factory);
        (await ChangeAsync(member, "taken.later@example.com")).EnsureSuccessStatusCode();
        var token = TokenIn(Assert.Single(outbox.Sent, m => m.To == "taken.later@example.com"));

        // Someone else registers the address before the link is opened.
        await RegisterAsync(factory, "taken.later@example.com");

        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(factory, token)).StatusCode);
        var me = (await member.GetFromJsonAsync<UserDto>("/api/auth/me"))!;
        Assert.Equal("sam@example.com", me.Email);
        Assert.Null(me.PendingEmail);
    }

    [Fact]
    public async Task Resend_replaces_the_link_and_cancel_or_a_new_password_stops_it()
    {
        using var factory = new TestAppFactory();
        var (_, member, outbox) = await InstanceAsync(factory);
        (await ChangeAsync(member, "sam.new@example.com")).EnsureSuccessStatusCode();
        var first = TokenIn(Assert.Single(outbox.Sent, m => m.To == "sam.new@example.com"));
        outbox.Sent.Clear();

        (await member.PostAsync("/api/auth/me/email/resend", null)).EnsureSuccessStatusCode();
        // Only the new address is written to again.
        var second = TokenIn(Assert.Single(outbox.Sent));
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(factory, first)).StatusCode);

        var canceled = (await (await member.DeleteAsync("/api/auth/me/email/pending")).Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Null(canceled.PendingEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(factory, second)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsync("/api/auth/me/email/resend", null)).StatusCode);

        // The notice tells the old address a new password stops the change: it does.
        outbox.Sent.Clear();
        (await ChangeAsync(member, "sam.new@example.com")).EnsureSuccessStatusCode();
        var third = TokenIn(Assert.Single(outbox.Sent, m => m.To == "sam.new@example.com"));
        (await member.PutAsJsonAsync("/api/auth/me/password",
            new { CurrentPassword = "supersecret", NewPassword = "a-brand-new-secret" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(factory, third)).StatusCode);
        Assert.Null((await member.GetFromJsonAsync<UserDto>("/api/auth/me"))!.PendingEmail);
    }

    [Fact]
    public async Task The_old_rules_still_hold()
    {
        using var factory = new TestAppFactory();
        var (_, member, outbox) = await InstanceAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync("/api/auth/me/email",
            new { CurrentPassword = "wrong-password", Email = "sam.new@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ChangeAsync(member, "owner@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ChangeAsync(member, "SAM@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ChangeAsync(member, "not-an-address")).StatusCode);
        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task Without_email_the_change_is_at_once_and_audited()
    {
        using var factory = new TestAppFactory();
        var (owner, member, outbox) = await InstanceAsync(factory, emailOn: false);

        var res = await ChangeAsync(member, "sam.new@example.com");
        res.EnsureSuccessStatusCode();
        var user = (await res.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("sam.new@example.com", user.Email);
        Assert.Null(user.PendingEmail);
        Assert.Empty(outbox.Sent);
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(factory, "sam.new@example.com"));
        Assert.Contains("email is off", await owner.GetStringAsync("/api/audit?take=50"));
    }
}
