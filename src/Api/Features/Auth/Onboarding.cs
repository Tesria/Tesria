using System.Text.Json;
using System.Text.Json.Serialization;
using Tesria.Api.Domain;

namespace Tesria.Api.Features.Auth;

/// <summary>
/// The tour and the tips (dev-plan 10.3), as they are stored on the account
/// and as the SPA sees them.
///
/// Everything here is one person's record of what they have already been
/// shown. It is deliberately not authorisation: nothing is withheld because
/// of it, so a stale or lost value costs at most one repeated tip.
/// </summary>
public static class Onboarding
{
    /// <summary>
    /// Bumped when the tour's screens change enough that people who have seen
    /// the old one should be offered the new one. Unused so far; the field
    /// exists so that raising it is a one-line change rather than a migration.
    /// </summary>
    public const int CurrentTourVersion = 1;

    public sealed class State
    {
        [JsonPropertyName("tourCompletedAt")] public DateTimeOffset? TourCompletedAt { get; set; }
        [JsonPropertyName("tourSkippedAt")] public DateTimeOffset? TourSkippedAt { get; set; }
        [JsonPropertyName("tourVersion")] public int? TourVersion { get; set; }
        [JsonPropertyName("tips")] public Dictionary<string, DateTimeOffset> Tips { get; set; } = [];
    }

    /// <summary>What <c>/auth/me</c> carries, so the SPA knows on load.</summary>
    public record Summary(bool TourDue, bool TipsEnabled, string[] DismissedTips);

    /// <summary>Partial updates; every field is optional and independent.</summary>
    public record UpdateRequest(
        bool? TourCompleted, bool? TourSkipped, bool? TipsEnabled,
        string? DismissTip, bool? ResetTips, bool? ResetTour);

    public static State Read(User user)
    {
        if (string.IsNullOrWhiteSpace(user.OnboardingJson)) return new State();
        try { return JsonSerializer.Deserialize<State>(user.OnboardingJson) ?? new State(); }
        // A record of what someone has been shown is not worth failing a
        // request over; the cost of starting it again is one repeated tour.
        catch (JsonException) { return new State(); }
    }

    public static void Write(User user, State state) =>
        user.OnboardingJson = JsonSerializer.Serialize(state);

    /// <summary>
    /// The tour is due until it has been either finished or skipped. An
    /// account that existed before this shipped was stamped as skipped by the
    /// migration, so nobody is shown a tour of a product they already use.
    /// </summary>
    public static bool TourDue(State state) =>
        state.TourCompletedAt is null && state.TourSkippedAt is null;

    public static Summary SummaryFor(User user)
    {
        var state = Read(user);
        return new Summary(TourDue(state), user.TipsEnabled, [.. state.Tips.Keys.Order()]);
    }

    /// <summary>Applies an update in place. Returns what <c>/auth/me</c> would now say.</summary>
    public static Summary Apply(User user, UpdateRequest req)
    {
        var state = Read(user);
        var now = DateTimeOffset.UtcNow;

        if (req.TourCompleted == true)
        {
            state.TourCompletedAt = now;
            state.TourSkippedAt = null;
            state.TourVersion = CurrentTourVersion;
        }
        if (req.TourSkipped == true)
        {
            // Skipping does not overwrite a completion: someone who finished
            // the tour and later wandered back into it has still seen it.
            state.TourSkippedAt ??= now;
            state.TourVersion ??= CurrentTourVersion;
        }
        if (req.ResetTour == true)
        {
            state.TourCompletedAt = null;
            state.TourSkippedAt = null;
        }
        if (req.ResetTips == true) state.Tips.Clear();
        if (!string.IsNullOrWhiteSpace(req.DismissTip)) state.Tips[req.DismissTip] = now;
        if (req.TipsEnabled is { } tips) user.TipsEnabled = tips;

        Write(user, state);
        return SummaryFor(user);
    }
}
