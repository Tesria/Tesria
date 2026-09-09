using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Instance settings (dev-plan 0.2): a single typed row an admin edits at
/// runtime, with the SMTP password write-only over the API.
/// </summary>
public class SiteSettingsTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role);

    private record SettingsDto(
        string InstanceName, bool AllowPublicRegistration, bool AllowPublicSpaces,
        bool EmailEnabled, string? SmtpHost, int SmtpPort, string? SmtpUsername,
        bool SmtpPasswordSet, string? SmtpFromAddress, int SmtpTls,
        bool RequireTotpForAdmins, DateTimeOffset UpdatedAt);

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email)
    {
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UserDto>())!;
    }

    /// <summary>Registers the admin (first account) and returns their client.</summary>
    private static async Task<HttpClient> AdminClientAsync(TestAppFactory factory)
    {
        var client = factory.CreateClient();
        var user = await RegisterAsync(client, "admin@example.com");
        Assert.Equal(1, user.Role);
        return client;
    }

    [Fact]
    public async Task Settings_have_working_defaults_before_anyone_saves_them()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminClientAsync(factory);

        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Assert.Equal("Tesria", settings!.InstanceName);
        // Preserves the behaviour that existed before the setting did.
        Assert.True(settings.AllowPublicRegistration);
        // Exposing content to the internet must be a deliberate act.
        Assert.False(settings.AllowPublicSpaces);
        Assert.False(settings.EmailEnabled);
        Assert.Equal(587, settings.SmtpPort);
        Assert.False(settings.SmtpPasswordSet);
    }

    [Fact]
    public async Task Settings_round_trip_and_a_partial_update_leaves_other_fields_alone()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminClientAsync(factory);

        var put = await admin.PutAsJsonAsync("/api/admin/settings", new
        {
            InstanceName = "  Acme Wiki  ",
            AllowPublicSpaces = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 465,
            SmtpTls = 2,
        });
        put.EnsureSuccessStatusCode();
        var saved = await put.Content.ReadFromJsonAsync<SettingsDto>();
        Assert.Equal("Acme Wiki", saved!.InstanceName); // trimmed
        Assert.True(saved.AllowPublicSpaces);
        Assert.Equal(465, saved.SmtpPort);
        Assert.Equal(2, saved.SmtpTls);

        // A second update touching one field must not reset the others.
        var second = await admin.PutAsJsonAsync("/api/admin/settings", new { EmailEnabled = true });
        second.EnsureSuccessStatusCode();

        var after = await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.True(after!.EmailEnabled);
        Assert.Equal("Acme Wiki", after.InstanceName);
        Assert.Equal("smtp.example.com", after.SmtpHost);
        Assert.Equal(465, after.SmtpPort);
    }

    [Fact]
    public async Task The_smtp_password_is_write_only_and_stored_encrypted()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminClientAsync(factory);

        await admin.PutAsJsonAsync("/api/admin/settings", new { SmtpPassword = "hunter2" });

        // The API reports only that one exists.
        var settings = await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.True(settings!.SmtpPasswordSet);

        var raw = await admin.GetStringAsync("/api/admin/settings");
        Assert.DoesNotContain("hunter2", raw);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.SiteSettings.AsNoTracking().SingleAsync();
            // Encrypted at rest, and decryptable by the service that wrote it.
            Assert.NotNull(stored.SmtpPasswordProtected);
            Assert.DoesNotContain("hunter2", stored.SmtpPasswordProtected);

            var svc = scope.ServiceProvider
                .GetRequiredService<Tesria.Api.Infrastructure.Settings.ISiteSettingsService>();
            Assert.Equal("hunter2", svc.Unprotect(stored.SmtpPasswordProtected));
        }

        // An empty string clears it; null (omitted) would have left it alone.
        await admin.PutAsJsonAsync("/api/admin/settings", new { SmtpPassword = "" });
        var cleared = await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.False(cleared!.SmtpPasswordSet);
    }

    [Fact]
    public async Task Only_admins_can_read_or_write_settings()
    {
        using var factory = new TestAppFactory();
        await AdminClientAsync(factory);

        var member = factory.CreateClient();
        await RegisterAsync(member, "member@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Hijacked" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task Closing_registration_refuses_new_accounts()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminClientAsync(factory);

        var before = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "early@example.com", DisplayName = "Early", Password = "supersecret" });
        before.EnsureSuccessStatusCode();

        await admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = false });

        var after = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "late@example.com", DisplayName = "Late", Password = "supersecret" });
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);

        // Reopening restores it — proving the cache invalidates on save rather
        // than serving the closed value until its TTL expires.
        await admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = true });
        var reopened = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "later@example.com", DisplayName = "Later", Password = "supersecret" });
        reopened.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_very_first_account_ignores_closed_registration()
    {
        using var factory = new TestAppFactory();

        // Close registration with no users at all, the way an operator could by
        // editing the row directly — then prove the instance can still be set up.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Startup creates the row (the rate limiter reads it), so edit it.
            var settings = await db.SiteSettings.FirstAsync();
            settings.AllowPublicRegistration = false;
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<SiteSettingsCache>().Invalidate();
        }

        var first = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "founder@example.com", DisplayName = "Founder", Password = "supersecret" });
        first.EnsureSuccessStatusCode();
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<UserDto>())!.Role);

        // ...and that the exemption really is only for the first account.
        var second = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "second@example.com", DisplayName = "Second", Password = "supersecret" });
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task An_invalid_port_or_blank_name_is_rejected()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminClientAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/settings", new { SmtpPort = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "   " })).StatusCode);
    }

    [Fact]
    public async Task Updating_settings_is_audited_without_recording_the_password()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminClientAsync(factory);

        await admin.PutAsJsonAsync("/api/admin/settings",
            new { InstanceName = "Audited", SmtpPassword = "hunter2" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == "settings.updated");

        // Names which fields changed, so an operator can see what was touched…
        Assert.Contains("InstanceName", entry.MetadataJson);
        Assert.Contains("SmtpPassword", entry.MetadataJson);
        // …but never the secret itself.
        Assert.DoesNotContain("hunter2", entry.MetadataJson);
    }
}
