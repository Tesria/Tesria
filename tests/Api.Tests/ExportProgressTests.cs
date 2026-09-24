using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>How far along an export is (dev-plan 20.1).</summary>
public class ExportProgressTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Progress_is_counted_stage_by_stage_and_belongs_to_whoever_started_it()
    {
        var progress = new ExportProgress(new Clock());
        var me = Guid.NewGuid();
        var id = Guid.NewGuid();

        var report = progress.Start(me, id.ToString());
        report.Stage("Capturing pages", 3);
        report.Working("Home");
        report.Step();

        var seen = progress.Get(me, id)!;
        Assert.Equal(("Capturing pages", 1, 3, "Home", false), (seen.Stage, seen.Done, seen.Total, seen.Current, seen.Finished));
        Assert.Null(progress.Get(Guid.NewGuid(), id));
        Assert.Null(progress.Get(null, id));

        report.Finish();
        Assert.True(progress.Get(me, id)!.Finished);
    }

    [Fact]
    public void An_id_that_is_missing_malformed_or_taken_reports_to_nobody()
    {
        var progress = new ExportProgress(new Clock());
        var me = Guid.NewGuid();
        var id = Guid.NewGuid().ToString();

        Assert.Same(ExportProgress.Reporter.None, progress.Start(me, null));
        Assert.Same(ExportProgress.Reporter.None, progress.Start(me, "not-a-guid"));
        var first = progress.Start(me, id);
        Assert.NotSame(ExportProgress.Reporter.None, first);
        // A second export cannot take over the first one's progress.
        Assert.Same(ExportProgress.Reporter.None, progress.Start(me, id));
        ExportProgress.Reporter.None.Stage("anything", 1); // and None is harmless
    }

    [Fact]
    public void Progress_is_forgotten_ten_minutes_after_its_last_change()
    {
        var clock = new Clock();
        var progress = new ExportProgress(clock);
        var id = Guid.NewGuid();
        progress.Start(null, id.ToString()).Finish();

        clock.Now += ExportProgress.Keep + TimeSpan.FromSeconds(1);
        progress.Start(null, Guid.NewGuid().ToString()); // a start sweeps

        Assert.Null(progress.Get(null, id));
    }

    [Fact]
    public async Task A_pack_export_reports_its_progress_to_its_exporter_only()
    {
        await using var app = new TestAppFactory();
        var owner = app.CreateClient();
        var other = app.CreateClient();
        await owner.RegisterAndSignInAsync();
        await other.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync("PROG");
        foreach (var title in new[] { "One", "Two" })
            (await owner.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, Title = title, ContentJson = """{"type":"doc","content":[{"type":"paragraph"}]}""" }))
                .EnsureSuccessStatusCode();

        var id = Guid.NewGuid();
        var res = await owner.GetAsync($"/api/spaces/PROG/export/pack?progress={id}");
        res.EnsureSuccessStatusCode();

        var seen = await owner.GetFromJsonAsync<ExportProgress.Snapshot>($"/api/export-progress/{id}");
        Assert.True(seen!.Finished);
        // The last stage it reached: the pages were written, then the files
        // (none here) were copied.
        Assert.Equal(("Copying files", 0), (seen.Stage, seen.Total));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/export-progress/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/export-progress/{Guid.NewGuid()}")).StatusCode);
    }
}
