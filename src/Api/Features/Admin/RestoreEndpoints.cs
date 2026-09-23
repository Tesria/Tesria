using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Backups;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Admin → Backups → Restore (dev-plan 9.4).
///
/// The app still never touches a backup file: it appends a <c>restore</c> job
/// and a sidecar does the work, exactly as 9.1 established. What is new is
/// the worst thing a web request can cause. Until now that was a backup being
/// taken; from here it is the wiki being replaced by an older copy. So the
/// gate is the substance of this file, and it is deliberately stronger than
/// the one on deleting a space (11.3), which destroys one space:
///
/// <list type="number">
/// <item>a right of its own that only the owner holds by default;</item>
/// <item>the backup's label typed back, proving the right row is on screen;</item>
/// <item>the password or a one-time code in this request, not the sudo window;</item>
/// <item>a safety backup the sidecar takes first and nobody can skip;</item>
/// <item>one at a time;</item>
/// <item>an audit entry on each side of the restore, and a Critical alert to
/// every administrator whoever did it.</item>
/// </list>
///
/// The audit entry written here is on the near side of the swap and will be
/// rolled away with everything else; that is why it is written again from
/// <see cref="RestoreCompletion"/> once the restored database is up. The
/// near-side copy survives in the safety backup and in the kept copy, which
/// is the point: an owner-level account cannot restore away the record that
/// it restored.
/// </summary>
public static class RestoreEndpoints
{
    /// <summary>What a restore would replace, and what it would cost.</summary>
    public record RestorePreview(
        string Label, string Agent, string Mode, DateTimeOffset BackupAt, DateTimeOffset? TargetAt,
        DateTimeOffset? EarliestTarget, DateTimeOffset? LatestTarget,
        bool HasUploads, long BackupBytes,
        int PagesCreated, int VersionsSaved, int CommentsPosted, int AttachmentsAdded, long AttachmentBytes,
        int AccountsCreated, int SessionsEnding,
        long? FreeBytes, long? NeededBytes,
        List<string> BlockedBy);

    public record RestoreRequest(string? ConfirmLabel, string? Password, string? Code, DateTimeOffset? At);

    /// <summary>The copy a restore replaced, kept so it can be undone.</summary>
    public record KeptCopy(
        Guid JobId, string Mode, DateTimeOffset RestoredAt, string? Database, string? Uploads,
        string? RestoredFrom, long? DatabaseBytes, long? UploadsBytes, DateTimeOffset? RemovedAt);

