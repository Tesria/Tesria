using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// deploy/backup/claim-target.sh, which marks a network or removable drive
/// as a backup target (dev-plan 9.2). It runs in the backup image, where the
/// mounts it checks exist, so these read the script.
/// </summary>
public class ClaimTargetTests
{
    private static string Script() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../deploy/backup/claim-target.sh")));

    [Fact]
    public void A_slot_with_no_path_is_refused_before_anything_is_written()
    {
        // WIN-006: with no OFFSITE_*_PATH, Compose mounts deploy/backup/no-target,
        // a folder on the boot disk with a README in it, so it was not empty
        // and was claimed without a question.
        var sh = Script();
        var check = sh.IndexOf("if [ -z \"${!PATH_VAR:-}\" ]; then", StringComparison.Ordinal);
        Assert.True(check > 0);
        Assert.Contains("PATH_VAR=\"OFFSITE_${SLOT_UPPER}_PATH\"", sh);
        Assert.True(check < sh.IndexOf("cat > \"$SENTINEL\"", StringComparison.Ordinal));
        Assert.True(check < sh.IndexOf("Claim it anyway?", StringComparison.Ordinal));
        Assert.Contains("is not set, so there is no $SLOT target to claim. Nothing was written.", sh);
    }
}
