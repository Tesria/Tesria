using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Backups;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Admin → Backups (dev-plan 9.1).
///
/// The app never touches a backup file. The two backup sidecars write what
/// they have and what they did into BackupAgents, Backups and BackupJobs;
/// this file reads those, stores the retention policy, and queues work by
/// appending a <c>requested</c> job for a sidecar to claim. There is
/// deliberately no download endpoint: backups do not pass through the web tier.
/// </summary>
public static class BackupEndpoints
{
    public record PolicyDto(bool Enabled, int KeepCount, int KeepDays, DateTimeOffset? ChangedAt, string? ChangedByName);
    public record PolicyRequest(bool Enabled, int KeepCount, int KeepDays);

    public record BackupDto(
        Guid Id, string Agent, string Label, string Type, string? Prior, string? FullLabel,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, long SizeBytes, bool HasUploads, string? Error,
        DateTimeOffset? RemovedAt, string? RemovedReason, DateTimeOffset? LastVerifiedAt, bool? LastVerifyOk,
        string? DetailJson);

    public record JobDto(
        Guid Id, string Agent, string Kind, string Trigger, string Status, string? Target,
        DateTimeOffset RequestedAt, string? RequestedByName, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
        string? Error, string? ResultJson, string? LogTail);

    public record AgentDto(
        string Name, bool Reporting, bool Online, bool Overdue, bool LastRunFailed, bool DiskLow,
        DateTimeOffset? StartedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? NextRunAt,
        int? IntervalHours, int? FullEveryDays, string? ToolVersion, string? Message,
        long? VolumeFreeBytes, long? VolumeTotalBytes, DateTimeOffset? WalArchivedAt,
        BackupDto? LastSuccess, JobDto? LastFailure, double? SuccessRate30Days,
        int PresentCount, long PresentBytes, DateTimeOffset? OldestRestorePoint,
        DateTimeOffset? LastVerifiedAt, bool? LastVerifyOk,
        PolicyDto? AppliedPolicy, DateTimeOffset? PolicyEffectiveAt, bool PolicyPending);

    public record Overview(
        PolicyDto Policy, List<AgentDto> Agents, List<BackupDto> Backups, List<BackupDto> Removed, List<JobDto> Jobs);

    public record PreviewAgent(
        string Agent, List<BackupDto> Removed, long RemovedBytes,
        DateTimeOffset? OldestRestorePoint, DateTimeOffset? NewOldestRestorePoint, bool Stricter);

    public record Preview(bool Stricter, List<PreviewAgent> Agents);

    public record RunRequest(List<string>? Agents);

    /// <summary>The dashboard's Health tiles (2.5): one per agent.</summary>
    public record Health(string Agent, bool Reporting, bool Online, bool Overdue, bool LastRunFailed,
        DateTimeOffset? LastBackupAt, long? LastBackupBytes);

    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/backups").WithTags("Admin").RequireAuthorization();

