using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Features.Admin;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Backups;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Restoring the wiki from the admin page (dev-plan 9.4).
///
/// The gate is what these tests are mostly about, and deliberately so. A
/// restore is the most destructive thing a web request can cause in this
/// product, so every way of reaching it without meaning to is a test: the
/// wrong right, the wrong label, the wrong password, a second one while one
/// is running, a time outside what the backups cover.
///
/// The sidecars are bash and are verified live. These stand in for them by
/// writing the rows they write.
/// </summary>
public class RestoreTests
{
    private record BackupDto(Guid Id, string Agent, string Label, string Type, DateTimeOffset StartedAt);
    private record RestoreStatusDto(
        Guid? JobId, DateTimeOffset? StartedAt, bool CancelRequested,
        DateTimeOffset? LastRestoredAt, string? LastRestoreFrom,
        RestoreEndpoints.KeptCopy? KeptCopy, DateTimeOffset? KeptCopyExpiresAt);
    private record OverviewDto(List<BackupDto> Backups, RestoreStatusDto Restore);
    private record PreviewDto(
        string Label, string Agent, string Mode, DateTimeOffset BackupAt, DateTimeOffset? TargetAt,
        DateTimeOffset? EarliestTarget, DateTimeOffset? LatestTarget, bool HasUploads, long BackupBytes,
        int PagesCreated, int VersionsSaved, int CommentsPosted, int AttachmentsAdded, long AttachmentBytes,
        int AccountsCreated, int SessionsEnding, long? FreeBytes, long? NeededBytes, List<string> BlockedBy);
    private record QueuedDto(Guid JobId, string? Mode, string? Describes);
    private record RoleDto(Guid Id, string Name, string[] Permissions);
    private record UserDto(Guid Id, string Email, string DisplayName, int Role);
    private record AlertDto(Guid Id, string Kind, int Severity, string Key, int Status);

