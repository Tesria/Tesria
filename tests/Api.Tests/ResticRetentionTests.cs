using Tesria.Api.Infrastructure.Backups;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Turning the admin page's retention policy into restic's arguments
/// (dev-plan 9.2 step 2).
///
/// <para>Worth pinning because the failure is silent and expensive in both
/// directions: too loose and an offsite repository grows without limit, too
/// tight and it quietly deletes the copy that was meant to outlive this
/// machine. The rule is duplicated in bash (<c>restic_forget_args</c>), so
/// these cases are also the specification that copy is written against.</para>
/// </summary>
public class ResticRetentionTests
{
    private static BackupPolicy Policy(bool enabled = true, int count = 10, int days = 30) =>
        new(enabled, count, days);

    [Fact]
    public void Keeps_the_newest_count_and_everything_inside_the_window()
    {
        Assert.Equal(["--keep-last", "10", "--keep-within", "30d"],
            ResticRetention.ForgetArguments(Policy(count: 10, days: 30)));
    }

    [Fact]
    public void Retention_off_produces_no_arguments_at_all()
    {
        // And the caller must then skip `forget` entirely. Running it with no
        // rules is not "keep everything": restic would drop every snapshot,
        // which is the one mistake here that cannot be undone.
        Assert.Empty(ResticRetention.ForgetArguments(Policy(enabled: false)));
        Assert.False(ResticRetention.Removes(Policy(enabled: false)));
        Assert.True(ResticRetention.Removes(Policy()));
    }

    [Fact]
    public void A_target_that_is_not_always_present_keeps_a_count_and_no_window()
    {
        // A drive in a drawer has not failed; it has been in a drawer. A time
        // window would prune it for that, so a removable target keeps a count.
        Assert.Equal(["--keep-last", "10"],
            ResticRetention.ForgetArguments(Policy(count: 10, days: 30), windowed: false));
    }

    [Theory]
    [InlineData(0, 1)]                 // below the floor the admin page allows
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(1000, 1000)]
    [InlineData(99999, 1000)]          // above the ceiling
    public void The_count_is_held_inside_the_limits_the_policy_allows(int given, int expected)
    {
        // A policy out of range should never reach restic as-is: "--keep-last 0"
        // deletes everything.
        var args = ResticRetention.ForgetArguments(Policy(count: given));
        Assert.Equal(expected.ToString(), args[1]);
    }

    [Theory]
    [InlineData(0, "1d")]
    [InlineData(1, "1d")]
    [InlineData(3650, "3650d")]
    [InlineData(99999, "3650d")]
    public void The_window_is_held_inside_the_limits_too(int given, string expected)
    {
        var args = ResticRetention.ForgetArguments(Policy(days: given));
        Assert.Equal(expected, args[3]);
    }

    [Fact]
    public void The_arguments_are_the_pairs_restic_expects()
    {
        // restic takes a value after each flag; an odd count would mean one
        // flag swallowed the next, which is the shape of bug a string-built
        // command line hides until it runs.
        var args = ResticRetention.ForgetArguments(Policy());
        Assert.Equal(0, args.Length % 2);
        for (var i = 0; i < args.Length; i += 2)
        {
            Assert.StartsWith("--", args[i]);
            Assert.DoesNotContain(args[i + 1], "--");
        }
    }

    [Fact]
    public void The_same_policy_the_local_copy_uses_produces_the_same_intent()
    {
        // Both copies expire together: what BackupRetention keeps locally is
        // what these arguments keep offsite. The shared vocabulary is the
        // count and the window, so this pins that they come from one policy
        // rather than two that can drift.
        var policy = Policy(count: 3, days: 14);
        var args = ResticRetention.ForgetArguments(policy);

        Assert.Contains("--keep-last", args);
        Assert.Equal(policy.KeepCount.ToString(), args[Array.IndexOf(args, "--keep-last") + 1]);
        Assert.Equal($"{policy.KeepDays}d", args[Array.IndexOf(args, "--keep-within") + 1]);
    }
}
