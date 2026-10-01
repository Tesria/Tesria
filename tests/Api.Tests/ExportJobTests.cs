using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Features.Export;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Space exports prepared in the background (dev-plan 20.2): queued, run as
/// the person who asked with their rights checked again, kept for them alone,
/// and never left spinning after a restart.
/// </summary>
public class ExportJobTests
{
    private record JobDto(
        Guid Id, string Format, string SpaceKey, string SpaceName, string? Audience, string Status,
        string? Stage, int Done, int Total, string? Current, int? Position, string? Error,
        string? FileName, long? FileSize);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hello"}]}]}""";

    private static async Task<(HttpClient Owner, Guid SpaceId)> SpaceAsync(TestAppFactory factory)
    {
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync("JOBS");
        (await owner.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Plans", ContentJson = Doc })).EnsureSuccessStatusCode();
        return (owner, spaceId);
    }

    /// <summary>What the background runner does, done now (the tests turn its loop off).</summary>
    private static Task RunQueuedAsync(TestAppFactory factory) =>
        factory.Services.GetRequiredService<ExportJobRunner>().RunQueuedAsync(CancellationToken.None);

    [Fact]
    public async Task A_pack_is_prepared_in_the_background_and_only_its_owner_may_have_it()
    {
        using var factory = new TestAppFactory();
        var (owner, _) = await SpaceAsync(factory);

        var started = await owner.PostAsJsonAsync("/api/exports", new { SpaceKey = "JOBS", Format = "pack" });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var job = await started.Content.ReadFromJsonAsync<JobDto>();

        Assert.Equal("queued", job!.Status);
        await RunQueuedAsync(factory);
        var ready = await owner.GetFromJsonAsync<JobDto>($"/api/exports/{job.Id}");
        Assert.Equal("ready", ready.Status);
        Assert.Equal("jobs-pack.zip", ready.FileName);
        Assert.Contains(ready.Id, (await owner.GetFromJsonAsync<List<JobDto>>("/api/exports"))!.Select(j => j.Id));

        var file = await owner.GetAsync($"/api/exports/{job.Id}/file");
        file.EnsureSuccessStatusCode();
        using (var zip = new ZipArchive(await file.Content.ReadAsStreamAsync()))
            Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
        Assert.Equal(ready!.FileSize, (await owner.GetByteArrayAsync($"/api/exports/{job.Id}/file")).Length);

        // Someone else sees nothing of it, file included.
        var other = factory.CreateClient();
        await other.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/exports/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/exports/{job.Id}/file")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<JobDto>>("/api/exports"))!);

        // Removing it takes the file with it.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/exports/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/exports/{job.Id}/file")).StatusCode);
        Assert.False(File.Exists(factory.Services.GetRequiredService<ExportJobQueue>().PathOf(job.Id)));
    }

    [Fact]
    public async Task What_the_export_itself_would_refuse_is_refused_when_asked()
    {
        using var factory = new TestAppFactory();
        var (owner, _) = await SpaceAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PostAsJsonAsync("/api/exports", new { SpaceKey = "NOPE", Format = "pack" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/exports", new { SpaceKey = "JOBS", Format = "pdf" })).StatusCode);

        (await owner.PutAsJsonAsync("/api/spaces/JOBS/exports",
            new { Markdown = true, Html = true, Pdf = true, Site = true, Pack = false })).EnsureSuccessStatusCode();
        var off = await owner.PostAsJsonAsync("/api/exports", new { SpaceKey = "JOBS", Format = "pack" });
        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.Contains("turned off", await off.Content.ReadAsStringAsync());

        // Someone else cannot queue it either: refused exactly as the export
        // itself refuses them (a member has no export right by default).
        var stranger = factory.CreateClient();
        await stranger.RegisterAndSignInAsync();
        Assert.Equal((await stranger.GetAsync("/api/spaces/JOBS/export/pack")).StatusCode,
            (await stranger.PostAsJsonAsync("/api/exports", new { SpaceKey = "JOBS", Format = "pack" })).StatusCode);
    }

    [Fact]
    public async Task A_job_that_waited_is_checked_again_as_its_owner_when_it_runs()
    {
        using var factory = new TestAppFactory();
        var (owner, spaceId) = await SpaceAsync(factory);
        var member = factory.CreateClient();
        var memberId = await member.RegisterAndSignInAsync();

        // Queued for someone who has since been suspended: it must not run as them.
        var id = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ExportJobs.Add(new ExportJob { Id = id, UserId = memberId, SpaceId = spaceId, Format = "pack", CreatedAt = DateTimeOffset.UtcNow });
            await db.Users.Where(u => u.Id == memberId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Suspended));
            await db.SaveChangesAsync();
        }
        await RunQueuedAsync(factory);

        ExportJob job;
        using (var scope = factory.Services.CreateScope())
            job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ExportJobs.AsNoTracking().FirstAsync(j => j.Id == id);
        Assert.Equal(ExportJobStatus.Failed, job.Status);
        Assert.Equal("Your account is no longer active.", job.Error);
        Assert.False(File.Exists(factory.Services.GetRequiredService<ExportJobQueue>().PathOf(id)));
    }

    [Fact]
    public async Task After_a_restart_a_job_that_was_running_says_so_instead_of_spinning()
    {
        using var factory = new TestAppFactory();
        var (owner, spaceId) = await SpaceAsync(factory);
        var me = await owner.GetFromJsonAsync<TestHelpers.UserDto>("/api/auth/me");
        var runner = factory.Services.GetRequiredService<ExportJobRunner>();

        var running = Guid.NewGuid();
        var readyGone = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ExportJobs.Add(new ExportJob { Id = running, UserId = me!.Id, SpaceId = spaceId, Format = "pack",
                Status = ExportJobStatus.Running, CreatedAt = DateTimeOffset.UtcNow });
            db.ExportJobs.Add(new ExportJob { Id = readyGone, UserId = me.Id, SpaceId = spaceId, Format = "pack",
                Status = ExportJobStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }

        await runner.RecoverAsync(CancellationToken.None);

        var jobs = await owner.GetFromJsonAsync<List<JobDto>>("/api/exports");
        var wasRunning = jobs!.Single(j => j.Id == running);
        Assert.Equal("failed", wasRunning.Status);
        Assert.Contains("restarted", wasRunning.Error);
        Assert.Equal("expired", jobs.Single(j => j.Id == readyGone).Status);
    }

    [Fact]
    public async Task A_queued_job_can_be_canceled_and_three_unfinished_is_the_most()
    {
        using var factory = new TestAppFactory();
        var (owner, spaceId) = await SpaceAsync(factory);
        var me = await owner.GetFromJsonAsync<TestHelpers.UserDto>("/api/auth/me");

        // Three already waiting (written directly, so the runner has not had them).
        var ids = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                db.ExportJobs.Add(new ExportJob { Id = id, UserId = me!.Id, SpaceId = spaceId, Format = "pack",
                    Status = ExportJobStatus.Running, CreatedAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        var fourth = await owner.PostAsJsonAsync("/api/exports", new { SpaceKey = "JOBS", Format = "pack" });
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);

        // Back to one waiting: canceled at once, and never run.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ExportJobs.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync();
        }
        var queued = await (await owner.PostAsJsonAsync("/api/exports", new { SpaceKey = "JOBS", Format = "pack" }))
            .Content.ReadFromJsonAsync<JobDto>();
        var canceled = await (await owner.PostAsync($"/api/exports/{queued!.Id}/cancel", null)).Content.ReadFromJsonAsync<JobDto>();
        Assert.Equal("canceled", canceled!.Status);
        await RunQueuedAsync(factory);
        Assert.Equal("canceled", (await owner.GetFromJsonAsync<JobDto>($"/api/exports/{queued.Id}"))!.Status);
    }
}