    private const string Password = "supersecret";
    private const string Label = "20260920T030000Z";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The owner, with a live logical agent and one restorable backup.</summary>
    private static async Task<HttpClient> InstanceAsync(TestAppFactory factory, bool physical = false)
    {
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        await SeedAsync(factory, db =>
        {
            db.BackupAgents.Add(new BackupAgent
            {
                Name = BackupNames.Logical,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                LastSeenAt = DateTimeOffset.UtcNow,
                IntervalHours = 24,
                VolumeFreeBytes = 500_000_000_000,
                VolumeTotalBytes = 900_000_000_000,
            });
            db.Backups.Add(new Backup
            {
                Id = Guid.NewGuid(), Agent = BackupNames.Logical, Label = Label, Type = "dump",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-2), CompletedAt = DateTimeOffset.UtcNow.AddDays(-2),
                SizeBytes = 1_000_000, HasUploads = true,
                FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-2), LastSeenAt = DateTimeOffset.UtcNow,
            });
            if (physical)
            {
                db.BackupAgents.Add(new BackupAgent
                {
                    Name = BackupNames.Physical,
                    StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                    LastSeenAt = DateTimeOffset.UtcNow,
                    IntervalHours = 24,
                    WalArchivedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    VolumeFreeBytes = 500_000_000_000,
                });
                db.Backups.Add(new Backup
                {
                    Id = Guid.NewGuid(), Agent = BackupNames.Physical, Label = "20260920-030000F", Type = "full",
                    StartedAt = DateTimeOffset.UtcNow.AddDays(-2), CompletedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    SizeBytes = 5_000_000,
                    FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-2), LastSeenAt = DateTimeOffset.UtcNow,
                });
            }
        });
        return owner;
    }

    private static async Task SeedAsync(TestAppFactory factory, Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Writes settings through the service rather than the table: it caches
    /// the row for thirty seconds, so a direct write is invisible to whatever
    /// is under test.
    /// </summary>
    private static async Task SettingsAsync(TestAppFactory factory, Action<SiteSettings> change)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().UpdateAsync(change, null);
    }

    private static async Task<T> ReadAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<HttpResponseMessage> RestoreAsync(
        HttpClient client, string label, string? confirm = null, string password = Password, DateTimeOffset? at = null) =>
        client.PostAsJsonAsync($"/api/admin/backups/{label}/restore",
            new { ConfirmLabel = confirm ?? label, Password = password, At = at });

    // --- The right ----------------------------------------------------------

    [Fact]
    public async Task An_administrator_cannot_restore_until_the_owner_allows_it()
    {
        using var factory = new TestAppFactory();
        await InstanceAsync(factory);

        // A second account, promoted to administrator. The built-in
        // administrator role does not hold backups.restore.
        var adminClient = factory.CreateClient();
        var adminId = await adminClient.RegisterAndSignInAsync();
        await SeedAsync(factory, db => db.Users.Single(u => u.Id == adminId).Role = UserRole.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await RestoreAsync(adminClient, Label)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await adminClient.GetAsync($"/api/admin/backups/{Label}/restore-preview")).StatusCode);
    }

    [Fact]
    public void The_right_belongs_to_the_owner_by_default()
    {
        var right = InstancePermissions.All.Single(p => p.Key == InstancePermissions.BackupsRestore);
        Assert.Equal(UserRole.Owner, right.DefaultFrom);
        Assert.Equal(PermissionScope.Administration, right.Scope);
    }

    [Fact]
    public async Task The_owner_can_grant_it_to_a_role()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        var roles = await owner.GetFromJsonAsync<PermissionMatrixDto>("/api/admin/roles");
        var adminRole = roles!.Roles.Single(r => r.Name == "Administrator");
        (await owner.PutAsJsonAsync($"/api/admin/roles/{adminRole.Id}/permissions",
            new { Permissions = adminRole.Permissions.Append(InstancePermissions.BackupsRestore).ToArray() }))
            .EnsureSuccessStatusCode();

        // Promoted through the API, so the role row changes and not just the
        // tier: the rights come from the role.
        var adminClient = factory.CreateClient();
        var adminId = await adminClient.RegisterAndSignInAsync();
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new { RoleId = adminRole.Id }))
            .EnsureSuccessStatusCode();

        // Reaches the endpoint now: the refusal is about the password, not the right.
        var res = await RestoreAsync(adminClient, Label, password: "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private record PermissionMatrixDto(List<RoleDto> Roles);

    // --- The confirmation ---------------------------------------------------

    [Fact]
    public async Task The_wrong_label_is_refused_and_queues_nothing()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        var res = await RestoreAsync(owner, Label, confirm: "20260101T000000Z");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(await ReadAsync(factory, db => db.BackupJobs.ToListAsync()));
        Assert.Null(await ReadAsync(factory, db => db.SiteSettings.Select(s => s.RestoreJobId).FirstOrDefaultAsync()));
    }

    [Fact]
    public async Task The_wrong_password_is_refused_and_counts_toward_lockout()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        var res = await RestoreAsync(owner, Label, password: "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Empty(await ReadAsync(factory, db => db.BackupJobs.ToListAsync()));
        // The same counter a failed sign-in moves: a restore dialog must not
        // be a way to guess a password without the lockout noticing.
        Assert.Equal(1, await ReadAsync(factory, db =>
            db.Users.Select(u => u.FailedLoginCount).SingleAsync()));
    }

    [Fact]
    public async Task A_restore_queues_a_job_and_puts_the_wiki_into_maintenance()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        var res = await RestoreAsync(owner, Label);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var queued = await res.Content.ReadFromJsonAsync<QueuedDto>();

        var job = await ReadAsync(factory, db => db.BackupJobs.SingleAsync());
        Assert.Equal(BackupNames.KindRestore, job.Kind);
        Assert.Equal(BackupNames.Logical, job.Agent);
        Assert.Equal(Label, job.Target);
        Assert.Equal(BackupNames.StatusRequested, job.Status);
        Assert.Equal(queued!.JobId, job.Id);

        var settings = await ReadAsync(factory, db => db.SiteSettings.FirstAsync());
        Assert.Equal(job.Id, settings.RestoreJobId);
        Assert.NotNull(settings.RestoreStartedAt);

        // In memory too: this is the copy that survives the database being
        // renamed away during the swap.
        Assert.True(factory.Services.GetRequiredService<RestoreState>().InProgress);

        // Audited on the near side. It is rolled away by the restore itself,
        // which is why it is written again afterwards; it survives here in
        // the safety backup and the kept copy.
        Assert.Contains(await ReadAsync(factory, db => db.AuditLogs.ToListAsync()),
            a => a.Action == "backup.restore_requested");
    }

    [Fact]
    public async Task Only_one_restore_runs_at_a_time()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();
        var second = await RestoreAsync(owner, Label);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(await ReadAsync(factory, db => db.BackupJobs.ToListAsync()));
    }

    [Fact]
    public async Task A_failed_backup_cannot_be_restored()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        await SeedAsync(factory, db => db.Backups.Single(b => b.Label == Label).Error = "the dump was truncated");

        Assert.Equal(HttpStatusCode.Conflict, (await RestoreAsync(owner, Label)).StatusCode);
    }

    // --- Point in time ------------------------------------------------------

    [Fact]
    public async Task A_time_outside_what_the_backups_cover_is_refused()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory, physical: true);

        var tooEarly = await RestoreAsync(owner, "20260920-030000F", at: DateTimeOffset.UtcNow.AddYears(-1));
        var tooLate = await RestoreAsync(owner, "20260920-030000F", at: DateTimeOffset.UtcNow.AddYears(1));

        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLate.StatusCode);
        Assert.Empty(await ReadAsync(factory, db => db.BackupJobs.ToListAsync()));
    }

    [Fact]
    public async Task A_dump_cannot_be_asked_for_a_time()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        // A logical backup restores to the moment it was taken; there is no
        // WAL to roll forward through, so asking is a mistake worth naming.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await RestoreAsync(owner, Label, at: DateTimeOffset.UtcNow.AddHours(-1))).StatusCode);
    }

    [Fact]
    public async Task A_point_in_time_restore_records_the_moment_it_targets()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory, physical: true);
        var at = DateTimeOffset.UtcNow.AddHours(-3);

        (await RestoreAsync(owner, "20260920-030000F", at: at)).EnsureSuccessStatusCode();

        var job = await ReadAsync(factory, db => db.BackupJobs.SingleAsync());
        Assert.Equal(BackupNames.Physical, job.Agent);
        Assert.Contains("pitr", job.OptionsJson);
        Assert.Contains(at.UtcDateTime.ToString("yyyy-MM-dd"), job.OptionsJson);
    }

    // --- The preview --------------------------------------------------------

    [Fact]
    public async Task The_preview_counts_what_would_be_lost()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        // A second account created after the backup: exactly the kind of
        // thing somebody needs to know is about to disappear.
        var later = factory.CreateClient();
        await later.RegisterAndSignInAsync();

        var preview = await owner.GetFromJsonAsync<PreviewDto>($"/api/admin/backups/{Label}/restore-preview");

        Assert.Equal("logical", preview!.Mode);
        // Both accounts: the backup is dated two days ago and this instance
        // was made a moment ago, so the owner counts too. That is the honest
        // answer, and it is the one the dialog shows.
        Assert.Equal(2, preview.AccountsCreated);
        Assert.True(preview.SessionsEnding >= 1);
        Assert.Empty(preview.BlockedBy);
    }

    [Fact]
    public async Task The_preview_says_why_a_restore_is_blocked()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        // Not enough room to hold the restored copy beside the live one.
        await SeedAsync(factory, db =>
            db.BackupAgents.Single(a => a.Name == BackupNames.Logical).VolumeFreeBytes = 1);

        var preview = await owner.GetFromJsonAsync<PreviewDto>($"/api/admin/backups/{Label}/restore-preview");

        Assert.Contains(preview!.BlockedBy, b => b.Contains("free space", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_preview_blocks_a_restore_while_one_is_running()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();

        var preview = await owner.GetFromJsonAsync<PreviewDto>($"/api/admin/backups/{Label}/restore-preview");

        Assert.Contains(preview!.BlockedBy, b => b.Contains("already running", StringComparison.OrdinalIgnoreCase));
    }

    // --- Maintenance --------------------------------------------------------

    [Fact]
    public async Task Reads_pass_and_writes_are_refused_while_a_restore_runs()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();

        // Reading the wiki keeps working right up to the swap.
        (await owner.GetAsync("/api/spaces")).EnsureSuccessStatusCode();

        var write = await owner.PostAsJsonAsync("/api/spaces", new { Key = "NOPE", Name = "Nope" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, write.StatusCode);
        var body = await write.Content.ReadFromJsonAsync<MaintenanceBody>();
        Assert.Equal("maintenance", body!.Code);
        Assert.Equal("restore", body.Maintenance.Reason);
        Assert.Equal("30", write.Headers.GetValues("Retry-After").Single());
    }

    private record MaintenanceBody(string Code, string Message, MaintenanceDetail Maintenance);
    private record MaintenanceDetail(string Reason, Guid JobId, DateTimeOffset StartedAt);

    [Fact]
    public async Task The_health_endpoint_reports_maintenance_to_anyone()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();

        // Anonymous, because a restore can roll the database back past the
        // session of the person watching the overlay.
        var anonymous = factory.CreateClient();
        var health = await anonymous.GetFromJsonAsync<HealthBody>("/api/health");

        Assert.NotNull(health!.Maintenance);
        Assert.Equal("restore", health.Maintenance!.Reason);
    }

    private record HealthBody(string Status, MaintenanceReason? Maintenance);
    private record MaintenanceReason(string Reason, DateTimeOffset StartedAt);

    [Fact]
    public async Task Cancelling_is_still_allowed_while_everything_else_is_refused()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();

        var res = await owner.PostAsJsonAsync("/api/admin/backups/restore/cancel", new { });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // --- Cancelling ---------------------------------------------------------

    [Fact]
    public async Task Cancelling_a_queued_restore_ends_it_and_changes_nothing()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();

        (await owner.PostAsJsonAsync("/api/admin/backups/restore/cancel", new { })).EnsureSuccessStatusCode();

        var job = await ReadAsync(factory, db => db.BackupJobs.SingleAsync());
        Assert.Equal(BackupNames.StatusFailed, job.Status);
        var settings = await ReadAsync(factory, db => db.SiteSettings.FirstAsync());
        Assert.Null(settings.RestoreJobId);
        Assert.False(factory.Services.GetRequiredService<RestoreState>().InProgress);

        // And the wiki is writable again.
        (await owner.PostAsJsonAsync("/api/spaces", new { Key = "OK", Name = "Ok" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cancelling_a_running_restore_only_asks()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        (await RestoreAsync(owner, Label)).EnsureSuccessStatusCode();
        // The sidecar has claimed it.
        await SeedAsync(factory, db => db.BackupJobs.Single().Status = BackupNames.StatusRunning);

        var res = await owner.PostAsJsonAsync("/api/admin/backups/restore/cancel", new { });
        var body = await res.Content.ReadFromJsonAsync<CancelBody>();

        Assert.False(body!.Cancelled);
        // Still in maintenance: the sidecar decides, and it may already be
        // past the point of no return.
        Assert.Equal(BackupNames.StatusRunning, (await ReadAsync(factory, db => db.BackupJobs.SingleAsync())).Status);
        Assert.NotNull((await ReadAsync(factory, db => db.SiteSettings.FirstAsync())).RestoreCancelRequestedAt);
    }

    private record CancelBody(bool Cancelled, string Message);

    [Fact]
    public async Task Cancelling_when_nothing_is_running_is_a_conflict()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync("/api/admin/backups/restore/cancel", new { })).StatusCode);
    }

    // --- Afterwards ---------------------------------------------------------

    [Fact]
    public async Task The_lasting_audit_entry_and_alert_are_written_once()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        var jobId = Guid.NewGuid();

        // What the sidecar leaves behind: the job done, and the settings
        // naming it. The near-side audit entry went with the old database.
        await SeedAsync(factory, db =>
        {
            db.BackupJobs.Add(new BackupJob
            {
                Id = jobId, Agent = BackupNames.Logical, Kind = BackupNames.KindRestore,
                Trigger = BackupNames.TriggerManual, Status = BackupNames.StatusSucceeded,
                Target = Label, RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                FinishedAt = DateTimeOffset.UtcNow,
            });
        });
        await SettingsAsync(factory, s =>
        {
            s.LastRestoreJobId = jobId;
            s.LastRestoredAt = DateTimeOffset.UtcNow;
            s.LastRestoreFrom = $"db-{Label}.dump";
        });

        var completion = factory.Services.GetServices<IHostedService>().OfType<RestoreCompletion>().Single();
        await completion.ResumeAsync(CancellationToken.None);
        // A second startup must not write it again: any number of restarts
        // produce exactly one record.
        await completion.ResumeAsync(CancellationToken.None);

        var entries = await ReadAsync(factory, db =>
            db.AuditLogs.Where(a => a.Action == "backup.restored").ToListAsync());
        Assert.Single(entries);
        Assert.Equal(jobId, entries[0].TargetId);

        var alerts = await owner.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        Assert.Single(alerts!, a => a.Kind == "backup.restored");
    }

    [Fact]
    public async Task A_restore_still_running_at_startup_keeps_the_wiki_read_only()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        var jobId = Guid.NewGuid();
        await SeedAsync(factory, db =>
        {
            db.BackupJobs.Add(new BackupJob
            {
                Id = jobId, Agent = BackupNames.Logical, Kind = BackupNames.KindRestore,
                Trigger = BackupNames.TriggerManual, Status = BackupNames.StatusRunning,
                Target = Label, RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
        });
        await SettingsAsync(factory, s =>
        {
            s.RestoreJobId = jobId;
            s.RestoreStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        });

        var completion = factory.Services.GetServices<IHostedService>().OfType<RestoreCompletion>().Single();
        await completion.ResumeAsync(CancellationToken.None);

        Assert.True(factory.Services.GetRequiredService<RestoreState>().InProgress);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await owner.PostAsJsonAsync("/api/spaces", new { Key = "NOPE", Name = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task A_restore_that_finished_while_the_app_was_down_does_not_resume_maintenance()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        var jobId = Guid.NewGuid();
        await SeedAsync(factory, db =>
        {
            db.BackupJobs.Add(new BackupJob
            {
                Id = jobId, Agent = BackupNames.Logical, Kind = BackupNames.KindRestore,
                Trigger = BackupNames.TriggerManual, Status = BackupNames.StatusFailed,
                Target = Label, RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Error = "the safety backup failed",
            });
        });
        await SettingsAsync(factory, s =>
        {
            s.RestoreJobId = jobId;
            s.RestoreStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        });

        var completion = factory.Services.GetServices<IHostedService>().OfType<RestoreCompletion>().Single();
        await completion.ResumeAsync(CancellationToken.None);

        Assert.False(factory.Services.GetRequiredService<RestoreState>().InProgress);
        Assert.Null((await ReadAsync(factory, db => db.SiteSettings.FirstAsync())).RestoreJobId);
        (await owner.PostAsJsonAsync("/api/spaces", new { Key = "OK", Name = "Ok" })).EnsureSuccessStatusCode();
    }

    // --- The kept copy ------------------------------------------------------

    private static RestoreEndpoints.KeptCopy Kept(DateTimeOffset restoredAt) => new(
        Guid.NewGuid(), "logical", restoredAt, "tesria_pre_restore", "/data/uploads/.pre-restore",
        $"db-{Label}.dump", 40_000_000, null, null);

    // Through the settings service, not straight into the table: the service
    // caches the row for thirty seconds, so a direct write would be invisible
    // to the endpoint under test.
    private static async Task SeedKeptAsync(TestAppFactory factory, DateTimeOffset restoredAt)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().UpdateAsync(s =>
        {
            s.LastRestoredAt = restoredAt;
            s.LastRestoreFrom = $"db-{Label}.dump";
            s.KeptCopyJson = System.Text.Json.JsonSerializer.Serialize(
                Kept(restoredAt), RestoreEndpoints.JsonOptions);
        }, null);
    }

    [Fact]
    public async Task The_kept_copy_is_shown_with_the_date_the_policy_will_take_it()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        var restoredAt = DateTimeOffset.UtcNow.AddDays(-1);
        await SeedKeptAsync(factory, restoredAt);
        // Three newer backups, so the count limit no longer protects it and
        // the age limit is what decides.
        await SeedAsync(factory, db =>
        {
            for (var i = 0; i < 3; i++)
                db.Backups.Add(new Backup
                {
                    Id = Guid.NewGuid(), Agent = BackupNames.Logical,
                    Label = $"newer-{i}", Type = "dump",
                    StartedAt = restoredAt.AddHours(i + 1), CompletedAt = restoredAt.AddHours(i + 1),
                    SizeBytes = 10, FirstSeenAt = restoredAt, LastSeenAt = DateTimeOffset.UtcNow,
                });
        });

        var overview = await owner.GetFromJsonAsync<OverviewDto>("/api/admin/backups");

        Assert.NotNull(overview!.Restore.KeptCopy);
        Assert.Equal("tesria_pre_restore", overview.Restore.KeptCopy!.Database);
        // The default policy keeps 14 days, so it goes 14 days after the restore.
        Assert.Equal(restoredAt.AddDays(14).Date, overview.Restore.KeptCopyExpiresAt!.Value.Date);
    }

    [Fact]
    public async Task With_retention_off_the_kept_copy_is_never_taken_by_the_policy()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        await SeedKeptAsync(factory, DateTimeOffset.UtcNow.AddDays(-400));
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>()
                .UpdateAsync(x => x.BackupRetentionEnabled = false, null);
        }

        var overview = await owner.GetFromJsonAsync<OverviewDto>("/api/admin/backups");

        Assert.NotNull(overview!.Restore.KeptCopy);
        Assert.Null(overview.Restore.KeptCopyExpiresAt);
    }

    [Fact]
    public async Task Undo_needs_the_word_and_the_password()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        await SeedKeptAsync(factory, DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(
            "/api/admin/backups/restore/undo", new { ConfirmLabel = "yes", Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsJsonAsync(
            "/api/admin/backups/restore/undo", new { ConfirmLabel = "UNDO", Password = "wrong" })).StatusCode);

        var ok = await owner.PostAsJsonAsync(
            "/api/admin/backups/restore/undo", new { ConfirmLabel = "UNDO", Password });
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        Assert.Equal(BackupNames.KindRestoreUndo,
            (await ReadAsync(factory, db => db.BackupJobs.SingleAsync())).Kind);
    }

    [Fact]
    public async Task Undo_is_a_conflict_when_there_is_no_kept_copy()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            "/api/admin/backups/restore/undo", new { ConfirmLabel = "UNDO", Password })).StatusCode);
    }

    [Fact]
    public async Task Removing_the_kept_copy_needs_the_word_and_the_password()
    {
        using var factory = new TestAppFactory();
        var owner = await InstanceAsync(factory);
        await SeedKeptAsync(factory, DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(
            "/api/admin/backups/restore/discard-kept", new { ConfirmLabel = "UNDO", Password })).StatusCode);

        var ok = await owner.PostAsJsonAsync(
            "/api/admin/backups/restore/discard-kept", new { ConfirmLabel = "REMOVE", Password });
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        Assert.Equal(BackupNames.KindRestoreDiscard,
            (await ReadAsync(factory, db => db.BackupJobs.SingleAsync())).Kind);
    }

    // --- The audit chain ----------------------------------------------------

    [Fact]
    public async Task A_restore_explains_a_shorter_audit_chain_rather_than_breaking_it()
    {
        using var factory = new TestAppFactory();
        await InstanceAsync(factory);
        var monitor = factory.Services.GetRequiredService<Tesria.Api.Infrastructure.Audit.AuditChainMonitor>();

        // First pass: the chain is however long it is.
        await monitor.RunOnceAsync(CancellationToken.None);

        // A restore happens, and takes entries with it.
        await SeedAsync(factory, db =>
        {
            db.AuditLogs.RemoveRange(db.AuditLogs.OrderByDescending(a => a.Sequence).Take(2));
            db.SiteSettings.First().LastRestoredAt = DateTimeOffset.UtcNow;
        });

        var raised = false;
        monitor.OnBroken = (_, _) => { raised = true; return Task.CompletedTask; };
        await monitor.RunOnceAsync(CancellationToken.None);

        Assert.False(raised);
    }
}