    public static RouteGroupBuilder MapRestoreEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{label}/restore-preview", Preview)
            .RequirePermission(InstancePermissions.BackupsRestore);
        group.MapPost("/{label}/restore", Request)
            .RequirePermission(InstancePermissions.BackupsRestore);
        group.MapPost("/restore/cancel", Cancel)
            .RequirePermission(InstancePermissions.BackupsRestore);
        group.MapPost("/restore/undo", Undo)
            .RequirePermission(InstancePermissions.BackupsRestore);
        group.MapPost("/restore/discard-kept", DiscardKept)
            .RequirePermission(InstancePermissions.BackupsRestore);
        return group;
    }

    /// <summary>
    /// What is in this backup, what has happened since it, and what would
    /// stop it. Everything the dialog shows before anybody types anything,
    /// so the button can be disabled with a reason rather than failing later.
    /// </summary>
    private static async Task<IResult> Preview(
        string label, DateTimeOffset? at, AppDbContext db, ISiteSettingsService settings, RestoreState state)
    {
        var backup = await db.Backups.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Label == label && b.RemovedAt == null);
        if (backup is null) return Results.NotFound();

        var mode = ModeOf(backup.Agent);
        var agents = await db.BackupAgents.AsNoTracking().ToListAsync();
        var agent = agents.FirstOrDefault(a => a.Name == backup.Agent);
        var now = DateTimeOffset.UtcNow;
        var s = await settings.GetAsync();

        // The moment the wiki would be rolled back to: the end of the backup,
        // or the time asked for on the physical side.
        var (earliest, latest) = await BoundsAsync(db, agents);
        DateTimeOffset? target = mode == ModeLogical ? null : at ?? backup.CompletedAt ?? backup.StartedAt;
        var since = target ?? backup.CompletedAt ?? backup.StartedAt;

        // The counts are made in memory, from a projection of timestamps.
        //
        // Not a preference: the SQLite the tests run on cannot translate a
        // DateTimeOffset comparison, the same limitation 8.5 met with
        // ordering. Pulling one column and counting here keeps this endpoint
        // identical on both providers, which for a preview nobody can act on
        // until they have read it is the right trade. Query filters are
        // ignored throughout, because a trashed page's attachment is still
        // bytes a restore would take away.
        var attachments = await db.Attachments.IgnoreQueryFilters().AsNoTracking()
            .Select(a => new { a.CreatedAt, a.Size }).ToListAsync();
        var lost = attachments.Where(a => a.CreatedAt > since).ToList();

        var pagesCreated = (await db.Pages.IgnoreQueryFilters().AsNoTracking()
            .Select(p => p.CreatedAt).ToListAsync()).Count(d => d > since);
        var versionsSaved = (await db.PageVersions.IgnoreQueryFilters().AsNoTracking()
            .Select(v => v.CreatedAt).ToListAsync()).Count(d => d > since);
        var commentsPosted = (await db.Comments.IgnoreQueryFilters().AsNoTracking()
            .Select(c => c.CreatedAt).ToListAsync()).Count(d => d > since);
        var accountsCreated = (await db.Users.AsNoTracking()
            .Select(u => u.CreatedAt).ToListAsync()).Count(d => d > since);
        var sessionsEnding = (await db.UserSessions.AsNoTracking()
            .Select(x => x.CreatedAt).ToListAsync()).Count(d => d > since);

        // A restore is a copy alongside the live one before it is a
        // replacement, so the disk has to hold both. Four times the dump is
        // conservative and deliberately so: a restored database is larger
        // than the dump it came from, and refusing a restore that would fill
        // the disk is better than discovering it half way through.
        long? needed = mode == ModeLogical
            ? (backup.SizeBytes * 4) + (backup.HasUploads ? backup.SizeBytes * 2 : 0)
            : null;

        var blocked = new List<string>();
        if (backup.Error is not null) blocked.Add("That backup is marked as failed.");
        if (agent is null || !BackupStatus.For(backup.Agent, await BackupStatus.LoadAsync(db)).Online)
            blocked.Add($"The {backup.Agent} backup agent is not reporting, so nothing can run the restore.");
        if (state.InProgress || s.RestoreJobId is not null)
            blocked.Add("A restore is already running.");
        if (mode == ModeLogical && !backup.HasUploads)
            blocked.Add("That backup has no uploads archive, so attachments could not be restored with it.");
        if (needed is { } n && agent?.VolumeFreeBytes is { } free && free < n)
            blocked.Add($"Not enough free space: {n:N0} bytes are needed and {free:N0} are free.");
        if (mode == ModePitr && at is { } want && (want < earliest || want > latest))
            blocked.Add("That time is outside the range these backups cover.");

        return Results.Ok(new RestorePreview(
            backup.Label, backup.Agent, mode, backup.CompletedAt ?? backup.StartedAt, target,
            earliest, latest, backup.HasUploads, backup.SizeBytes,
            pagesCreated, versionsSaved, commentsPosted,
            lost.Count, lost.Sum(a => a.Size),
            accountsCreated, sessionsEnding,
            agent?.VolumeFreeBytes, needed,
            blocked));
    }

    /// <summary>
    /// Queues the restore, behind the full gate. Everything this writes is on
    /// the near side of the swap and will be rolled away with the rest; the
    /// lasting record is written again once the restored database is up.
    /// </summary>
    private static async Task<IResult> Request(
        string label, RestoreRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISecurityDetector detector, IPasswordHasher hasher, ITotpService totp,
        ISiteSettingsService settings, RestoreState state, ICollabNotifier collab, HttpContext http)
    {
        var s = await settings.GetAsync();
        if (state.InProgress || s.RestoreJobId is not null)
            return Results.Conflict(new { message = "A restore is already queued or running." });

        var backup = await db.Backups.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Label == label && b.RemovedAt == null);
        if (backup is null) return Results.NotFound();
        if (backup.Error is not null)
            return Results.Conflict(new { message = "That backup is marked as failed and cannot be restored." });

        // Case-sensitive, against the label the page displays: this step is
        // here to prove the right backup is on screen, exactly as typing a
        // space's key does before it is destroyed.
        if (!string.Equals(req.ConfirmLabel, backup.Label, StringComparison.Ordinal))
            return Results.ValidationProblem(Error("confirmLabel", $"Type {backup.Label} exactly to confirm."));

        var mode = ModeOf(backup.Agent);
        var agents = await db.BackupAgents.AsNoTracking().ToListAsync();
        var (earliest, latest) = await BoundsAsync(db, agents);
        if (mode == ModeLogical && req.At is not null)
            return Results.ValidationProblem(Error("at", "A database dump restores to the moment it was taken."));
        if (mode == ModePitr && req.At is { } want && (want < earliest || want > latest))
            return Results.ValidationProblem(Error("at",
                $"Choose a time between {earliest:u} and {latest:u}."));

        // The password in this request, not the sudo window, for the reason
        // 11.3 gives and more so: "you signed in a few minutes ago" must not
        // stand in for "replace the wiki". A wrong answer counts toward
        // lockout exactly as a failed sign-in does.
        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (!await Auth.AuthEndpoints.VerifyPasswordOrCodeAsync(
                req.Password, req.Code, user, db, hasher, totp, settings, detector, http, DateTimeOffset.UtcNow))
            return Results.Unauthorized();

        DateTimeOffset? at = mode == ModePitr ? req.At ?? backup.CompletedAt ?? backup.StartedAt : null;
        var describes = at is { } t ? $"{backup.Label} at {t:u}" : backup.Label;
        var job = new BackupJob
        {
            Id = Guid.NewGuid(),
            Agent = backup.Agent,
            Kind = BackupNames.KindRestore,
            Trigger = BackupNames.TriggerManual,
            Status = BackupNames.StatusRequested,
            Target = backup.Label,
            OptionsJson = JsonSerializer.Serialize(new { mode, at }),
            RequestedAt = DateTimeOffset.UtcNow,
            RequestedById = user.Id,
        };
        db.BackupJobs.Add(job);
        audit.Record("backup.restore_requested", "backup", job.Id,
            new { backup.Agent, backup.Label, Mode = mode, At = at });
        await db.SaveChangesAsync();

        await settings.UpdateAsync(x =>
        {
            x.RestoreJobId = job.Id;
            x.RestoreStartedAt = job.RequestedAt;
            x.RestoreCancelRequestedAt = null;
        }, user.Id);

        // In memory too, and this is the copy that matters: in a few minutes
        // the database this was just written to may be renamed away.
        state.Begin(job.Id, job.RequestedAt, mode, describes);
        // An open editor holds a document that would otherwise be written
        // back into the restored wiki on the next keystroke.
        await collab.MaintenanceAsync(true);

        return Results.Accepted($"/api/admin/backups/jobs/{job.Id}", new { jobId = job.Id, mode, describes });
    }

    /// <summary>
    /// Stops a restore that has not passed the point of no return. After it
    /// there is no cancel, only undo, and the response says so.
    /// </summary>
    private static async Task<IResult> Cancel(
        AppDbContext db, ISiteSettingsService settings, CurrentUser current, IAuditLogger audit,
        RestoreState state, ICollabNotifier collab)
    {
        var s = await settings.GetAsync();
        var jobId = s.RestoreJobId ?? state.Current?.JobId;
        if (jobId is null) return Results.Conflict(new { message = "No restore is running." });

        var job = await db.BackupJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        var actorId = current.RequireId();

        // Still queued: no sidecar has claimed it, so it can simply be ended.
        if (job is { Status: BackupNames.StatusRequested })
        {
            job.Status = BackupNames.StatusFailed;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.Error = "Canceled before it started.";
            audit.Record("backup.restore_cancelled", "backup", job.Id, new { job.Target, Phase = "queued" });
            await db.SaveChangesAsync();
            await settings.UpdateAsync(x =>
            {
                x.RestoreJobId = null;
                x.RestoreStartedAt = null;
                x.RestoreCancelRequestedAt = null;
            }, actorId);
            state.Clear();
            await collab.MaintenanceAsync(false);
            return Results.Ok(new { canceled = true, message = "The restore was canceled. Nothing was changed." });
        }

        // Running: the sidecar decides. It checks this flag once more just
        // before the point of no return and ignores it after, so this is a
        // request rather than a guarantee, and the message says exactly that.
        await settings.UpdateAsync(x => x.RestoreCancelRequestedAt = DateTimeOffset.UtcNow, actorId);
        audit.Record("backup.restore_cancel_requested", "backup", jobId.Value, new { job?.Target });
        await db.SaveChangesAsync();
        return Results.Ok(new
        {
            canceled = false,
            message = "Asked the backup agent to stop. If it has already begun replacing the wiki it will finish, and Undo is then the way back.",
        });
    }

    /// <summary>Puts the kept copy back, which is the same swap in reverse.</summary>
    private static async Task<IResult> Undo(
        RestoreRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISecurityDetector detector, IPasswordHasher hasher, ITotpService totp,
        ISiteSettingsService settings, RestoreState state, ICollabNotifier collab, HttpContext http)
    {
        var s = await settings.GetAsync();
        if (state.InProgress || s.RestoreJobId is not null)
            return Results.Conflict(new { message = "A restore is already running." });
        if (KeptCopyOf(s) is not { RemovedAt: null } kept)
            return Results.Conflict(new { message = "There is no kept copy to go back to." });

        if (!string.Equals(req.ConfirmLabel, ConfirmUndo, StringComparison.Ordinal))
            return Results.ValidationProblem(Error("confirmLabel", $"Type {ConfirmUndo} exactly to confirm."));

        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (!await Auth.AuthEndpoints.VerifyPasswordOrCodeAsync(
                req.Password, req.Code, user, db, hasher, totp, settings, detector, http, DateTimeOffset.UtcNow))
            return Results.Unauthorized();

        // A point-in-time restore leaves no kept database: its undo is another
        // point-in-time restore, to the moment the first one began, which the
        // safety incremental and the WAL switch made reachable.
        var agent = kept.Mode == ModePitr ? BackupNames.Physical : BackupNames.Logical;
        var job = new BackupJob
        {
            Id = Guid.NewGuid(),
            Agent = agent,
            Kind = BackupNames.KindRestoreUndo,
            Trigger = BackupNames.TriggerManual,
            Status = BackupNames.StatusRequested,
            Target = kept.Database,
            OptionsJson = JsonSerializer.Serialize(new { mode = kept.Mode, at = kept.RestoredAt, undoes = kept.JobId }),
            RequestedAt = DateTimeOffset.UtcNow,
            RequestedById = user.Id,
        };
        db.BackupJobs.Add(job);
        audit.Record("backup.restore_undo_requested", "backup", job.Id, new { kept.JobId, kept.Mode, kept.RestoredAt });
        await db.SaveChangesAsync();

        await settings.UpdateAsync(x =>
        {
            x.RestoreJobId = job.Id;
            x.RestoreStartedAt = job.RequestedAt;
            x.RestoreCancelRequestedAt = null;
        }, user.Id);
        state.Begin(job.Id, job.RequestedAt, kept.Mode, "the copy kept before the last restore");
        await collab.MaintenanceAsync(true);

        return Results.Accepted($"/api/admin/backups/jobs/{job.Id}", new { jobId = job.Id });
    }

    /// <summary>
    /// Removes the kept copy. Gated because it destroys the undo, which is
    /// the one thing standing between a restore and no way back.
    /// </summary>
    private static async Task<IResult> DiscardKept(
        RestoreRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISecurityDetector detector, IPasswordHasher hasher, ITotpService totp,
        ISiteSettingsService settings, RestoreState state, HttpContext http)
    {
        var s = await settings.GetAsync();
        if (state.InProgress || s.RestoreJobId is not null)
            return Results.Conflict(new { message = "A restore is running; wait for it to finish." });
        if (KeptCopyOf(s) is not { RemovedAt: null } kept)
            return Results.Conflict(new { message = "There is no kept copy to remove." });

        if (!string.Equals(req.ConfirmLabel, ConfirmDiscard, StringComparison.Ordinal))
            return Results.ValidationProblem(Error("confirmLabel", $"Type {ConfirmDiscard} exactly to confirm."));

        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (!await Auth.AuthEndpoints.VerifyPasswordOrCodeAsync(
                req.Password, req.Code, user, db, hasher, totp, settings, detector, http, DateTimeOffset.UtcNow))
            return Results.Unauthorized();

        var job = new BackupJob
        {
            Id = Guid.NewGuid(),
            Agent = BackupNames.Logical,
            Kind = BackupNames.KindRestoreDiscard,
            Trigger = BackupNames.TriggerManual,
            Status = BackupNames.StatusRequested,
            Target = kept.Database,
            OptionsJson = JsonSerializer.Serialize(new { kept.Database, kept.Uploads }),
            RequestedAt = DateTimeOffset.UtcNow,
            RequestedById = user.Id,
        };
        db.BackupJobs.Add(job);
        audit.Record("backup.restore_kept_discarded", "backup", job.Id, new { kept.Database, kept.RestoredAt });
        await db.SaveChangesAsync();

        return Results.Accepted($"/api/admin/backups/jobs/{job.Id}", new { jobId = job.Id });
    }

    public const string ModeLogical = "logical";
    public const string ModePitr = "pitr";

    /// <summary>What has to be typed to undo a restore or remove the kept copy.</summary>
    public const string ConfirmUndo = "UNDO";
    public const string ConfirmDiscard = "REMOVE";

    public static string ModeOf(string agent) => agent == BackupNames.Physical ? ModePitr : ModeLogical;

    /// <summary>
    /// How far back and forward a point-in-time restore can reach: from the
    /// end of the oldest physical backup that is still present, to the last
    /// WAL segment the archive has taken.
    /// </summary>
    public static async Task<(DateTimeOffset? Earliest, DateTimeOffset? Latest)> BoundsAsync(
        AppDbContext db, List<BackupAgent> agents)
    {
        var fulls = await db.Backups.AsNoTracking()
            .Where(b => b.Agent == BackupNames.Physical && b.RemovedAt == null && b.Error == null && b.Type == "full")
            .Select(b => b.CompletedAt ?? b.StartedAt)
            .ToListAsync();
        var physical = agents.FirstOrDefault(a => a.Name == BackupNames.Physical);
        return (fulls.Count == 0 ? null : fulls.Min(), physical?.WalArchivedAt);
    }

    public static KeptCopy? KeptCopyOf(SiteSettings s) =>
        s.KeptCopyJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<KeptCopy>(json, JsonOptions)
            : null;

    public static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
