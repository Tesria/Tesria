using Tesria.Api.Domain;

namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// The space a backups chart shows (dev-plan 9.3): backups, everything else,
/// and free.
/// </summary>
/// <param name="Filesystem">As <c>df</c> names it; the identity two agents are grouped by.</param>
/// <param name="Agents">Which agents' backups are counted in this one.</param>
public record DiskChart(
    string? Filesystem, IReadOnlyList<string> Agents,
    long BackupBytes, long WikiBytes, long OtherBytes, long FreeBytes, long TotalBytes)
{
    /// <summary>
    /// Whether free space is low enough to be worth saying so. The threshold
    /// is <b>two backup sets</b> rather than a percentage, because a
    /// percentage means the wrong thing at both ends: 10% of a 100 GB disk
    /// is not enough for one backup, and 10% of a 10 TB NAS is a terabyte
    /// of headroom nobody needs to hear about. Two is the next backup plus
    /// the one being replaced.
    /// </summary>
    public bool Low => BackupBytes > 0 && FreeBytes < BackupBytes * 2;
}

public static class DiskUsage
{
    /// <summary>
    /// One chart per filesystem, not per agent.
    ///
    /// <para>Both sidecars normally write to the same disk, and drawing that
    /// disk twice would double-count the free space and invite somebody to
    /// read two charts as two disks. They are grouped by the filesystem the
    /// agents reported; only genuinely separate disks get separate charts.
    /// An agent that has not reported its filesystem yet is left out rather
    /// than guessed at, since guessing would be the double-count again.</para>
    /// </summary>
    public static List<DiskChart> Charts(IEnumerable<BackupAgent> agents)
    {
        return agents
            .Where(a => a.VolumeTotalBytes is > 0 && a.VolumeFilesystem is { Length: > 0 })
            .GroupBy(a => a.VolumeFilesystem!)
            .Select(group =>
            {
                // Free and total describe the filesystem, so they are taken
                // from one agent rather than summed; the backups are each
                // agent's own, so those are added up.
                var first = group.First();
                var total = first.VolumeTotalBytes ?? 0;
                var free = first.VolumeFreeBytes ?? 0;
                var backups = group.Sum(a => a.VolumeBackupBytes ?? 0);
                // Each agent sees a different part of the live wiki (the
                // attachments, the database), so these add up rather than
                // being taken from one of them.
                var wiki = group.Sum(a => a.VolumeWikiBytes ?? 0);

                // The three numbers come from two commands taken moments
                // apart, so they can contradict each other: a backup
                // finishing in between, or `du` counting a sparse file
                // differently from `df`. The chart has to add up regardless,
                // so `df`'s free space is taken as authoritative (it
                // describes the filesystem) and the backups are fitted into
                // what is actually used. A slice a little out is fine; a
                // chart whose wedges sum to more than the disk is not.
                free = Math.Clamp(free, 0, total);
                var used = total - free;
                backups = Math.Clamp(backups, 0, used);
                wiki = Math.Clamp(wiki, 0, used - backups);
                var other = used - backups - wiki;

                return new DiskChart(
                    group.Key,
                    group.Select(a => a.Name).Order(StringComparer.Ordinal).ToList(),
                    backups, wiki, other, free, total);
            })
            .OrderBy(c => c.Filesystem, StringComparer.Ordinal)
            .ToList();
    }
}
