using Tesria.Api.Domain;
using Tesria.Api.Features.Admin;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// The app's half of a restore (dev-plan 9.4): it watches the job, restarts
/// the process when the wiki has been replaced, and writes the lasting record
/// of what happened.
///
/// <para><b>Why a restart and not a reload.</b> A restored database is an
/// older database: it may predate a migration, it carries no grants for the
/// runtime role (dumps are restored <c>--no-privileges</c>), its settings are
/// not the ones this process has cached, and every pooled connection points
/// at a database that has just been renamed away. Startup already does all of
/// that, in the right order, and is the path every deploy exercises. Doing it
/// again at runtime would be a second, rarely-run copy of the same sequence.
/// Compose restarts the container, so stopping is safe.</para>
///
/// <para><b>Why the audit entry is written here.</b> The entry the request
/// wrote is on the near side of the swap and is rolled away with everything
/// else; it survives in the safety backup and in the kept copy, where an
/// owner-level account cannot reach it. The lasting entry has to be written
/// into the restored database, which means after the restart, which means
/// here. Keyed on <c>LastRestoreJobId</c> and written only when no entry for
/// that job exists, so any number of restarts produce exactly one.</para>
/// </summary>
public sealed class RestoreCompletion(
    IServiceScopeFactory scopes, RestoreState state,
    IHostApplicationLifetime lifetime, ILogger<RestoreCompletion> logger) : BackgroundService
{
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long the app waits for a sidecar to claim a restore before giving
    /// up on it. A restore nobody picks up must not leave the wiki read-only
    /// for ever; the job is failed and maintenance ends.
    /// </summary>
    public static readonly TimeSpan Unclaimed = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Which database this process connected to at startup, by OID.
    ///
    /// This is the signal that a swap has happened, and getting it right took
    /// three attempts against a live instance. It cannot be anything on
    /// <c>SiteSettings</c>: a restored database is an <em>older</em> one, so
    /// the columns a restore writes its progress into may not exist yet, and
    /// the application would wait for a signal that cannot arrive while the
    /// sidecar waits for the restart that would migrate it. That deadlock is
    /// real. It cannot be the migration count either, for a second and
    /// independent reason: a dump is restored <c>--no-privileges</c>, so the
    /// runtime role has no grants on the restored database and reading any
    /// ordinary table, including <c>__EFMigrationsHistory</c>, is refused.
    ///
    /// <c>pg_database</c> is readable by everyone and needs no schema, and a
    /// database's OID is its identity. The swap renames one database out and
    /// another in, so the OID behind the same name changes. "I am connected
    /// to a different database than the one I started with" is exactly the
    /// thing worth restarting for, said directly.
    /// </summary>
    private long _databaseId = -1;

    /// <summary>Periodic check for a restore that completed unrecorded.</summary>
    private DateTimeOffset _lastRecordCheck = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResumeAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Poll, stoppingToken); } catch (OperationCanceledException) { return; }
            try
            {
                // A restore can finish after this process has already given
                // the wiki back (it restarted the moment the swap happened,
                // and the sidecar recorded the result a little later). One
                // cheap query a minute is what makes sure the lasting audit
                // entry is written in that case too.
                if (!state.InProgress)
                {
                    if (DateTimeOffset.UtcNow - _lastRecordCheck < TimeSpan.FromMinutes(1)) continue;
                    _lastRecordCheck = DateTimeOffset.UtcNow;
                    await RecordPendingAsync(stoppingToken);
                    continue;
                }
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Expected during the swap: the database this is reading is
                // being renamed. Logged at debug and tried again in three
                // seconds, because the alternative is a stream of errors for
                // the one minute the feature exists for.
                logger.LogDebug(ex, "Restore poll failed; the database is probably mid-swap");
            }
        }
    }

    /// <summary>
    /// At startup: pick up a restore this process died in the middle of, and
    /// write the record for one that finished while it was down.
    /// </summary>
    public async Task ResumeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISiteSettingsService>();
            var s = await settings.GetAsync(ct);

            // A restore finished (this process is very likely the restart it
            // asked for). Write the lasting entry if it is not already there.
            if (s.LastRestoreJobId is { } done) await RecordAsync(scope, done, s, ct);

            // A restore was still pending when this process stopped. The
            // sidecar owns it and may well still be working, so maintenance
            // resumes rather than being cleared.
            if (s.RestoreJobId is { } pending && s.RestoreStartedAt is { } startedAt)
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var job = await db.BackupJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == pending, ct);
                if (job is null || job.Status is BackupNames.StatusSucceeded or BackupNames.StatusFailed)
                {
                    await EndAsync(scope, settings, null, ct);
                }
                else
                {
                    logger.LogWarning("Resuming maintenance: restore {JobId} was still running", pending);
                    state.Begin(pending, startedAt, ModeOf(job), job.Target ?? "a backup");
                    await scope.ServiceProvider.GetRequiredService<ICollabNotifier>().MaintenanceAsync(true, ct);
                    return;
                }
            }

            // Every ordinary startup tells the collab sidecar the wiki is
            // writable again. This is what ends the maintenance a restore
            // put it into, and it is idempotent.
            await scope.ServiceProvider.GetRequiredService<ICollabNotifier>().MaintenanceAsync(false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not check for a restore in progress at startup");
        }
    }

    /// <summary>Writes the record for a restore that finished while this process was busy elsewhere.</summary>
    private async Task RecordPendingAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var s = await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().GetAsync(ct);
        if (s.LastRestoreJobId is { } done) await RecordAsync(scope, done, s, ct);
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (state.Current is not { } pending) return;
        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISiteSettingsService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The swap, detected without reading anything the restored database
        // might not have or might not let this role read. See _databaseId.
        var databaseId = await DatabaseIdAsync(db, ct);
        if (_databaseId < 0) _databaseId = databaseId;
        if (databaseId >= 0 && databaseId != _databaseId)
        {
            logger.LogWarning(
                "The database under this process was replaced (was {Was}, now {Now}); restarting into it",
                _databaseId, databaseId);
            lifetime.StopApplication();
            return;
        }

        var job = await db.BackupJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == pending.JobId, ct);

        // The sidecar says it is done. Also a restart, and the usual one when
        // the restored database happens to be the same schema version.
        var s = await settings.GetAsync(ct);
        if (s.LastRestoreJobId == pending.JobId || job is { Status: BackupNames.StatusSucceeded })
        {
            logger.LogWarning("Restore {JobId} finished; restarting to pick up the restored database", pending.JobId);
            lifetime.StopApplication();
            return;
        }

        if (job is { Status: BackupNames.StatusFailed })
        {
            logger.LogError("Restore {JobId} failed: {Error}", pending.JobId, job.Error);
            await EndAsync(scope, settings, job.Error, ct);
            return;
        }

        // Nothing claimed it. Better to give the wiki back than to leave it
        // read-only waiting for a sidecar that is not coming.
        if (job is { Status: BackupNames.StatusRequested }
            && DateTimeOffset.UtcNow - pending.StartedAt > Unclaimed)
        {
            logger.LogError("Restore {JobId} was not claimed within {Minutes} minutes; abandoning it",
                pending.JobId, Unclaimed.TotalMinutes);
            var writable = await db.BackupJobs.FirstAsync(j => j.Id == pending.JobId, ct);
            writable.Status = BackupNames.StatusFailed;
            writable.FinishedAt = DateTimeOffset.UtcNow;
            writable.Error = "No backup agent claimed this restore. Nothing was changed.";
            await db.SaveChangesAsync(ct);
            await EndAsync(scope, settings, writable.Error, ct);
        }
    }

    /// <summary>Gives the wiki back after a restore that did not happen.</summary>
    private async Task EndAsync(IServiceScope scope, ISiteSettingsService settings, string? error, CancellationToken ct)
    {
        await settings.UpdateAsync(x =>
        {
            x.RestoreJobId = null;
            x.RestoreStartedAt = null;
            x.RestoreCancelRequestedAt = null;
        }, null, ct);
        state.Clear();
        await scope.ServiceProvider.GetRequiredService<ICollabNotifier>().MaintenanceAsync(false, ct);
        if (error is not null) logger.LogWarning("Maintenance ended without a restore: {Error}", error);
    }

    /// <summary>
    /// Writes <c>backup.restored</c> and raises the alert, once, for a restore
    /// that has completed. Idempotent by design: the check is whether an entry
    /// for this job id already exists in the chain this database carries.
    /// </summary>
    private async Task RecordAsync(IServiceScope scope, Guid jobId, SiteSettings s, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var already = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.Action == "backup.restored" && a.TargetId == jobId, ct);
        if (already) return;

        var job = await db.BackupJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        var kept = RestoreEndpoints.KeptCopyOf(s);
        var metadata = new
        {
            JobId = jobId,
            From = s.LastRestoreFrom,
            At = s.LastRestoredAt,
            Mode = kept?.Mode ?? (job is null ? null : ModeOf(job)),
            RequestedById = job?.RequestedById,
            KeptCopy = kept?.Database,
        };

        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        audit.RecordAs(job?.RequestedById, "backup.restored", "backup", jobId, metadata);
        // Critical, no cooldown, to the owner and every administrator,
        // whoever did it. The threat this answers is an owner-level account
        // restoring to before something it wants nobody to see: the record of
        // the restore itself is the thing it cannot take back.
        await scope.ServiceProvider.GetRequiredService<ISecurityDetector>()
            .BackupRestoredAsync(job?.RequestedById, metadata);
        await db.SaveChangesAsync(ct);
        logger.LogWarning("The wiki was restored from {From}", s.LastRestoreFrom);
    }

    /// <summary>
    /// The OID of the database this connection is on, or -1 when it cannot be
    /// asked. Raw SQL against <c>pg_database</c>, which every role may read
    /// and which exists whatever the application's schema is.
    /// </summary>
    private async Task<long> DatabaseIdAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            return await db.Database
                .SqlQueryRaw<long>(
                    "SELECT oid::bigint AS \"Value\" FROM pg_database WHERE datname = current_database()")
                .SingleAsync(ct);
        }
        catch (Exception ex)
        {
            // Mid-swap the database is renamed away and this throws. Saying
            // "unknown" is right: it is not evidence either way. Logged at
            // warning rather than swallowed, because when this is the thing
            // that is broken nothing else in the restore makes sense, and the
            // silence is what made it hard to find.
            logger.LogWarning(ex, "Could not read the database id; cannot tell whether it was replaced");
            return -1;
        }
    }

    private static string ModeOf(BackupJob job) => RestoreEndpoints.ModeOf(job.Agent);
}
