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

    private static string Deployed(string? baseUrl, string? httpsPort, string? setting = null) =>
        SiteUrl.Resolve(new SiteSettings { BaseUrl = setting }, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Site:BaseUrl"] = baseUrl, ["Tls:HttpsPort"] = httpsPort }).Build());

    [Theory]
    // A Tesria on ports of its own sends its own port (Compose writes the
    // address as https://DOMAIN, with no port, whatever TESRIA_HTTPS_PORT is).
    [InlineData("https://localhost", "8443", "https://localhost:8443")]
    [InlineData("https://studio.local/", "127.0.0.1:8443", "https://studio.local:8443")]
    [InlineData(null, "8443", "https://localhost:8443")]
    // The standard port stays out of the address, as it always has.
    [InlineData("https://localhost", "443", "https://localhost")]
    [InlineData("https://localhost", null, "https://localhost")]
    [InlineData("https://localhost", "not a port", "https://localhost")]
    // An address given with a port, or over plain HTTP, is used as it is.
    [InlineData("https://wiki.example.com:9443", "8443", "https://wiki.example.com:9443")]
    [InlineData("https://wiki.example.com:443", "8443", "https://wiki.example.com:443")]
    [InlineData("http://localhost:8099", "8443", "http://localhost:8099")]
    public void Links_carry_the_https_port_tesria_is_published_on(string? baseUrl, string? httpsPort, string expected) =>
        Assert.Equal(expected, Deployed(baseUrl, httpsPort));

    [Fact]
    public void The_owners_public_address_still_wins_over_the_port()
    {
        // A real domain behind a proxy on 443, with this Tesria on 8443 behind it.
        Assert.Equal("https://wiki.example.com", Deployed("https://localhost", "8443", setting: "https://wiki.example.com/"));
    }
}
