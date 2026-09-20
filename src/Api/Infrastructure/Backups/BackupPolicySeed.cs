using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// Gives an instance its first backup policy (dev-plan 9.1). Before 9.1 the
/// only retention was <c>BACKUP_RETENTION_DAYS</c> in <c>.env</c>; on the
/// first start after upgrading, that value becomes the policy's days, so an
/// operator who had raised it keeps their history. Runs once: after the
/// policy has a <c>BackupPolicyChangedAt</c>, the variable is never read again.
/// </summary>
public static class BackupPolicySeed
{
    public const int DefaultKeepCount = 3;
    public const int DefaultKeepDays = 14;

    public static async Task<bool> EnsureAsync(ISiteSettingsService settings, IConfiguration config, ILogger logger)
    {
        var current = await settings.GetAsync();
        if (current.BackupPolicyChangedAt is not null) return false;

        var days = Math.Clamp(config.GetValue("Backup:SeedRetentionDays", DefaultKeepDays),
            BackupRetention.MinKeepDays, BackupRetention.MaxKeepDays);
        await settings.UpdateAsync(s =>
        {
            s.BackupRetentionEnabled = true;
            s.BackupKeepCount = DefaultKeepCount;
            s.BackupKeepDays = days;
            s.BackupPolicyChangedAt = DateTimeOffset.UtcNow;
            s.BackupPolicyChangedById = null;
        }, actorId: null);

        logger.LogInformation(
            "Backup retention policy seeded: keep the newest {Count} and everything from the last {Days} days",
            DefaultKeepCount, days);
        return true;
    }
}