        group.MapGet("", GetOverview).RequirePermission(InstancePermissions.BackupsView);
        group.MapPut("/policy", UpdatePolicy).RequirePermission(InstancePermissions.BackupsPolicy);
        group.MapPost("/policy/preview", PreviewPolicy).RequirePermission(InstancePermissions.BackupsPolicy);
        group.MapPost("/run", RequestBackup).RequirePermission(InstancePermissions.BackupsRun);
        group.MapPost("/{label}/restore-test", RequestRestoreTest).RequirePermission(InstancePermissions.BackupsRun);
        group.MapPost("/targets/{slot}/copy", RequestCopy).RequirePermission(InstancePermissions.BackupsRun);
        group.MapGet("/jobs/{id:guid}", GetJob).RequirePermission(InstancePermissions.BackupsView);
        return routes;
    }

    private static async Task<IResult> GetOverview(AppDbContext db, ISiteSettingsService settings, bool? includeRemoved)
    {
        var s = await settings.GetAsync();
        var snapshot = await BackupStatus.LoadAsync(db);
        var names = await NamesAsync(db, snapshot.Jobs.Select(j => j.RequestedById).Append(s.BackupPolicyChangedById));

        var present = snapshot.Backups.Where(b => b.RemovedAt is null)
            .OrderByDescending(b => b.StartedAt).Select(ToDto).ToList();
        var removed = includeRemoved == true
            ? snapshot.Backups
                .Where(b => b.RemovedAt is { } at && snapshot.Now - at <= BackupStatus.JobHistory)
                .OrderByDescending(b => b.RemovedAt).Select(ToDto).ToList()
            : [];
        var jobs = snapshot.Jobs.OrderByDescending(j => j.RequestedAt).Take(50)
            .Select(j => ToDto(j, names, includeLog: false)).ToList();

        return Results.Ok(new Overview(
            PolicyOf(s, names),
            BackupNames.Agents.Select(n => AgentOf(n, snapshot, s, names)).ToList(),
            present, removed, jobs));
    }

    private static async Task<IResult> UpdatePolicy(
        PolicyRequest req, ISiteSettingsService settings, AppDbContext db, CurrentUser current,
        IAuditLogger audit, ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        // A policy removes backups; changing it is sudo territory (dev-plan 3.5).
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        if (Validate(req) is { } invalid) return invalid;

        var actorId = current.RequireId();
        var before = await settings.GetAsync();
        var previous = BackupPolicy.From(before);
        var next = new BackupPolicy(req.Enabled, req.KeepCount, req.KeepDays);
        var hadPolicy = before.BackupPolicyChangedAt is not null;

        audit.Record("backup.policy_changed", "settings", SiteSettings.SingletonId,
            new { Before = previous, After = next });
        // Loud on purpose: a policy that removes more is how an attacker with
        // an admin session would quietly destroy the history. The sidecars
        // wait a day before applying it; this makes sure someone knows.
        if (hadPolicy && BackupRetention.IsStricter(next, previous))
            await detector.BackupRetentionReducedAsync(actorId, new { Before = previous, After = next });

        var saved = await settings.UpdateAsync(x =>
        {
            x.BackupRetentionEnabled = next.Enabled;
            x.BackupKeepCount = next.KeepCount;
            x.BackupKeepDays = next.KeepDays;
            x.BackupPolicyChangedAt = DateTimeOffset.UtcNow;
            x.BackupPolicyChangedById = actorId;
        }, actorId);

        return Results.Ok(PolicyOf(saved, await NamesAsync(db, [actorId])));
    }

    /// <summary>
    /// What a policy would remove from the current inventory. Advisory: the
    /// sidecars decide when they run, and each run's result says what it did.
    /// </summary>
    private static async Task<IResult> PreviewPolicy(PolicyRequest req, AppDbContext db, ISiteSettingsService settings)
    {
        if (Validate(req) is { } invalid) return invalid;
        var s = await settings.GetAsync();
        var snapshot = await BackupStatus.LoadAsync(db);
        var next = new BackupPolicy(req.Enabled, req.KeepCount, req.KeepDays);

        var agents = BackupNames.Agents.Select(name =>
        {
            var backups = snapshot.Backups.Where(b => b.Agent == name).ToList();
            var removed = BackupRetention.Plan(next, backups, snapshot.Now);
            var removedIds = removed.Select(b => b.Id).ToHashSet();
            var remaining = backups.Where(b => b.RemovedAt is null && b.Error is null && !removedIds.Contains(b.Id)).ToList();
            var agent = snapshot.Agents.FirstOrDefault(a => a.Name == name);
            return new PreviewAgent(
                name,
                removed.Select(ToDto).ToList(),
                removed.Sum(b => b.SizeBytes),
                BackupStatus.For(name, snapshot).OldestRestorePoint,
                OldestRestorePoint(name, remaining),
                BackupRetention.IsStricter(next, BackupRetention.Enforced(s, agent, snapshot.Now) ?? Disabled(s)));
        }).ToList();

        return Results.Ok(new Preview(
            s.BackupPolicyChangedAt is not null && BackupRetention.IsStricter(next, BackupPolicy.From(s)),
            agents));
    }

    private static async Task<IResult> RequestBackup(
        RunRequest? req, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var agents = req?.Agents is { Count: > 0 } wanted ? wanted.Distinct().ToList() : [.. BackupNames.Agents];
        if (agents.Any(a => !BackupNames.Agents.Contains(a)))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["agents"] = [$"Agents are {string.Join(" and ", BackupNames.Agents)}."],
            });

        var busy = await PendingAsync(db, agents, BackupNames.KindBackup);
        if (busy.Count > 0)
            return Results.Conflict(new { message = $"A backup is already queued or running ({string.Join(", ", busy)})." });

        var actorId = current.RequireId();
        var jobs = agents.Select(agent => NewJob(agent, BackupNames.KindBackup, null, actorId)).ToList();
        db.BackupJobs.AddRange(jobs);
        audit.Record("backup.requested", "backup", jobs[0].Id, new { Agents = agents, Jobs = jobs.Select(j => j.Id) });
        await db.SaveChangesAsync();

        var names = await NamesAsync(db, [actorId]);
        return Results.Ok(jobs.Select(j => ToDto(j, names, includeLog: false)));
    }

    /// <summary>
    /// Copies the latest backups to a target that is only there sometimes
    /// (dev-plan 9.2 step 4). Only the logical sidecar does this: it is the
    /// one holding the uploads and the dumps, and a removable drive never
    /// carries the pgBackRest repository.
    /// </summary>
    private static async Task<IResult> RequestCopy(
        string slot, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        if (slot != "removable")
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["slot"] = ["Only a removable target is copied on demand; the others are on a schedule."],
            });

        var target = await db.BackupTargets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slot == slot && t.Kind == "files");
        if (target is null || !target.Enabled)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["slot"] = ["No removable target is configured."],
            });
        // Present is what the sidecar saw last pass. Refusing here saves
        // queueing a job that can only fail, and says the useful thing.
        if (target.Present == false)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["slot"] = ["The drive is not plugged in, or has not been claimed."],
            });

        var actorId = current.RequireId();
        var job = NewJob(BackupNames.Logical, BackupNames.KindCopyOffsite, slot, actorId);
        db.BackupJobs.Add(job);
        audit.Record("backup.copy_requested", "backup", job.Id, new { Slot = slot });
        await db.SaveChangesAsync();

        var names = await NamesAsync(db, [actorId]);
        return Results.Ok(ToDto(job, names, includeLog: false));
    }

    private static async Task<IResult> RequestRestoreTest(
        string label, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var backup = await db.Backups.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Label == label && b.RemovedAt == null);
        if (backup is null) return Results.NotFound();
        if (backup.Error is not null)
            return Results.Conflict(new { message = "That backup is marked as failed and cannot be restored." });

        var busy = await PendingAsync(db, [backup.Agent], BackupNames.KindRestoreTest);
        if (busy.Count > 0)
            return Results.Conflict(new { message = "A restore test is already queued or running for these backups." });

        var actorId = current.RequireId();
        var job = NewJob(backup.Agent, BackupNames.KindRestoreTest, backup.Label, actorId);
        db.BackupJobs.Add(job);
        audit.Record("backup.restore_test_requested", "backup", job.Id, new { backup.Agent, backup.Label });
        await db.SaveChangesAsync();

        return Results.Ok(ToDto(job, await NamesAsync(db, [actorId]), includeLog: false));
    }

    private static async Task<IResult> GetJob(Guid id, AppDbContext db)
    {
        var job = await db.BackupJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null) return Results.NotFound();
        return Results.Ok(ToDto(job, await NamesAsync(db, [job.RequestedById]), includeLog: true));
    }

    /// <summary>The dashboard's view: small, and computed from the same status as this page.</summary>
    public static async Task<List<Health>> HealthAsync(AppDbContext db)
    {
        var snapshot = await BackupStatus.LoadAsync(db);
        return BackupNames.Agents.Select(name =>
        {
            var h = BackupStatus.For(name, snapshot);
            return new Health(name, h.Agent is not null, h.Online, h.Overdue, h.LastRunFailed,
                h.LastSuccess?.CompletedAt ?? h.LastSuccess?.StartedAt, h.LastSuccess?.SizeBytes);
        }).ToList();
    }

    private static IResult? Validate(PolicyRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (req.KeepCount is < BackupRetention.MinKeepCount or > BackupRetention.MaxKeepCount)
            errors["keepCount"] = [$"Keep between {BackupRetention.MinKeepCount} and {BackupRetention.MaxKeepCount} backups."];
        if (req.KeepDays is < BackupRetention.MinKeepDays or > BackupRetention.MaxKeepDays)
            errors["keepDays"] = [$"Keep between {BackupRetention.MinKeepDays} and {BackupRetention.MaxKeepDays} days."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static BackupPolicy Disabled(SiteSettings s) => BackupPolicy.From(s) with { Enabled = false };

    private static DateTimeOffset? OldestRestorePoint(string agent, List<Backup> remaining) =>
        agent == BackupNames.Physical
            ? remaining.Where(b => b.Type == "full").Select(b => (DateTimeOffset?)b.StartedAt).Min()
            : remaining.Select(b => (DateTimeOffset?)b.StartedAt).Min();

    private static async Task<List<string>> PendingAsync(AppDbContext db, List<string> agents, string kind) =>
        await db.BackupJobs.AsNoTracking()
            .Where(j => agents.Contains(j.Agent) && j.Kind == kind
                && (j.Status == BackupNames.StatusRequested || j.Status == BackupNames.StatusRunning))
            .Select(j => j.Agent)
            .Distinct()
            .ToListAsync();

    private static BackupJob NewJob(string agent, string kind, string? target, Guid actorId) => new()
    {
        Id = Guid.NewGuid(),
        Agent = agent,
        Kind = kind,
        Trigger = BackupNames.TriggerManual,
        Status = BackupNames.StatusRequested,
        Target = target,
        RequestedAt = DateTimeOffset.UtcNow,
        RequestedById = actorId,
    };

    private static AgentDto AgentOf(string name, BackupStatus.Snapshot snapshot, SiteSettings s, Dictionary<Guid, string> names)
    {
        var h = BackupStatus.For(name, snapshot);
        var a = h.Agent;
        var applied = BackupPolicy.AppliedBy(a);
        var effectiveAt = BackupRetention.EffectiveAt(s, a);
        return new AgentDto(
            name, a is not null, h.Online, h.Overdue, h.LastRunFailed, h.DiskLow,
            a?.StartedAt, a?.LastSeenAt, a?.NextRunAt, a?.IntervalHours, a?.FullEveryDays, a?.ToolVersion, a?.Message,
            a?.VolumeFreeBytes, a?.VolumeTotalBytes, a?.WalArchivedAt,
            h.LastSuccess is null ? null : ToDto(h.LastSuccess),
            h.LastFailure is null ? null : ToDto(h.LastFailure, names, includeLog: false),
            h.SuccessRate30Days, h.PresentCount, h.PresentBytes, h.OldestRestorePoint,
            h.LastVerifiedAt, h.LastVerifyOk,
            applied is null ? null : new PolicyDto(applied.Enabled, applied.KeepCount, applied.KeepDays, null, null),
            effectiveAt,
            PolicyPending: s.BackupPolicyChangedAt is not null
                && BackupRetention.IsStricter(BackupPolicy.From(s), applied)
                && !(effectiveAt <= snapshot.Now));
    }

    private static PolicyDto PolicyOf(SiteSettings s, Dictionary<Guid, string> names) => new(
        s.BackupRetentionEnabled, s.BackupKeepCount, s.BackupKeepDays, s.BackupPolicyChangedAt,
        s.BackupPolicyChangedById is { } id ? names.GetValueOrDefault(id) : null);

    private static BackupDto ToDto(Backup b) => new(
        b.Id, b.Agent, b.Label, b.Type, b.Prior, b.FullLabel, b.StartedAt, b.CompletedAt, b.SizeBytes,
        b.HasUploads, b.Error, b.RemovedAt, b.RemovedReason, b.LastVerifiedAt, b.LastVerifyOk, b.DetailJson);

    private static JobDto ToDto(BackupJob j, Dictionary<Guid, string> names, bool includeLog) => new(
        j.Id, j.Agent, j.Kind, j.Trigger, j.Status, j.Target, j.RequestedAt,
        j.RequestedById is { } id ? names.GetValueOrDefault(id) : null,
        j.StartedAt, j.FinishedAt, j.Error, j.ResultJson, includeLog ? j.LogTail : null);

    private static async Task<Dictionary<Guid, string>> NamesAsync(AppDbContext db, IEnumerable<Guid?> ids)
    {
        var wanted = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (wanted.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
    }
}
