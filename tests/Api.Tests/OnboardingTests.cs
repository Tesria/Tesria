using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The welcome tour and the tips (dev-plan 10.3). None of this is
/// authorization: it is one person's record of what they have been shown, so
/// what matters is that it is theirs alone and that it survives a round trip.
/// </summary>
public class OnboardingTests
{
    private record SummaryDto(bool TourDue, bool TipsEnabled, string[] DismissedTips);
    private record UserDto(Guid Id, string Email, SummaryDto Onboarding);

    private static async Task<UserDto> RegisterAsync(HttpClient c, string email) =>
        (await (await c.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>())!;

    private static async Task<T> InScopeAsync<T>(TestAppFactory f, Func<AppDbContext, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<HttpResponseMessage> UpdateAsync(HttpClient c, object body) =>
        c.PutAsJsonAsync("/api/auth/me/onboarding", body);

    private static async Task<SummaryDto> MeAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Onboarding;

    // --- What a new account is told.

    [Fact]
    public async Task A_new_account_is_due_the_tour_and_has_tips_on()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "new@example.com");

        var summary = await MeAsync(client);

        Assert.True(summary.TourDue);
        Assert.True(summary.TipsEnabled);
        Assert.Empty(summary.DismissedTips);
    }

    [Fact]
    public async Task An_account_that_predates_this_is_not_shown_the_tour()
    {
        // What the migration does to everyone who already had an account: it
        // marks the tour skipped rather than ambushing them with it.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "old@example.com");
        await InScopeAsync(factory, async db =>
        {
            var u = await db.Users.FirstAsync();
            u.OnboardingJson = """{"tourSkippedAt":"1970-01-01T00:00:00+00:00","tourVersion":1,"tips":{}}""";
            return await db.SaveChangesAsync();
        });

        Assert.False((await MeAsync(client)).TourDue);
    }

    // --- Each field of the update.

    [Fact]
    public async Task Finishing_the_tour_settles_it()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");

        (await UpdateAsync(client, new { TourCompleted = true })).EnsureSuccessStatusCode();

