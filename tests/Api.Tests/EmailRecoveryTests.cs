using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Password recovery by emailed link (dev-plan 4.2).</summary>
public class EmailRecoveryTests
{
    private record OptionsDto(bool EmailEnabled);

    private static async Task RegisterAsync(HttpClient client, string email) =>
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" })).EnsureSuccessStatusCode();

    /// <summary>Registers the admin and turns email on (recorded, not sent).</summary>
    private static async Task<HttpClient> InstanceWithEmailAsync(TestAppFactory factory)
    {
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings",
            new { EmailEnabled = true, BaseUrl = "https://wiki.example.com", LoginRateLimitPerMinute = 1000 }))
            .EnsureSuccessStatusCode();
        return admin;
    }

    private static string TokenFrom(Tesria.Api.Infrastructure.Email.EmailMessage m) =>
        Regex.Match(m.Text, @"/reset\?token=([0-9a-f]+)").Groups[1].Value;

    [Fact]
    public async Task The_link_arrives_and_resets_the_password_once()
    {
        using var factory = new TestAppFactory();
        await InstanceWithEmailAsync(factory);
        var outbox = factory.Services.GetRequiredService<RecordingEmailSender>();

        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/email", new { Email = "Admin@Example.com" });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var mail = Assert.Single(outbox.Sent);
        Assert.Equal("admin@example.com", mail.To);
        Assert.Contains("https://wiki.example.com/reset?token=", mail.Text);
        var token = TokenFrom(mail);
        Assert.Equal(64, token.Length);

        (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = token, NewPassword = "a-brand-new-secret" })).EnsureSuccessStatusCode();
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Email = "admin@example.com", Password = "a-brand-new-secret" })).EnsureSuccessStatusCode();

        // Single-use.
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = token, NewPassword = "another-one-entirely" })).StatusCode);
    }

    [Fact]
    public async Task An_unknown_address_gets_the_same_answer_and_no_email()
    {
        using var factory = new TestAppFactory();
        await InstanceWithEmailAsync(factory);
        var outbox = factory.Services.GetRequiredService<RecordingEmailSender>();

        var known = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/email", new { Email = "admin@example.com" });
        var unknown = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/email", new { Email = "nobody@example.com" });

        // Identical status and body: the endpoint is not an account oracle.
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Single(outbox.Sent);
    }

    [Fact]
    public async Task With_email_off_nothing_is_sent_and_the_option_is_not_offered()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "admin@example.com");
        var outbox = factory.Services.GetRequiredService<RecordingEmailSender>();

        Assert.False((await factory.CreateClient().GetFromJsonAsync<OptionsDto>("/api/auth/recovery-options"))!.EmailEnabled);
        Assert.Equal(HttpStatusCode.Accepted, (await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/recover/email", new { Email = "admin@example.com" })).StatusCode);
        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task A_newer_request_invalidates_the_older_link()
    {
        using var factory = new TestAppFactory();
        await InstanceWithEmailAsync(factory);
        var outbox = factory.Services.GetRequiredService<RecordingEmailSender>();

        await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/email", new { Email = "admin@example.com" });
        await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/email", new { Email = "admin@example.com" });
        var tokens = outbox.Sent.Select(TokenFrom).ToList();
        Assert.Equal(2, tokens.Count);

        // The first link, perhaps in an inbox the attacker reached, is dead.
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = tokens[0], NewPassword = "a-brand-new-secret" })).StatusCode);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = tokens[1], NewPassword = "a-brand-new-secret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Requests_for_one_address_are_throttled()
    {
        using var factory = new TestAppFactory();
        await InstanceWithEmailAsync(factory);

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i <= Tesria.Api.Infrastructure.Auth.RecoveryAttemptLimiter.MaxAttempts; i++)
            last = (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/email", new { Email = "admin@example.com" })).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}
