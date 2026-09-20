using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Backups;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Backups in the admin portal (dev-plan 9.1): the retention rule, the
/// policy endpoint's guards, the job queue, the status the page and the
/// monitor share, and the alerts. The sidecars are bash and are verified
/// live; these tests stand in for them by writing the rows they write.
/// </summary>
public class BackupTests
{
    private record PolicyDto(bool Enabled, int KeepCount, int KeepDays, DateTimeOffset? ChangedAt, string? ChangedByName);
    private record BackupDto(Guid Id, string Agent, string Label, string Type, string? FullLabel, DateTimeOffset StartedAt, long SizeBytes, DateTimeOffset? RemovedAt, string? RemovedReason);
    private record JobDto(Guid Id, string Agent, string Kind, string Trigger, string Status, string? Target, string? RequestedByName, string? LogTail);
    private record AgentDto(string Name, bool Reporting, bool Online, bool Overdue, bool LastRunFailed, bool DiskLow,
        BackupDto? LastSuccess, double? SuccessRate30Days, int PresentCount, long PresentBytes,
        DateTimeOffset? OldestRestorePoint, DateTimeOffset? WalArchivedAt, DateTimeOffset? PolicyEffectiveAt, bool PolicyPending);
    private record OverviewDto(PolicyDto Policy, List<AgentDto> Agents, List<BackupDto> Backups, List<BackupDto> Removed, List<JobDto> Jobs);
    private record PreviewAgentDto(string Agent, List<BackupDto> Removed, long RemovedBytes, DateTimeOffset? OldestRestorePoint, DateTimeOffset? NewOldestRestorePoint);
    private record PreviewDto(bool Stricter, List<PreviewAgentDto> Agents);
    private record AlertDto(Guid Id, string Kind, int Severity, string Key, int Status);
    private record HealthDto(string Agent, bool Reporting, bool Online, bool Overdue, bool LastRunFailed, DateTimeOffset? LastBackupAt);
    private record DashboardDto(DashboardHealthDto Health);
    private record DashboardHealthDto(List<HealthDto> Backups);

    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    private static async Task<HttpClient> AdminAsync(TestAppFactory factory)
    {
        var admin = factory.CreateClient();
        await admin.RegisterAndSignInAsync();
        return admin;
    }

    private static Backup Logical(double ageDays, long size = 100, DateTimeOffset? now = null)
    {
        var started = (now ?? Now) - TimeSpan.FromDays(ageDays);
        return new Backup
        {
            Id = Guid.NewGuid(), Agent = BackupNames.Logical, Label = started.ToString("yyyyMMdd'T'HHmmss'Z'"),
            Type = "dump", StartedAt = started, CompletedAt = started.AddSeconds(5), SizeBytes = size,
            FirstSeenAt = started, LastSeenAt = now ?? Now,
        };
    }

    private static Backup Full(double ageDays, DateTimeOffset? now = null)
    {
        var started = (now ?? Now) - TimeSpan.FromDays(ageDays);
        return new Backup
        {
            Id = Guid.NewGuid(), Agent = BackupNames.Physical, Label = started.ToString("yyyyMMdd-HHmmss") + "F",
            Type = "full", StartedAt = started, CompletedAt = started.AddSeconds(5), SizeBytes = 1000,
            FirstSeenAt = started, LastSeenAt = now ?? Now,
        };
    }

    private static Backup Incr(Backup full, double ageDays, DateTimeOffset? now = null)
    {
        var started = (now ?? Now) - TimeSpan.FromDays(ageDays);
        return new Backup
        {
            Id = Guid.NewGuid(), Agent = BackupNames.Physical, Label = $"{full.Label}_{started:yyyyMMdd-HHmmss}I",
            Type = "incr", Prior = full.Label, StartedAt = started, CompletedAt = started.AddSeconds(5), SizeBytes = 10,
            FirstSeenAt = started, LastSeenAt = now ?? Now,
        };
    }

