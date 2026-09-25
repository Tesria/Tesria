using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Backups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The app against real PostgreSQL, running as the least-privilege role
/// (dev-plan 14.3), so the grants are enforced the way they are in
/// production. SQLite has no roles: every test elsewhere passes whether or
/// not the app writes a table it may not, which is how the review's DATA-04
/// (2026-09-24) shipped. A path that must work under the grants gets a test
/// here.
/// </summary>
public class DatabaseRoleTests
{
    private const string Label = "20260920T030000Z";
    private const string Password = "supersecret";

    private record JobDto(Guid Id, string Kind, string Status, string? Error);
    private record QueuedDto(Guid JobId);

    /// <summary>The owner signed in, with a live logical agent and one restorable backup.</summary>
    private static async Task<HttpClient> InstanceAsync(TestAppFactory factory, PostgresTestDatabase pg)
    {
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        // The agents' tables are read-only for the app, so they are seeded as
        // the owner, the way the backup sidecar writes them.
        await using var db = pg.Owner();
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
        await db.SaveChangesAsync();
        return owner;
    }

    private static async Task<Guid> RestoreAsync(HttpClient owner)
    {
        var res = await owner.PostAsJsonAsync($"/api/admin/backups/{Label}/restore",
            new { ConfirmLabel = Label, Password });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<QueuedDto>())!.JobId;
    }

    [PostgresFact]
    public async Task The_app_runs_as_its_role_and_the_role_cannot_rewrite_the_job_history()
    {
        // Without this, every other test here could pass for the wrong reason.
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        await InstanceAsync(factory, pg);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("tesria_test_app",
            await db.Database.SqlQueryRaw<string>("SELECT current_user AS \"Value\"").SingleAsync());

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE \"BackupJobs\" SET \"Status\" = 'failed'"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [PostgresFact]
    public async Task A_queued_restore_is_canceled_within_the_roles_grants()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var owner = await InstanceAsync(factory, pg);
        var jobId = await RestoreAsync(owner);

        var res = await owner.PostAsJsonAsync("/api/admin/backups/restore/cancel", new { });
        res.EnsureSuccessStatusCode();

        // The wiki stopped waiting, and is writable again.
        Assert.False(factory.Services.GetRequiredService<RestoreState>().InProgress);
        await using (var db = pg.Owner())
        {
            Assert.Null((await db.SiteSettings.SingleAsync()).RestoreJobId);
            // The row is the agent's to end; the app could not have.
            Assert.Equal(BackupNames.StatusRequested, (await db.BackupJobs.SingleAsync(j => j.Id == jobId)).Status);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "backup.restore_cancelled" && a.TargetId == jobId));
        }
        (await owner.PostAsJsonAsync("/api/spaces", new { Key = "OK", Name = "Ok" })).EnsureSuccessStatusCode();

        // And the page shows it as ended, not waiting.
        var job = await owner.GetFromJsonAsync<JobDto>($"/api/admin/backups/jobs/{jobId}");
        Assert.Equal(BackupNames.StatusFailed, job!.Status);
        Assert.Equal(BackupNames.WithdrawnRestoreError, job.Error);
    }

    [PostgresFact]
    public async Task An_unclaimed_restore_is_abandoned_within_the_roles_grants()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var owner = await InstanceAsync(factory, pg);
        var jobId = await RestoreAsync(owner);

        // As if it had waited past the limit for an agent that never came.
        factory.Services.GetRequiredService<RestoreState>().Begin(
            jobId, DateTimeOffset.UtcNow - RestoreCompletion.Unclaimed - TimeSpan.FromMinutes(1), "logical", "a backup");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (factory.Services.GetRequiredService<RestoreState>().InProgress && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(250);

        Assert.False(factory.Services.GetRequiredService<RestoreState>().InProgress);
        await using (var db = pg.Owner())
        {
            Assert.Null((await db.SiteSettings.SingleAsync()).RestoreJobId);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "backup.restore_abandoned" && a.TargetId == jobId));
        }
        (await owner.PostAsJsonAsync("/api/spaces", new { Key = "OK", Name = "Ok" })).EnsureSuccessStatusCode();
    }
}
