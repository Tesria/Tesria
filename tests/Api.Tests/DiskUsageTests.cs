using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Backups;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The space chart on the backups page (dev-plan 9.3).
///
/// <para>The case the plan asks for by name is the grouping: both sidecars
/// normally write to one disk, and drawing it twice would double the free
/// space and invite somebody to read two charts as two disks. The rest of
/// these pin the arithmetic, which has to stay sane when the numbers
/// disagree slightly, because they come from two commands taken moments
/// apart.</para>
/// </summary>
public class DiskUsageTests
{
    private const long GB = 1024L * 1024 * 1024;

    private static BackupAgent Agent(
        string name, string? fs = "/dev/sda1", long total = 100 * GB,
        long free = 60 * GB, long? backups = 10 * GB, long? wiki = 0) => new()
    {
        Name = name,
        VolumeFilesystem = fs,
        VolumeTotalBytes = total,
        VolumeFreeBytes = free,
        VolumeBackupBytes = backups,
        VolumeWikiBytes = wiki,
    };

    [Fact]
    public void Two_agents_on_one_filesystem_draw_one_chart()
    {
        var charts = DiskUsage.Charts([
            Agent(BackupNames.Logical, backups: 10 * GB),
            Agent(BackupNames.Physical, backups: 15 * GB),
        ]);

        var chart = Assert.Single(charts);
        Assert.Equal(["logical", "physical"], chart.Agents);
        // The backups add up; free and total describe the disk and must not.
        Assert.Equal(25 * GB, chart.BackupBytes);
        Assert.Equal(60 * GB, chart.FreeBytes);
        Assert.Equal(100 * GB, chart.TotalBytes);
    }

    [Fact]
    public void Two_agents_on_two_filesystems_draw_two()
    {
        var charts = DiskUsage.Charts([
            Agent(BackupNames.Logical, fs: "/dev/sda1"),
            Agent(BackupNames.Physical, fs: "/dev/sdb1"),
        ]);

        Assert.Equal(2, charts.Count);
        Assert.All(charts, c => Assert.Single(c.Agents));
    }

    [Fact]
    public void The_slices_add_up_to_the_disk()
    {
        var chart = Assert.Single(DiskUsage.Charts([Agent(BackupNames.Logical, wiki: 5 * GB)]));
        Assert.Equal(chart.TotalBytes,
            chart.BackupBytes + chart.WikiBytes + chart.OtherBytes + chart.FreeBytes);
    }

    [Fact]
    public void Each_agent_sees_a_different_part_of_the_live_wiki_so_those_add_up()
    {
        // The logical agent can see the attachments, the physical one the
        // database directory. Neither sees the whole wiki, so unlike free
        // space these are summed.
        var chart = Assert.Single(DiskUsage.Charts([
            Agent(BackupNames.Logical, wiki: 2 * GB),
            Agent(BackupNames.Physical, wiki: 7 * GB),
        ]));

        Assert.Equal(9 * GB, chart.WikiBytes);
    }

    [Fact]
    public void The_wiki_never_pushes_the_other_slice_negative()
    {
        // du and df again: the wiki and the backups together can appear to
        // exceed what df says is used.
        var chart = Assert.Single(DiskUsage.Charts([
            Agent(BackupNames.Logical, total: 100 * GB, free: 95 * GB, backups: 4 * GB, wiki: 40 * GB),
        ]));

        Assert.True(chart.OtherBytes >= 0 && chart.WikiBytes >= 0);
        Assert.Equal(chart.TotalBytes,
            chart.BackupBytes + chart.WikiBytes + chart.OtherBytes + chart.FreeBytes);
    }

    [Fact]
    public void Numbers_that_contradict_each_other_never_make_a_negative_slice()
    {
        // free and the backup size come from two commands taken moments
        // apart, so a backup finishing in between can make them overlap. A
        // chart a few bytes out is fine; one with a negative wedge is not.
        var chart = Assert.Single(DiskUsage.Charts([
            Agent(BackupNames.Logical, total: 100 * GB, free: 95 * GB, backups: 20 * GB),
        ]));

        Assert.Equal(0, chart.OtherBytes);
        Assert.True(chart.BackupBytes >= 0 && chart.FreeBytes >= 0);
        Assert.Equal(chart.TotalBytes,
            chart.BackupBytes + chart.WikiBytes + chart.OtherBytes + chart.FreeBytes);
    }

    [Fact]
    public void An_agent_that_has_not_reported_its_filesystem_is_left_out()
    {
        // Guessing that it shares the other agent's disk would be the
        // double-count this grouping exists to avoid.
        var charts = DiskUsage.Charts([
            Agent(BackupNames.Logical),
            Agent(BackupNames.Physical, fs: null),
        ]);

        var chart = Assert.Single(charts);
        Assert.Equal(["logical"], chart.Agents);
    }

    [Fact]
    public void An_agent_that_has_reported_nothing_yet_draws_no_chart()
    {
        Assert.Empty(DiskUsage.Charts([Agent(BackupNames.Logical, total: 0)]));
        Assert.Empty(DiskUsage.Charts([]));
    }

    [Fact]
    public void A_backup_size_that_has_not_been_measured_counts_as_nothing_yet()
    {
        // Null is "not measured", not "zero bytes of backups", but for the
        // chart both mean the same: nothing to draw in that slice.
        var chart = Assert.Single(DiskUsage.Charts([
            Agent(BackupNames.Logical, backups: null, total: 100 * GB, free: 60 * GB),
        ]));

        Assert.Equal(0, chart.BackupBytes);
        Assert.Equal(40 * GB, chart.OtherBytes);
        Assert.False(chart.Low);
    }

    [Theory]
    // free, backups, expected: the threshold is two backup sets, not a share
    // of the disk, so it means the same thing on a laptop and on a NAS.
    [InlineData(25, 10, false)]   // room for two more
    [InlineData(20, 10, false)]   // exactly two
    [InlineData(19, 10, true)]    // not quite two
    [InlineData(1, 10, true)]
    [InlineData(500, 10, false)]  // a big disk is not low
    public void Low_space_is_measured_in_backup_sets(int freeGb, int backupsGb, bool expected)
    {
        var chart = Assert.Single(DiskUsage.Charts([
            Agent(BackupNames.Logical, total: 1000 * GB, free: freeGb * GB, backups: backupsGb * GB),
        ]));

        Assert.Equal(expected, chart.Low);
    }

    [Fact]
    public void A_disk_with_no_backups_on_it_is_never_called_low()
    {
        // Nothing has been backed up yet, so there is no set to measure two
        // of, and "low" would be a guess dressed as a warning.
        var chart = Assert.Single(DiskUsage.Charts([
            Agent(BackupNames.Logical, total: 100 * GB, free: 0, backups: 0),
        ]));

        Assert.False(chart.Low);
    }
}
