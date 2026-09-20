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

    /// <summary>Failed jobs finished after this are new. Starts one interval back, to cover a restart.</summary>
    private DateTimeOffset _lastCheck = DateTimeOffset.UtcNow - Interval;

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
            var since = _lastCheck;
            _lastCheck = snapshot.Now;

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
                    if (snapshot.Now - ProcessStartedAt > NoAgentGrace)
                        await Raise("backup.agent_offline", SecuritySeverity.Warning, name,
                            new { Agent = name, Problem = "The backup agent has never reported." });
                    continue;
                }

                if (agent.LastSeenAt is not { } seen || snapshot.Now - seen > BackupStatus.OfflineAlertAfter)
                    await Raise("backup.agent_offline", SecuritySeverity.Warning, name,
                        new { Agent = name, LastSeenAt = agent.LastSeenAt });

                if (health.Overdue)
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
