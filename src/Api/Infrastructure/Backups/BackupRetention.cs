using Tesria.Api.Domain;

namespace Tesria.Api.Infrastructure.Backups;

public record BackupPolicy(bool Enabled, int KeepCount, int KeepDays)
{
    public static BackupPolicy From(SiteSettings s) =>
        new(s.BackupRetentionEnabled, s.BackupKeepCount, s.BackupKeepDays);

    /// <summary>The policy an agent last applied, or null if it has never applied one.</summary>
    public static BackupPolicy? AppliedBy(BackupAgent? agent) =>
        agent?.AppliedRetentionEnabled is { } enabled
            ? new BackupPolicy(enabled, agent.AppliedKeepCount ?? 0, agent.AppliedKeepDays ?? 0)
            : null;
}

/// <summary>
/// The retention rule (dev-plan 9.1). A backup is kept if it is one of the
/// newest <see cref="BackupPolicy.KeepCount"/> or was started within the last
/// <see cref="BackupPolicy.KeepDays"/>; it is removed only when it is outside
/// both. Retention disabled removes nothing.
///
/// <b>This rule also exists in SQL and bash</b>, in
/// <c>deploy/backup/common.sh</c>, which is what actually deletes. This copy
/// drives the preview and the status page, and the tests pin its cases.
/// Change one, change both.
/// </summary>
public static class BackupRetention
{
    public const int MinKeepCount = 1, MaxKeepCount = 1000;
    public const int MinKeepDays = 1, MaxKeepDays = 3650;

    /// <summary>How long a stricter policy waits before an agent applies it.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(24);

    /// <summary>
    /// The backups <paramref name="policy"/> would remove from one agent's
    /// present, successful backups.
    /// </summary>
    public static List<Backup> Plan(BackupPolicy policy, IEnumerable<Backup> backups, DateTimeOffset now)
    {
        if (!policy.Enabled) return [];
        var present = backups.Where(b => b.RemovedAt is null && b.Error is null).ToList();
        var cutoff = now - TimeSpan.FromDays(policy.KeepDays);

        if (present.All(b => b.Agent != BackupNames.Physical))
            return present
                .OrderByDescending(b => b.StartedAt)
                .Where((b, i) => i >= policy.KeepCount && b.StartedAt < cutoff)
                .ToList();

        // Physical: the unit is a full backup and everything that depends on
        // it. pgBackRest keeps the newest K fulls, so K is chosen to cover
        // both limits: at least KeepCount, and every full inside KeepDays.
        var fulls = present.Where(b => b.Type == "full").OrderByDescending(b => b.StartedAt).ToList();
        var keep = Math.Max(policy.KeepCount, fulls.Count(f => f.StartedAt >= cutoff));
        var removedFulls = fulls.Skip(keep).Select(f => f.Label).ToHashSet();
        return present
            .Where(b => removedFulls.Contains(b.Type == "full" ? b.Label : b.FullLabel ?? ""))
            .OrderByDescending(b => b.StartedAt)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="next"/> could remove something
    /// <paramref name="current"/> keeps. "Never applied one" counts as stricter.
    /// </summary>
    public static bool IsStricter(BackupPolicy next, BackupPolicy? current) =>
        next.Enabled && (current is null || !current.Enabled
            || next.KeepCount < current.KeepCount || next.KeepDays < current.KeepDays);

    /// <summary>
    /// What an agent enforces right now, as the sidecar computes it: the saved
    /// policy once it is no stricter than the applied one or its grace period
    /// is over; before that, the union of the two (keep anything either keeps).
    /// Null when no policy has been set, which removes nothing.
    /// </summary>
    public static BackupPolicy? Enforced(SiteSettings settings, BackupAgent? agent, DateTimeOffset now)
    {
        if (settings.BackupPolicyChangedAt is null) return null;
        var saved = BackupPolicy.From(settings);
        var applied = BackupPolicy.AppliedBy(agent);
        if (!IsStricter(saved, applied)) return saved;
        if (EffectiveAt(settings, agent) is { } at && at <= now) return saved;
        if (applied is null || !applied.Enabled) return saved with { Enabled = false };
        return new BackupPolicy(true,
            Math.Max(saved.KeepCount, applied.KeepCount), Math.Max(saved.KeepDays, applied.KeepDays));
    }

    /// <summary>
    /// When a stricter saved policy starts removing backups on this agent.
    /// Null when it is not stricter, or the agent has not seen it yet (it
    /// will within a minute, and the clock starts then).
    /// </summary>
    public static DateTimeOffset? EffectiveAt(SiteSettings settings, BackupAgent? agent)
    {
        if (settings.BackupPolicyChangedAt is not { } changed) return null;
        if (!IsStricter(BackupPolicy.From(settings), BackupPolicy.AppliedBy(agent))) return null;
        // An observation older than the latest change belongs to an earlier
        // policy; the sidecar restarts the clock when it next looks.
        if (agent?.PolicyObservedAt is not { } observed || observed < changed) return null;
        return observed + Grace;
    }
}
