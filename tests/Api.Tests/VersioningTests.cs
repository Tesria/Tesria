using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Versioning;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// One version number, reported the same everywhere (dev-plan 16.1), and an
/// audit entry when it changes.
/// </summary>
public class VersioningTests
{
    [Fact]
    public void The_version_is_semantic_and_starts_at_0_5()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", AppVersion.Current);
        Assert.DoesNotContain('+', AppVersion.Current);
        var parts = AppVersion.MajorMinor.Split('.').Select(int.Parse).ToArray();
        Assert.True(parts[0] > 0 || parts[1] >= 5, $"{AppVersion.Current} is older than 0.5");
    }

    [Fact]
    public async Task The_first_start_remembers_the_version_without_calling_it_an_upgrade()
    {
        await using var app = new TestAppFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().GetAsync();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(AppVersion.Current, settings.RunningVersion);
        Assert.Null(settings.PreviousVersion);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "instance.upgraded"));
    }

    [Fact]
    public async Task A_new_version_is_recorded_with_where_it_came_from()
    {
        await using var app = new TestAppFactory();
        _ = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var settings = services.GetRequiredService<ISiteSettingsService>();
        var db = services.GetRequiredService<AppDbContext>();
        await settings.UpdateAsync(s => s.RunningVersion = "0.4.9", null);

        await VersionSeed.EnsureAsync(settings, services.GetRequiredService<IAuditLogger>(), db, NullLogger.Instance);

        var now = await settings.GetAsync();
        Assert.Equal("0.4.9", now.PreviousVersion);
        Assert.Equal(AppVersion.Current, now.RunningVersion);
        Assert.NotNull(now.VersionChangedAt);
        var entry = await db.AuditLogs.SingleAsync(a => a.Action == "instance.upgraded");
        using var meta = JsonDocument.Parse(entry.MetadataJson!);
        Assert.Equal("0.4.9", meta.RootElement.GetProperty("From").GetString());
        Assert.Equal(AppVersion.Current, meta.RootElement.GetProperty("To").GetString());

        // Starting again on the same version records nothing more.
        await VersionSeed.EnsureAsync(settings, services.GetRequiredService<IAuditLogger>(), db, NullLogger.Instance);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "instance.upgraded"));
    }

    [Fact]
    public async Task The_instance_the_health_check_and_the_API_spec_agree_on_the_version()
    {
        await using var app = new TestAppFactory();
        var client = app.CreateClient();

        var instance = await client.GetFromJsonAsync<JsonElement>("/api/instance");
        Assert.Equal(AppVersion.Current, instance.GetProperty("version").GetString());

        var spec = await client.GetFromJsonAsync<JsonElement>("/api/openapi.json");
        Assert.Equal(AppVersion.Current, spec.GetProperty("info").GetProperty("version").GetString());

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health");
        Assert.StartsWith(AppVersion.Current, health.GetProperty("version").GetString());
    }
}
