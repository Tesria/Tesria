using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Outbound email plumbing (dev-plan 4.1).</summary>
public class EmailTests
{
    private record ResultDto(bool Sent, string? Error);

    private static async Task RegisterAsync(HttpClient client, string email) =>
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" })).EnsureSuccessStatusCode();

    [Fact]
    public async Task The_test_email_goes_to_the_administrator_who_asked()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Docs Wiki", BaseUrl = "https://wiki.example.com/" }))
            .EnsureSuccessStatusCode();

        var result = await (await admin.PostAsync("/api/admin/settings/email/test", null))
            .Content.ReadFromJsonAsync<ResultDto>();
        Assert.True(result!.Sent);

        var sent = Assert.Single(factory.Services.GetRequiredService<RecordingEmailSender>().Sent);
        Assert.Equal("admin@example.com", sent.To);
        Assert.StartsWith("[Docs Wiki]", sent.Subject);
        Assert.Contains("https://wiki.example.com", sent.Text); // trailing slash trimmed
    }

    [Fact]
    public async Task A_refusal_comes_back_as_a_result_not_an_error()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        factory.Services.GetRequiredService<RecordingEmailSender>().Fail = true;

        var res = await admin.PostAsync("/api/admin/settings/email/test", null);
        res.EnsureSuccessStatusCode();
        var result = await res.Content.ReadFromJsonAsync<ResultDto>();
        Assert.False(result!.Sent);
        Assert.Equal("simulated failure", result.Error);
    }

    [Fact]
    public async Task The_real_sender_declines_cleanly_when_email_is_off()
    {
        // No SMTP server is contacted: the disabled check comes first.
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();
        var sender = ActivatorUtilities.CreateInstance<SmtpEmailSender>(scope.ServiceProvider);
        var result = await sender.SendAsync(new EmailMessage("x@example.com", "s", "t"));
        Assert.False(result.Sent);
        Assert.Contains("turned off", result.Error);
    }

    [Fact]
    public void Links_and_line_breaks_survive_the_html_twin()
    {
        var html = EmailTemplates.Html("Wiki", "Line one\nSee https://wiki.example.com/reset?token=abc <ok>");
        Assert.Contains("Line one<br>", html);
        Assert.Contains("<a href=\"https://wiki.example.com/reset?token=abc\">", html);
        Assert.Contains("&lt;ok&gt;", html); // never interpreted as markup
    }

    [Fact]
    public void The_setting_overrides_the_deploy_time_url()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://deploy.example.com" }).Build();
        Assert.Equal("https://deploy.example.com", SiteUrl.Resolve(new SiteSettings(), config));
        Assert.Equal("https://other.example.com", SiteUrl.Resolve(new SiteSettings { BaseUrl = "https://other.example.com/" }, config));
    }
}
