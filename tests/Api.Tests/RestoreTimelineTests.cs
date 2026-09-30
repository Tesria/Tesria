using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// deploy/pgbackrest/run.sh and restore.sh, which run pgBackRest in the db
/// image, so these read the scripts (t8-R01, the 0.8.3 retest).
/// </summary>
public class RestoreTimelineTests
{
    private static string Script(string name) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../deploy/pgbackrest", name)));

    [Fact]
    public void Every_restore_to_a_moment_or_a_backup_follows_that_backups_timeline()
    {
        // Undoing a point-in-time restore is another one, to the moment the
        // first began. Without this pgBackRest follows the newest timeline,
        // which forked off before that moment, and every undo failed with
        // error [058].
        var run = Script("run.sh");
        Assert.Contains("--type=time --target=$at --target-timeline=current", run);
        Assert.Contains("--set=$set_label --type=immediate --target-timeline=current", run);
        Assert.Contains("--set=$target --type=immediate --target-timeline=current", run);
        Assert.DoesNotContain("--target-action=promote --delta\"\n", run.Replace("--target-timeline=current --target-action=promote --delta\"\n", ""));

        // The undo itself restores the safety backup and replays its own
        // timeline to the end: a time target on that timeline is never
        // reached, because nothing was written on it after that moment.
        Assert.Contains("\"undo\\\":true}", run);
        Assert.Contains("args=\"--set=$set_label --type=default --target-timeline=current --delta\"", run);

        var restore = Script("restore.sh");
        Assert.Contains("--type=time --target=\"$TARGET_TIME\" --target-timeline=current", restore);
    }
}
