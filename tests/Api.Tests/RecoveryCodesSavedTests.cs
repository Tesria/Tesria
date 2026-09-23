using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Whether recovery codes were ever confirmed saved (found 2026-09-22).
/// Registration creates eight, so the count said 8 for accounts whose owner
/// was redirected to the tour before the codes were drawn and had never
/// seen one. The session and the Users tab now say which is which, so the
/// app can ask, and an administrator can see it.
/// </summary>
public class RecoveryCodesSavedTests
{
    private record MeDto(Guid Id, int RecoveryCodesRemaining, bool RecoveryCodesSaved);
    private record AdminUserDto(Guid Id, int RecoveryCodesRemaining, bool RecoveryCodesSaved);

    [Fact]
    public async Task New_codes_are_not_saved_until_their_owner_says_so()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var member = factory.CreateClient();
        var memberId = await member.RegisterAndSignInAsync();

        var me = await member.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(8, me!.RecoveryCodesRemaining);
        Assert.False(me.RecoveryCodesSaved);
        var listed = (await owner.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!.Single(u => u.Id == memberId);
        Assert.False(listed.RecoveryCodesSaved);

        (await member.PostAsync("/api/auth/me/recovery-codes/acknowledge", null)).EnsureSuccessStatusCode();

        Assert.True((await member.GetFromJsonAsync<MeDto>("/api/auth/me"))!.RecoveryCodesSaved);
        Assert.True((await owner.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!.Single(u => u.Id == memberId).RecoveryCodesSaved);
    }
}