        Assert.False((await MeAsync(client)).TourDue);
        Assert.NotNull(await InScopeAsync(factory, db => db.Users
            .Select(u => u.OnboardingJson).FirstAsync()));
    }

    [Fact]
    public async Task Skipping_the_tour_settles_it_too()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "b@example.com");

        (await UpdateAsync(client, new { TourSkipped = true })).EnsureSuccessStatusCode();

        Assert.False((await MeAsync(client)).TourDue);
    }

    [Fact]
    public async Task Skipping_does_not_undo_having_finished_it()
    {
        // Wandering back into a tour you already finished and leaving again
        // should not rewrite you as someone who skipped it.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "c@example.com");
        (await UpdateAsync(client, new { TourCompleted = true })).EnsureSuccessStatusCode();

        (await UpdateAsync(client, new { TourSkipped = true })).EnsureSuccessStatusCode();

        var json = await InScopeAsync(factory, db => db.Users.Select(u => u.OnboardingJson).FirstAsync());
        Assert.Contains("tourCompletedAt", json);
    }

    [Fact]
    public async Task Showing_the_tour_again_makes_it_due()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "d@example.com");
        (await UpdateAsync(client, new { TourCompleted = true })).EnsureSuccessStatusCode();
        Assert.False((await MeAsync(client)).TourDue);

        (await UpdateAsync(client, new { ResetTour = true })).EnsureSuccessStatusCode();

        Assert.True((await MeAsync(client)).TourDue);
    }

    [Fact]
    public async Task Tips_are_dismissed_one_at_a_time_and_remembered()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "e@example.com");

        (await UpdateAsync(client, new { DismissTip = "slash-menu" })).EnsureSuccessStatusCode();
        (await UpdateAsync(client, new { DismissTip = "bubble-menu" })).EnsureSuccessStatusCode();
        // Dismissing the same one twice is not an error and not a duplicate.
        (await UpdateAsync(client, new { DismissTip = "slash-menu" })).EnsureSuccessStatusCode();

        Assert.Equal(["bubble-menu", "slash-menu"], (await MeAsync(client)).DismissedTips);
    }

    [Fact]
    public async Task Tips_can_be_turned_off_and_on_again()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "f@example.com");

        (await UpdateAsync(client, new { TipsEnabled = false })).EnsureSuccessStatusCode();
        Assert.False((await MeAsync(client)).TipsEnabled);

        (await UpdateAsync(client, new { TipsEnabled = true })).EnsureSuccessStatusCode();
        Assert.True((await MeAsync(client)).TipsEnabled);
    }

    [Fact]
    public async Task Resetting_tips_clears_the_dismissals_and_leaves_the_tour_alone()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "g@example.com");
        (await UpdateAsync(client, new { DismissTip = "watch" })).EnsureSuccessStatusCode();
        (await UpdateAsync(client, new { TourCompleted = true })).EnsureSuccessStatusCode();

        (await UpdateAsync(client, new { ResetTips = true })).EnsureSuccessStatusCode();

        var summary = await MeAsync(client);
        Assert.Empty(summary.DismissedTips);
        Assert.False(summary.TourDue);
    }

    [Fact]
    public async Task One_field_at_a_time_leaves_the_others_alone()
    {
        // The SPA sends a single field per call; omission must not be read as
        // "set it back to the default".
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "h@example.com");
        (await UpdateAsync(client, new { TourCompleted = true })).EnsureSuccessStatusCode();
        (await UpdateAsync(client, new { TipsEnabled = false })).EnsureSuccessStatusCode();

        (await UpdateAsync(client, new { DismissTip = "labels" })).EnsureSuccessStatusCode();

        var summary = await MeAsync(client);
        Assert.False(summary.TourDue);
        Assert.False(summary.TipsEnabled);
        Assert.Equal(["labels"], summary.DismissedTips);
    }

    // --- Whose record it is.

    [Fact]
    public async Task One_person_cannot_change_anothers()
    {
        using var factory = new TestAppFactory();
        var first = factory.CreateClient();
        await RegisterAsync(first, "one@example.com");
        var second = factory.CreateClient();
        await RegisterAsync(second, "two@example.com");

        (await UpdateAsync(first, new { TourCompleted = true, DismissTip = "watch" }))
            .EnsureSuccessStatusCode();

        // There is no route that names a user, so the only thing to check is
        // that the other account is untouched by it.
        var other = await MeAsync(second);
        Assert.True(other.TourDue);
        Assert.Empty(other.DismissedTips);
    }

    [Fact]
    public async Task Signing_out_is_required_to_have_no_onboarding_at_all()
    {
        using var factory = new TestAppFactory();
        var anon = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await UpdateAsync(anon, new { TourSkipped = true })).StatusCode);
    }

    [Fact]
    public async Task A_suspended_account_cannot_write_its_own()
    {
        // The refusal is the cookie's, not the endpoint's: OnValidatePrincipal
        // rejects any account that is not Active on every request, so this
        // never reaches the handler and comes back 401 rather than 403.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "suspended@example.com");
        // Set directly rather than through the admin route, so the session
        // is left alive and it is the status alone being tested.
        await InScopeAsync(factory, async db =>
        {
            var u = await db.Users.FirstAsync();
            u.Status = UserStatus.Suspended;
            return await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await UpdateAsync(client, new { TourSkipped = true })).StatusCode);
    }

    [Fact]
    public async Task A_corrupt_record_is_treated_as_a_fresh_one_rather_than_failing()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "i@example.com");
        await InScopeAsync(factory, async db =>
        {
            var u = await db.Users.FirstAsync();
            u.OnboardingJson = "not json at all";
            return await db.SaveChangesAsync();
        });

        var summary = await MeAsync(client);

        Assert.True(summary.TourDue);
        Assert.Empty(summary.DismissedTips);
    }
}
