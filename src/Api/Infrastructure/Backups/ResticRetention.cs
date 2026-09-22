namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// The admin page's retention policy (dev-plan 9.1) expressed as restic's
/// own arguments, for the offsite copy of the uploads and the dumps
/// (dev-plan 9.2 step 2).
///
/// <para>The point of translating rather than inventing a second policy is
/// that both copies then expire together. "Keep the newest N, and everything
/// from the last D days" is <c>--keep-last N --keep-within Dd</c>, and
/// restic keeps a snapshot that either rule would keep, which is the same
/// "outside both" rule <see cref="BackupRetention"/> applies locally.</para>
///
/// <para><b>This rule also exists in bash</b>, as <c>restic_forget_args</c>
/// in <c>deploy/backup/offsite-files.sh</c>, which is what actually runs.
/// This copy is here to be tested and to drive what the screen says. Change
/// one, change both.</para>
/// </summary>
public static class ResticRetention
{
    /// <summary>
    /// The <c>forget</c> arguments for a policy, or an empty array when
    /// nothing should ever be removed.
    /// </summary>
    /// <param name="windowed">
    /// False for a target that is only present now and then (a removable
    /// drive, 9.2 step 4). A time window would prune a repository for having
    /// sat in a drawer, so such a target keeps a count and no window.
    /// </param>
    public static string[] ForgetArguments(BackupPolicy policy, bool windowed = true)
    {
        // Retention disabled means keep everything, so there is no forget to
        // run at all. An empty argument list is not "forget with no rules":
        // that would delete every snapshot, which is why this returns empty
        // and the caller skips the command entirely.
        if (!policy.Enabled) return [];

        var count = Math.Clamp(policy.KeepCount, BackupRetention.MinKeepCount, BackupRetention.MaxKeepCount);
        if (!windowed) return ["--keep-last", count.ToString()];

        var days = Math.Clamp(policy.KeepDays, BackupRetention.MinKeepDays, BackupRetention.MaxKeepDays);
        return ["--keep-last", count.ToString(), "--keep-within", $"{days}d"];
    }

    /// <summary>Whether a forget pass should run at all under this policy.</summary>
    public static bool Removes(BackupPolicy policy) => policy.Enabled;
}