    private static List<double> RemovedAges(BackupPolicy policy, List<Backup> backups) =>
        BackupRetention.Plan(policy, backups, Now)
            .Select(b => Math.Round((Now - b.StartedAt).TotalDays, 1)).OrderBy(d => d).ToList();

    // --- The rule (the table in dev-plan 9.1). The same cases were run
    // against the sidecars' SQL with RETENTION_DRY_RUN=1.

    [Fact]
    public void Old_backups_beyond_the_newest_N_are_removed() =>
        Assert.Equal([20, 30], RemovedAges(new(true, 3, 14), [Logical(1), Logical(2), Logical(3), Logical(20), Logical(30)]));

    [Fact]
    public void The_newest_N_survive_however_old_they_are() =>
        Assert.Equal([50], RemovedAges(new(true, 3, 14), [Logical(20), Logical(30), Logical(40), Logical(50)]));

    [Fact]
    public void A_backup_inside_the_day_limit_survives_beyond_the_count() =>
        Assert.Equal([2], RemovedAges(new(true, 1, 1), [Logical(0.5), Logical(2)]));

    [Fact]
    public void Fewer_backups_than_the_count_removes_nothing() =>
        Assert.Empty(RemovedAges(new(true, 5, 14), [Logical(1), Logical(2)]));

    [Fact]
    public void Retention_disabled_removes_nothing() =>
        Assert.Empty(RemovedAges(new(false, 1, 1), [Logical(100), Logical(200), Logical(300)]));

    [Fact]
    public void Exactly_at_the_day_limit_is_still_inside_it()
    {
        Assert.Empty(RemovedAges(new(true, 1, 14), [Logical(0), Logical(14)]));
        Assert.Equal([14.1], RemovedAges(new(true, 1, 14), [Logical(0), Logical(14.1)]));
    }

    [Fact]
    public void Failed_and_already_removed_backups_do_not_count_towards_the_newest_N()
    {
        var failed = Logical(1); failed.Error = "boom";
        var gone = Logical(2); gone.RemovedAt = Now;
        // Only 20 and 30 are real; both are within the newest 3.
        Assert.Empty(RemovedAges(new(true, 3, 14), [failed, gone, Logical(20), Logical(30)]));
    }

    [Fact]
    public void Physical_retention_removes_whole_fulls_with_their_incrementals()
    {
        var f1 = Full(1); var f10 = Full(10); var f20 = Full(20); var f30 = Full(30);
        var backups = new List<Backup> { f1, Incr(f1, 0.5), f10, Incr(f10, 9), f20, Incr(f20, 19), Incr(f20, 18), f30 };

        var removed = BackupRetention.Plan(new(true, 2, 14), backups, Now);

        // K = max(2, fulls within 14 days = 2) = 2: fulls 20 and 30 go, and
        // the incrementals that cannot be restored without full 20 go with it.
        Assert.Equal(4, removed.Count);
        Assert.All(removed, b => Assert.True(b.Label.StartsWith(f20.Label) || b.Label == f30.Label));
    }

    [Fact]
    public void Physical_retention_keeps_every_full_inside_the_day_limit()
    {
        var backups = new List<Backup> { Full(1), Full(3), Full(5), Full(20) };
        Assert.Equal([20], RemovedAges(new(true, 1, 7), backups));
    }

    // --- Stricter policies and the grace period.

    [Fact]
    public void Stricter_means_could_remove_something_the_old_one_keeps()
    {
        Assert.True(BackupRetention.IsStricter(new(true, 3, 14), null));
        Assert.True(BackupRetention.IsStricter(new(true, 3, 14), new(false, 3, 14)));
        Assert.True(BackupRetention.IsStricter(new(true, 2, 14), new(true, 3, 14)));
        Assert.True(BackupRetention.IsStricter(new(true, 5, 7), new(true, 3, 14)));
        Assert.False(BackupRetention.IsStricter(new(true, 3, 14), new(true, 3, 14)));
        Assert.False(BackupRetention.IsStricter(new(true, 5, 30), new(true, 3, 14)));
        Assert.False(BackupRetention.IsStricter(new(false, 1, 1), new(true, 3, 14)));
    }

