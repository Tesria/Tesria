using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// What the admin page, the dashboard and <see cref="BackupMonitor"/> all
/// need to know about one agent, computed once from the rows the sidecars
/// write. Kept apart from the endpoints so the monitor alerts on exactly
/// what the page shows.
/// </summary>
public sealed record AgentHealth(
    string Name,
    BackupAgent? Agent,
    bool Online,
    bool Overdue,
    Backup? LastSuccess,
    BackupJob? LastFailure,
    // A failed backup job newer than the last successful backup.
    bool LastRunFailed,
    double? SuccessRate30Days,
    int PresentCount,
    long PresentBytes,
    DateTimeOffset? OldestRestorePoint,
    DateTimeOffset? LastVerifiedAt,
    bool? LastVerifyOk,
    bool DiskLow);

public static class BackupStatus
{
    /// <summary>An agent that has not checked in for this long is offline.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    /// <summary>The monitor alerts after this much silence (the page greys out sooner).</summary>
    public static readonly TimeSpan OfflineAlertAfter = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan JobHistory = TimeSpan.FromDays(30);

    public sealed record Snapshot(
        List<BackupAgent> Agents, List<Backup> Backups, List<BackupJob> Jobs, DateTimeOffset Now);

    /// <summary>
    /// Everything needed for a status computation in three queries. Jobs are
    /// limited to the last 30 days on Postgres; the SQLite test provider
    /// cannot compare a DateTimeOffset in SQL, so there it filters in memory.
    /// </summary>
    public static async Task<Snapshot> LoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now - JobHistory;
        var agents = await db.BackupAgents.AsNoTracking().ToListAsync(ct);
        var backups = await db.Backups.AsNoTracking().ToListAsync(ct);
        var jobs = db.Database.IsNpgsql()
            ? await db.BackupJobs.AsNoTracking().Where(j => j.RequestedAt >= since).ToListAsync(ct)
            : (await db.BackupJobs.AsNoTracking().ToListAsync(ct)).Where(j => j.RequestedAt >= since).ToList();
        return new Snapshot(agents, backups, jobs, now);
    }

    public static AgentHealth For(string name, Snapshot s)
    {
        var agent = s.Agents.FirstOrDefault(a => a.Name == name);
        var backups = s.Backups.Where(b => b.Agent == name).ToList();
        var present = backups.Where(b => b.RemovedAt is null).ToList();
        var good = backups.Where(b => b.Error is null).ToList();
        var backupJobs = s.Jobs.Where(j => j.Agent == name && j.Kind == BackupNames.KindBackup).ToList();

        var lastSuccess = good.OrderByDescending(b => b.CompletedAt ?? b.StartedAt).FirstOrDefault();
        var lastSuccessAt = lastSuccess?.CompletedAt ?? lastSuccess?.StartedAt;
        var lastFailure = backupJobs
            .Where(j => j.Status == BackupNames.StatusFailed)
            .OrderByDescending(j => j.FinishedAt ?? j.RequestedAt)
            .FirstOrDefault();

        var finished = backupJobs.Count(j => j.Status is BackupNames.StatusSucceeded or BackupNames.StatusFailed);
        double? rate = finished == 0 ? null
            : (double)backupJobs.Count(j => j.Status == BackupNames.StatusSucceeded) / finished;

        var online = agent?.LastSeenAt is { } seen && s.Now - seen <= OnlineWindow;

        // Overdue: nothing good within two intervals. Not during the first
        // interval after the agent started, when there may be nothing yet.
        var overdue = false;
        if (agent is { IntervalHours: > 0, StartedAt: { } started })
        {
            var interval = TimeSpan.FromHours(agent.IntervalHours);
            overdue = s.Now - started > interval
                && (lastSuccessAt is null || s.Now - lastSuccessAt > 2 * interval);
        }

        var verified = backups.Where(b => b.LastVerifiedAt is not null)
            .OrderByDescending(b => b.LastVerifiedAt).FirstOrDefault();

        // The oldest point a restore can reach. Physical: the start of the
        // oldest full still present (WAL before it is expired with it).
        var presentGood = present.Where(b => b.Error is null).ToList();
        DateTimeOffset? oldest = name == BackupNames.Physical
            ? presentGood.Where(b => b.Type == "full").Select(b => (DateTimeOffset?)b.StartedAt).Min()
            : presentGood.Select(b => (DateTimeOffset?)b.StartedAt).Min();

        var diskLow = false;
        if (agent is { VolumeFreeBytes: { } free, VolumeTotalBytes: { } total } && total > 0)
        {
            var newestSize = present.OrderByDescending(b => b.StartedAt).FirstOrDefault()?.SizeBytes ?? 0;
            diskLow = free < total / 10 || free < 2 * newestSize;
        }

        return new AgentHealth(
            name, agent, online, overdue, lastSuccess, lastFailure,
            LastRunFailed: lastFailure?.FinishedAt is { } failedAt && (lastSuccessAt is null || failedAt > lastSuccessAt),
            SuccessRate30Days: rate,
            PresentCount: present.Count,
            PresentBytes: present.Sum(b => b.SizeBytes),
            OldestRestorePoint: oldest,
            LastVerifiedAt: verified?.LastVerifiedAt,
            LastVerifyOk: verified?.LastVerifyOk,
            DiskLow: diskLow);
    }
}
