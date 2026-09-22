using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// Turns what the backup sidecars report into alerts (dev-plan 9.1): a failed
/// backup or restore test, an overdue or silent agent, a filling disk.
///
/// One open alert per problem: while an unresolved alert of the same kind for
/// the same agent exists, no new one is raised, so a sidecar that stays down
/// all weekend is one email rather than one an hour.
/// </summary>
public sealed class BackupMonitor(IServiceScopeFactory scopes, ILogger<BackupMonitor> logger)
    : BackgroundService
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>An agent with no row at all is only reported once the app has been up this long.</summary>
    public static readonly TimeSpan NoAgentGrace = TimeSpan.FromMinutes(10);

    /// <summary>When this process started. Settable so tests can age it.</summary>
    public DateTimeOffset ProcessStartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// A pass this long after the previous one means the process was not
    /// running in between: the host slept, or Docker paused the container.
    /// The sidecars' heartbeats are then stale for the same reason, not
    /// because an agent is down.
    /// </summary>
    public static readonly TimeSpan SuspendedAfter = 2 * Interval;

    /// <summary>
    /// WAL segments waiting before an offsite target is called broken. Three
    /// is past any ordinary burst (the stack archives on a 60 second timeout)
    /// and far below any sane <c>archive-push-queue-max</c>, which is the
    /// point: this has to fire while the backlog can still be cleared, not
    /// once segments have been dropped.
    /// </summary>
    public const int WalBacklogWarning = 3;

    /// <summary>
    /// How long an offsite repository may go without receiving WAL before it
    /// is called stale. Generous, because a backlog is the sharper signal and
    /// a quiet instance genuinely produces little WAL.
    /// </summary>
    public static readonly TimeSpan OffsiteWalStale = TimeSpan.FromHours(6);

    /// <summary>
    /// Failed jobs finished after this are new. Starts one interval back, to
    /// cover a restart. Settable so tests can simulate a suspended host.
    /// </summary>
    public DateTimeOffset LastCheck { get; set; } = DateTimeOffset.UtcNow - Interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var detector = scope.ServiceProvider.GetRequiredService<ISecurityDetector>();

            var snapshot = await BackupStatus.LoadAsync(db, ct);
            var since = LastCheck;
            LastCheck = snapshot.Now;

            // After a suspend every clock jumped together, so on this pass a
            // stale heartbeat or a late backup says nothing about the agent.
            // Failed jobs are real whenever they finished, so those still
            // count; the next pass, five minutes on, judges with heartbeats
            // the sidecars have had time to refresh.
            var suspended = snapshot.Now - since > SuspendedAfter;
            if (suspended)
                logger.LogInformation("Backup monitor: {Gap} since the last pass, so the process was suspended; not judging heartbeats or schedules this time",
                    snapshot.Now - since);

            var open = await db.SecurityAlerts.AsNoTracking()
                .Where(a => a.Status != SecurityAlertStatus.Resolved && a.Kind.StartsWith("backup."))
                .Select(a => new { a.Kind, a.Key })
                .ToListAsync(ct);
            var raised = 0;

            async Task Raise(string kind, SecuritySeverity severity, string agent, object metadata)
            {
                if (open.Any(a => a.Kind == kind && a.Key == agent)) return;
                await detector.BackupProblemAsync(kind, severity, agent, metadata);
                open.Add(new { Kind = kind, Key = agent });
                raised++;
            }

            foreach (var name in BackupNames.Agents)
            {
                var health = BackupStatus.For(name, snapshot);
                var agent = health.Agent;

                if (agent is null)
                {
                    if (!suspended && snapshot.Now - ProcessStartedAt > NoAgentGrace)
                        await Raise("backup.agent_offline", SecuritySeverity.Warning, name,
                            new { Agent = name, Problem = "The backup agent has never reported." });
                    continue;
                }

                if (!suspended && (agent.LastSeenAt is not { } seen || snapshot.Now - seen > BackupStatus.OfflineAlertAfter))
                    await Raise("backup.agent_offline", SecuritySeverity.Warning, name,
                        new { Agent = name, LastSeenAt = agent.LastSeenAt });

                if (!suspended && health.Overdue)
                    await Raise("backup.overdue", SecuritySeverity.Critical, name,
                        new { Agent = name, LastSuccessAt = health.LastSuccess?.CompletedAt ?? health.LastSuccess?.StartedAt, agent.IntervalHours });

                foreach (var job in snapshot.Jobs.Where(j => j.Agent == name
                             && j.Status == BackupNames.StatusFailed && j.FinishedAt > since))
                {
                    if (job.Kind == BackupNames.KindRestoreTest)
                        await Raise("backup.restore_test_failed", SecuritySeverity.Critical, name,
                            new { Agent = name, JobId = job.Id, job.Target, job.Error });
                    else
                        await Raise("backup.failed", SecuritySeverity.Warning, name,
                            new { Agent = name, JobId = job.Id, job.Trigger, job.Error });
                }

                if (health.DiskLow)
                    await Raise("backup.disk_low", SecuritySeverity.Warning, name,
                        new { Agent = name, agent.VolumeFreeBytes, agent.VolumeTotalBytes });
            }

            // Offsite targets (dev-plan 9.2). Read from what the sidecars
            // publish; the app has no credentials and talks to no provider.
            foreach (var target in await db.BackupTargets.AsNoTracking().Where(t => t.Enabled).ToListAsync(ct))
            {
                // The gap that matters. WAL is only acknowledged to Postgres
                // once *every* repository has it, so an offsite repository
                // that has stopped accepting segments does not merely fall
                // behind: the backlog grows until archive-push-queue-max
                // trips, and what it drops then is gone from the local
                // repository too. This fires on the backlog, long before that.
                if (target.WalBacklogFiles is { } backlog && backlog >= WalBacklogWarning)
                    await Raise("backup.offsite_archive_gap", SecuritySeverity.Critical, target.Slot,
                        new { target.Slot, Backlog = backlog, target.LastWalAt, target.Location });

                // The repository is reachable but stale: WAL reached it once
                // and has not for a while.
                else if (target.LastWalAt is { } wal && snapshot.Now - wal > OffsiteWalStale)
                    await Raise("backup.offsite_stale", SecuritySeverity.Warning, target.Slot,
                        new { target.Slot, target.LastWalAt, target.LastBackupAt });

                // A network drive that should always be there and is not.
                // Deliberately not raised for a removable drive: one that is
                // unplugged has not failed, it is in a drawer, and alerting
                // on that would train people to ignore this whole class.
                if (target.Slot == "nas" && target.Present == false)
                    await Raise("backup.offsite_absent", SecuritySeverity.Warning, target.Slot,
                        new { target.Slot, target.Location, target.LastBackupAt });

                // Anything the sidecar could not do: a failed offsite backup,
                // a failed verify, a repository it could not read.
                if (!string.IsNullOrWhiteSpace(target.Message))
                    await Raise("backup.offsite_failed", SecuritySeverity.Warning, target.Slot,
                        new { target.Slot, target.Message, target.LastBackupAt, target.LastVerifyAt });
            }

            if (raised > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Backup monitor raised {Count} alert(s)", raised);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Backup monitor failed to run");
        }
    }
}