    [Fact]
    public void A_stricter_policy_waits_a_day_and_keeps_what_either_policy_keeps_until_then()
    {
        var changed = Now.AddHours(-2);
        var settings = new SiteSettings
        {
            BackupRetentionEnabled = true, BackupKeepCount = 1, BackupKeepDays = 30, BackupPolicyChangedAt = changed,
        };
        var agent = new BackupAgent
        {
            Name = BackupNames.Logical, AppliedRetentionEnabled = true, AppliedKeepCount = 5, AppliedKeepDays = 7,
            PolicyObservedAt = changed.AddMinutes(1),
        };

        Assert.Equal(changed.AddMinutes(1) + BackupRetention.Grace, BackupRetention.EffectiveAt(settings, agent));
        Assert.Equal(new BackupPolicy(true, 5, 30), BackupRetention.Enforced(settings, agent, Now));
        Assert.Equal(new BackupPolicy(true, 1, 30), BackupRetention.Enforced(settings, agent, Now.AddDays(1)));

        // Changed again after the agent looked: its clock restarts.
        settings.BackupPolicyChangedAt = Now;
        Assert.Null(BackupRetention.EffectiveAt(settings, agent));
    }

    [Fact]
    public void An_agent_that_never_applied_a_policy_removes_nothing_during_the_grace_period()
    {
        var settings = new SiteSettings { BackupPolicyChangedAt = Now.AddHours(-1) };
        var agent = new BackupAgent { Name = BackupNames.Physical, PolicyObservedAt = Now.AddMinutes(-59) };
        Assert.False(BackupRetention.Enforced(settings, agent, Now)!.Enabled);
        Assert.True(BackupRetention.Enforced(settings, agent, Now.AddDays(1))!.Enabled);

        // No policy set at all: nothing to enforce.
        Assert.Null(BackupRetention.Enforced(new SiteSettings(), agent, Now));
    }

    // --- The policy endpoint.

