using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Infrastructure.Versioning;

/// <summary>
/// Which Tesria this is (dev-plan 16.1): semantic versioning from 0.5.0, one
/// number, set by the release tag. <see cref="Full"/> is what the build
/// stamped, build metadata included (<c>0.5.0-dev+3f2a1c9</c>);
/// <see cref="Current"/> is the version without it, which is what people are
/// shown and what packs record.
/// </summary>
public static class AppVersion
{
    public static readonly string Full =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0-unknown";

    public static string Current => Full.Split('+')[0];

    /// <summary>A release, as opposed to a development or pre-release build.</summary>
    public static bool IsRelease => !Current.Contains('-');

    /// <summary>"0.5" from "0.5.0-dev": what "Applies to" says in the docs (16.2).</summary>
    public static string MajorMinor => string.Join('.', Current.Split('-')[0].Split('.').Take(2));
}

/// <summary>
/// Remembers which version last ran, and records an upgrade (or a
/// downgrade) in the audit log when it changes, so the owner can see what
/// they came from and went to (dev-plan 16.1). Runs at startup, after the
/// migrations.
/// </summary>
public static class VersionSeed
{
    public static async Task EnsureAsync(ISiteSettingsService settings, IAuditLogger audit, AppDbContext db, ILogger log)
    {
        var s = await settings.GetAsync();
        var now = AppVersion.Current;
        if (s.RunningVersion == now) return;
        var from = s.RunningVersion;
        await settings.UpdateAsync(x =>
        {
            x.PreviousVersion = from;
            x.RunningVersion = now;
            x.VersionChangedAt = DateTimeOffset.UtcNow;
        }, null);
        // The first start with versioning has nothing to compare to.
        if (from is null)
        {
            log.LogInformation("Tesria {Version}", now);
            return;
        }
        audit.RecordAs(null, "instance.upgraded", "instance", null, new { From = from, To = now });
        await db.SaveChangesAsync();
        log.LogInformation("Tesria changed from {From} to {To}", from, now);
    }
}
