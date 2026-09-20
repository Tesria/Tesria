using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Setup;

/// <summary>
/// First-run setup (dev-plan 10.2).
///
/// The wizard is a friendlier door to rooms that already exist: every step
/// saves through the endpoint that owns that setting, and these two routes
/// only record that a step was answered and decide when the whole thing is
/// done. Nothing here is a second way to change a setting, which is why
/// there is no "save the wizard" endpoint.
///
/// Completion is checked against evidence in real columns rather than
/// against the progress record, because the progress record is a list of
/// what someone clicked and the columns are what actually happened.
/// </summary>
public static class SetupEndpoints
{
    public record RecordStepRequest(bool Skipped);
    public record SetupStatusResponse(
        bool Required, DateTimeOffset? CompletedAt, Dictionary<string, StepRecord> Steps);
    public record StepRecord(DateTimeOffset At, bool Skipped);

    /// <summary>
    /// The steps the wizard will not let the owner past. Kept here rather
    /// than in the client so that "required" means the same thing to both,
    /// and so recording one as skipped can be refused.
    /// </summary>
    public static readonly IReadOnlySet<string> RequiredSteps =
        new HashSet<string> { "account", "instance", "registration", "permissions", "backups" };

    /// <summary>Every key the wizard may record, in the order it presents them.</summary>
    public static readonly IReadOnlyList<string> AllSteps =
    [
        "welcome", "account", "instance", "registration", "permissions",
        "backups", "email", "two-factor", "first-space", "done",
    ];

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/setup").WithTags("Setup")
            .RequireAuthorization(AuthPolicies.RequireOwner);

        group.MapGet("/", Status);
        group.MapPost("/steps/{key}", RecordStep);
        group.MapPost("/complete", Complete);
        return routes;
    }

    private static Dictionary<string, StepRecord> Progress(SiteSettings s) =>
        string.IsNullOrWhiteSpace(s.SetupProgressJson)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, StepRecord>>(s.SetupProgressJson) ?? [];

    private static async Task<IResult> Status(ISiteSettingsService settings)
    {
        var s = await settings.GetAsync();
        return Results.Ok(new SetupStatusResponse(s.SetupCompletedAt is null, s.SetupCompletedAt, Progress(s)));
    }

    private static async Task<IResult> RecordStep(
        string key, RecordStepRequest req, ISiteSettingsService settings, CurrentUser current)
    {
        if (!AllSteps.Contains(key))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["key"] = ["No such step."] });

        // The client decides what to show; the server decides what counts.
        // A required step recorded as skipped would leave an instance that
        // looks set up and is not.
        if (req.Skipped && RequiredSteps.Contains(key))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["skipped"] = [$"The {key} step cannot be skipped."],
            });

        var updated = await settings.UpdateAsync(s =>
        {
            var progress = Progress(s);
            progress[key] = new StepRecord(DateTimeOffset.UtcNow, req.Skipped);
            s.SetupProgressJson = JsonSerializer.Serialize(progress);
        }, current.RequireId());

        return Results.Ok(new SetupStatusResponse(
            updated.SetupCompletedAt is null, updated.SetupCompletedAt, Progress(updated)));
    }

    /// <summary>
    /// Finishes setup, but only if the instance really is set up. Each check
    /// is a thing that happened, not a step someone clicked past.
    /// </summary>
    private static async Task<IResult> Complete(
        AppDbContext db, ISiteSettingsService settings, CurrentUser current, IAuditLogger audit)
    {
        var s = await settings.GetAsync();
        if (s.SetupCompletedAt is not null)
            return Results.Ok(new SetupStatusResponse(false, s.SetupCompletedAt, Progress(s)));

        var progress = Progress(s);
        var owner = await db.Users.FirstAsync(u => u.Id == current.RequireId());

        // Ordered as the wizard presents them, so the step the SPA jumps to
        // is the earliest one outstanding rather than an arbitrary one.
        var missing =
            owner.RecoveryCodesAcknowledgedAt is null && owner.PasswordHash is not null ? "account"
            : s.UpdatedById is null ? "instance"
            : Unanswered(progress, "instance") ? "instance"
            : Unanswered(progress, "registration") ? "registration"
            : s.PermissionsReviewedAt is null ? "permissions"
            : s.BackupPolicyChangedById is null ? "backups"
            : null;

        if (missing is not null)
            return Results.Conflict(new { message = "That step is not finished yet.", step = missing });

        var skipped = progress.Where(p => p.Value.Skipped).Select(p => p.Key).Order().ToArray();
        await settings.UpdateAsync(su => su.SetupCompletedAt = DateTimeOffset.UtcNow, owner.Id);
        audit.Record("setup.completed", "instance", SiteSettings.SingletonId, new { Skipped = skipped });
        await db.SaveChangesAsync();

        var after = await settings.GetAsync();
        return Results.Ok(new SetupStatusResponse(false, after.SetupCompletedAt, Progress(after)));
    }

    private static bool Unanswered(Dictionary<string, StepRecord> progress, string key) =>
        !progress.TryGetValue(key, out var step) || step.Skipped;

    /// <summary>
    /// Whether this account is the owner of an instance whose setup is
    /// unfinished. Read by <c>/auth/me</c> so the SPA can route (dev-plan 10.2).
    /// </summary>
    public static async Task<bool> RequiredForAsync(User user, ISiteSettingsService settings) =>
        user.Role == UserRole.Owner && (await settings.GetAsync()).SetupCompletedAt is null;
}