    [Fact]
    public async Task Saving_the_policy_validates_audits_and_alerts_only_when_stricter()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = true, KeepCount = 0, KeepDays = 14 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = true, KeepCount = 3, KeepDays = 4000 })).StatusCode);

        // Looser than the seeded 3 / 14: no alert.
        var saved = await (await admin.PutAsJsonAsync("/api/admin/backups/policy",
            new { Enabled = true, KeepCount = 5, KeepDays = 30 })).Content.ReadFromJsonAsync<PolicyDto>();
        Assert.Equal(5, saved!.KeepCount);
        Assert.NotNull(saved.ChangedByName);
        Assert.Empty(await RetentionAlertsAsync(admin));

        // Keep forever: looser still.
        (await admin.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = false, KeepCount = 5, KeepDays = 30 })).EnsureSuccessStatusCode();
        Assert.Empty(await RetentionAlertsAsync(admin));

        // Turning pruning back on can remove backups: alert.
        (await admin.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = true, KeepCount = 5, KeepDays = 30 })).EnsureSuccessStatusCode();
        var alert = Assert.Single(await RetentionAlertsAsync(admin));
        Assert.Equal((int)SecuritySeverity.Critical, alert.Severity);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.Action == "backup.policy_changed"));
    }

    [Fact]
    public async Task Saving_the_policy_needs_sudo()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Auth:SudoMinutes"] = "0" });
        var admin = await AdminAsync(factory);
        var denied = await admin.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = false, KeepCount = 3, KeepDays = 14 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("reauth_required", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Members_cannot_see_or_change_backups()
    {
        using var factory = new TestAppFactory();
        await AdminAsync(factory);
        var member = factory.CreateClient();
        await member.RegisterAndSignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = false, KeepCount = 3, KeepDays = 14 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/admin/backups/run", new { })).StatusCode);
    }

    [Fact]
    public async Task The_preview_lists_what_would_go_and_the_new_oldest_restore_point()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var now = DateTimeOffset.UtcNow;
        var f1 = Full(1, now); var f10 = Full(10, now); var f40 = Full(40, now);
        await SeedAsync(factory, db => db.Backups.AddRange(
            Logical(1, 100, now), Logical(2, 100, now), Logical(40, 300, now), Logical(50, 500, now),
            f1, f10, f40, Incr(f40, 39, now)));

        var preview = await (await admin.PostAsJsonAsync("/api/admin/backups/policy/preview",
            new { Enabled = true, KeepCount = 2, KeepDays = 14 })).Content.ReadFromJsonAsync<PreviewDto>();

        var logical = preview!.Agents.Single(a => a.Agent == BackupNames.Logical);
        Assert.Equal(2, logical.Removed.Count);
        Assert.Equal(800, logical.RemovedBytes);
        Assert.Equal(now.AddDays(-50), logical.OldestRestorePoint!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(now.AddDays(-2), logical.NewOldestRestorePoint!.Value, TimeSpan.FromSeconds(1));

        var physical = preview.Agents.Single(a => a.Agent == BackupNames.Physical);
        Assert.Equal(2, physical.Removed.Count); // full 40 and its incremental
        Assert.Equal(now.AddDays(-10), physical.NewOldestRestorePoint!.Value, TimeSpan.FromSeconds(1));

        // Keep forever: nothing.
        var none = await (await admin.PostAsJsonAsync("/api/admin/backups/policy/preview",
            new { Enabled = false, KeepCount = 2, KeepDays = 14 })).Content.ReadFromJsonAsync<PreviewDto>();
        Assert.All(none!.Agents, a => Assert.Empty(a.Removed));
        Assert.False(none.Stricter);
    }

    // --- The queue.

    [Fact]
    public async Task Back_up_now_queues_one_job_per_agent_and_refuses_a_second()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        var jobs = await (await admin.PostAsJsonAsync("/api/admin/backups/run", new { })).Content.ReadFromJsonAsync<List<JobDto>>();
        Assert.Equal(2, jobs!.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal("requested", j.Status);
            Assert.Equal("manual", j.Trigger);
            Assert.Equal("backup", j.Kind);
            Assert.NotNull(j.RequestedByName);
        });

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/backups/run", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/admin/backups/run", new { Agents = new[] { "physical" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/backups/run", new { Agents = new[] { "tape" } })).StatusCode);

        var one = await admin.GetFromJsonAsync<JobDto>($"/api/admin/backups/jobs/{jobs[0].Id}");
        Assert.Equal(jobs[0].Id, one!.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "backup.requested"));
    }

    [Fact]
    public async Task Test_restore_queues_a_job_for_a_present_backup_only()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var present = Logical(1, now: DateTimeOffset.UtcNow);
        var gone = Logical(2, now: DateTimeOffset.UtcNow); gone.RemovedAt = DateTimeOffset.UtcNow;
        await SeedAsync(factory, db => db.Backups.AddRange(present, gone));

        var job = await (await admin.PostAsync($"/api/admin/backups/{present.Label}/restore-test", null))
            .Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("restore-test", job!.Kind);
        Assert.Equal(present.Label, job.Target);
        Assert.Equal(BackupNames.Logical, job.Agent);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/backups/{present.Label}/restore-test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/backups/{gone.Label}/restore-test", null)).StatusCode);
    }

    [Fact]
    public void The_app_role_can_queue_jobs_but_never_write_the_inventory()
    {
        // SQLite cannot enforce the role split; what can be checked is that
        // the tables are on the lists the owner connection revokes from.
        Assert.Contains("BackupJobs", DatabaseRoles.AppendOnlyTables);
        Assert.Contains("BackupAgents", DatabaseRoles.ReadOnlyTables);
        Assert.Contains("Backups", DatabaseRoles.ReadOnlyTables);
    }

    // --- Status.

    [Fact]
    public async Task The_overview_reports_health_counts_and_the_restore_window()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var now = DateTimeOffset.UtcNow;
        var f3 = Full(3, now);
        var removed = Logical(40, 999, now); removed.RemovedAt = now.AddDays(-1); removed.RemovedReason = "retention";
        await SeedAsync(factory, db =>
        {
            db.BackupAgents.AddRange(
                new BackupAgent { Name = BackupNames.Logical, StartedAt = now.AddDays(-5), LastSeenAt = now, IntervalHours = 24 },
                new BackupAgent { Name = BackupNames.Physical, StartedAt = now.AddDays(-5), LastSeenAt = now.AddMinutes(-20), IntervalHours = 24, WalArchivedAt = now.AddMinutes(-1) });
            db.Backups.AddRange(Logical(0.5, 100, now), Logical(1.5, 200, now), removed, f3, Incr(f3, 2, now));
            db.BackupJobs.AddRange(
                Job(BackupNames.Logical, "succeeded", now.AddDays(-1)),
                Job(BackupNames.Logical, "succeeded", now.AddDays(-2)),
                Job(BackupNames.Logical, "failed", now.AddDays(-3)),
                Job(BackupNames.Physical, "failed", now.AddHours(-1)));
        });

        var overview = await admin.GetFromJsonAsync<OverviewDto>("/api/admin/backups?includeRemoved=true");

        var logical = overview!.Agents.Single(a => a.Name == BackupNames.Logical);
        Assert.True(logical.Online);
        Assert.False(logical.Overdue);
        Assert.False(logical.LastRunFailed);
        Assert.Equal(2, logical.PresentCount);
        Assert.Equal(300, logical.PresentBytes);
        Assert.Equal(2.0 / 3, logical.SuccessRate30Days!.Value, 3);
        Assert.Equal(now.AddDays(-1.5), logical.OldestRestorePoint!.Value, TimeSpan.FromSeconds(1));

        var physical = overview.Agents.Single(a => a.Name == BackupNames.Physical);
        Assert.False(physical.Online);
        Assert.True(physical.LastRunFailed);
        Assert.False(physical.Overdue);
        Assert.Equal(now.AddDays(-3), physical.OldestRestorePoint!.Value, TimeSpan.FromSeconds(1));
        Assert.NotNull(physical.WalArchivedAt);

        Assert.Equal(4, overview.Backups.Count);
        Assert.Single(overview.Removed);
        Assert.Equal(4, overview.Jobs.Count);
        Assert.Equal(3, overview.Policy.KeepCount);
    }

    [Fact]
    public async Task The_dashboard_shows_backup_health()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(factory, db =>
        {
            db.BackupAgents.Add(new BackupAgent { Name = BackupNames.Logical, StartedAt = now.AddDays(-5), LastSeenAt = now, IntervalHours = 24 });
            db.Backups.Add(Logical(3, 100, now));
        });

        var dashboard = await admin.GetFromJsonAsync<DashboardDto>("/api/admin/dashboard");
        var logical = dashboard!.Health.Backups.Single(b => b.Agent == BackupNames.Logical);
        Assert.True(logical.Reporting);
        Assert.True(logical.Overdue); // three days old on a daily schedule
        Assert.NotNull(logical.LastBackupAt);
        Assert.False(dashboard.Health.Backups.Single(b => b.Agent == BackupNames.Physical).Reporting);
    }

    // --- Alerts.

    [Fact]
    public async Task The_monitor_is_quiet_when_backups_are_healthy()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(factory, db =>
        {
            foreach (var name in BackupNames.Agents)
                db.BackupAgents.Add(new BackupAgent
                {
                    Name = name, StartedAt = now.AddDays(-5), LastSeenAt = now, IntervalHours = 24,
                    VolumeFreeBytes = 900_000, VolumeTotalBytes = 1_000_000,
                });
            db.Backups.AddRange(Logical(0.2, 100, now), Full(0.2, now));
            db.BackupJobs.Add(Job(BackupNames.Logical, "succeeded", now.AddMinutes(-1)));
        });

        await RunMonitorAsync(factory, processAge: TimeSpan.FromHours(1));
        Assert.DoesNotContain(await AlertsAsync(admin), a => a.Kind.StartsWith("backup."));
    }

    [Fact]
    public async Task The_monitor_raises_each_problem_once()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(factory, db =>
        {
            // Logical: silent for an hour, three days since a backup, a failed
            // run and a failed restore test just now, and a nearly full disk.
            db.BackupAgents.Add(new BackupAgent
            {
                Name = BackupNames.Logical, StartedAt = now.AddDays(-5), LastSeenAt = now.AddHours(-1), IntervalHours = 24,
                VolumeFreeBytes = 50, VolumeTotalBytes = 1_000_000,
            });
            db.Backups.Add(Logical(3, 100, now));
            db.BackupJobs.Add(Job(BackupNames.Logical, "failed", now.AddMinutes(-1)));
            var test = Job(BackupNames.Logical, "failed", now.AddMinutes(-1));
            test.Kind = BackupNames.KindRestoreTest;
            db.BackupJobs.Add(test);
            // Physical: never reported at all.
        });

        await RunMonitorAsync(factory, processAge: TimeSpan.FromHours(1));
        var kinds = (await AlertsAsync(admin)).Where(a => a.Kind.StartsWith("backup."))
            .Select(a => $"{a.Kind}|{a.Key}").OrderBy(k => k).ToList();
        Assert.Equal(new[]
        {
            "backup.agent_offline|logical",
            "backup.agent_offline|physical",
            "backup.disk_low|logical",
            "backup.failed|logical",
            "backup.overdue|logical",
            "backup.restore_test_failed|logical",
        }, kinds);

        // Still broken on the next pass: no second round while those are open.
        await RunMonitorAsync(factory, processAge: TimeSpan.FromHours(1));
        Assert.Equal(6, (await AlertsAsync(admin)).Count(a => a.Kind.StartsWith("backup.")));
    }

    [Fact]
    public async Task A_missing_agent_is_not_reported_while_the_app_has_only_just_started()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        await RunMonitorAsync(factory, processAge: TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(await AlertsAsync(admin), a => a.Kind == "backup.agent_offline");
    }

    // --- Seeding the policy.

    [Fact]
    public async Task The_policy_is_seeded_once_from_the_old_retention_days()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Backup:SeedRetentionDays"] = "30" });
        var admin = await AdminAsync(factory);

        var overview = await admin.GetFromJsonAsync<OverviewDto>("/api/admin/backups");
        Assert.True(overview!.Policy.Enabled);
        Assert.Equal(BackupPolicySeed.DefaultKeepCount, overview.Policy.KeepCount);
        Assert.Equal(30, overview.Policy.KeepDays);
        Assert.NotNull(overview.Policy.ChangedAt);

        (await admin.PutAsJsonAsync("/api/admin/backups/policy", new { Enabled = true, KeepCount = 9, KeepDays = 60 })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISiteSettingsService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        Assert.False(await BackupPolicySeed.EnsureAsync(settings, config, NullLogger.Instance));
        Assert.Equal(60, (await settings.GetAsync()).BackupKeepDays);
    }

    private static BackupJob Job(string agent, string status, DateTimeOffset finished) => new()
    {
        Id = Guid.NewGuid(), Agent = agent, Kind = BackupNames.KindBackup, Trigger = BackupNames.TriggerScheduled,
        Status = status, RequestedAt = finished.AddMinutes(-1), StartedAt = finished.AddMinutes(-1), FinishedAt = finished,
        Error = status == "failed" ? "pg_dump: connection refused" : null,
    };

    private static async Task SeedAsync(TestAppFactory factory, Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static Task RunMonitorAsync(TestAppFactory factory, TimeSpan processAge)
    {
        var monitor = factory.Services.GetRequiredService<BackupMonitor>();
        monitor.ProcessStartedAt = DateTimeOffset.UtcNow - processAge;
        return monitor.RunOnceAsync();
    }

    private static async Task<List<AlertDto>> AlertsAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all"))!;

    private static async Task<List<AlertDto>> RetentionAlertsAsync(HttpClient admin) =>
        (await AlertsAsync(admin)).Where(a => a.Kind == "backup.retention_reduced").ToList();
}
