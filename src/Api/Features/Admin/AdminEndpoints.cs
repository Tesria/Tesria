using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Instance-level operations, gated by the Admin role alone — they are about
/// the instance, not about any space's content.
///
/// Note what is deliberately absent: there is no endpoint here that returns
/// page content. Admins do not bypass space permissions or page restrictions
/// (see docs/architecture.md, "Roles and administrators"); to read a space
/// they hold no grant for, an admin uses <see cref="RecoverSpaceAccess"/>,
/// which is audited and leaves a revocable grant behind. A silent bypass
/// would let any admin read any team's private space with no trace.
/// </summary>
public static class AdminEndpoints
{
    public record RecoverAccessResponse(
        Guid SpaceId, string Key, string Name, bool AlreadyHadAccess);

    /// <summary>
    /// Note the absence of the SMTP password: it is write-only over the API.
    /// <paramref name="SmtpPasswordSet"/> tells the UI whether one exists so it
    /// can render "configured" without ever transmitting the secret.
    /// </summary>
    public record SettingsResponse(
        string InstanceName,
        bool AllowPublicRegistration,
        bool AllowPublicSpaces,
        bool EmailEnabled,
        string? SmtpHost,
        int SmtpPort,
        string? SmtpUsername,
        bool SmtpPasswordSet,
        string? SmtpFromAddress,
        SmtpTlsMode SmtpTls,
        bool RequireTotpForAdmins,
        DateTimeOffset UpdatedAt);

    /// <summary>
    /// Every field is optional: an omitted (null) field leaves the stored value
    /// alone, so a caller can change one setting without having to send — and
    /// risk clobbering — the rest.
    ///
    /// <paramref name="SmtpPassword"/> follows the same rule with one addition:
    /// an empty string means "clear it", which null cannot express.
    /// </summary>
    public record UpdateSettingsRequest(
        string? InstanceName,
        bool? AllowPublicRegistration,
        bool? AllowPublicSpaces,
        bool? EmailEnabled,
        string? SmtpHost,
        int? SmtpPort,
        string? SmtpUsername,
        string? SmtpPassword,
        string? SmtpFromAddress,
        SmtpTlsMode? SmtpTls,
        bool? RequireTotpForAdmins);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin").WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapGet("/settings", GetSettings);
        group.MapPut("/settings", UpdateSettings);
        group.MapPost("/spaces/{key}/recover-access", RecoverSpaceAccess);

        return routes;
    }

    /// <summary>
    /// Grants the calling admin an explicit <see cref="SpaceOperation.Admin"/>
    /// permission on a space, so they can administer (or recover) it.
    ///
    /// From that point the existing permission rules apply unchanged — including
    /// the one that already lets an explicit space admin past page restrictions —
    /// rather than adding an "unless admin" branch to every check. The grant is a
    /// normal row, so it can be revoked afterwards through the usual permissions
    /// endpoint, returning the admin to ordinary access.
    /// </summary>
    private static async Task<IResult> RecoverSpaceAccess(
        string key, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == normalizedKey);
        if (space is null) return Results.NotFound();

        var userId = current.RequireId();
        var alreadyHadAccess = await db.SpacePermissions.AnyAsync(p =>
            p.SpaceId == space.Id
            && p.PrincipalType == PrincipalType.User
            && p.PrincipalId == userId
            && p.Operation == SpaceOperation.Admin);

        // Idempotent: re-running it is a no-op rather than a duplicate grant and
        // a second audit entry, so a retried request doesn't pollute the log.
        if (alreadyHadAccess)
            return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, true));

        db.SpacePermissions.Add(new SpacePermission
        {
            Id = Guid.NewGuid(),
            SpaceId = space.Id,
            PrincipalType = PrincipalType.User,
            PrincipalId = userId,
            Operation = SpaceOperation.Admin,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        audit.Record("space.access_recovered", "space", space.Id,
            new { space.Key, space.Name });
        await db.SaveChangesAsync();

        return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, false));
    }

    private static async Task<IResult> GetSettings(ISiteSettingsService settings) =>
        Results.Ok(ToResponse(await settings.GetAsync()));

    private static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest req, ISiteSettingsService settings,
        CurrentUser current, IAuditLogger audit, AppDbContext db)
    {
        if (req.SmtpPort is { } port && (port < 1 || port > 65535))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["smtpPort"] = ["Port must be between 1 and 65535."],
            });

        var name = req.InstanceName?.Trim();
        if (name is { Length: 0 })
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["instanceName"] = ["Instance name cannot be empty."],
            });

        // Recorded before the mutation so the entry names what actually changed
        // rather than the full (largely unchanged) new state — and never the
        // password itself, only that it was touched.
        var changed = new List<string>();
        if (name is not null) changed.Add(nameof(req.InstanceName));
        if (req.AllowPublicRegistration is not null) changed.Add(nameof(req.AllowPublicRegistration));
        if (req.AllowPublicSpaces is not null) changed.Add(nameof(req.AllowPublicSpaces));
        if (req.EmailEnabled is not null) changed.Add(nameof(req.EmailEnabled));
        if (req.SmtpHost is not null) changed.Add(nameof(req.SmtpHost));
        if (req.SmtpPort is not null) changed.Add(nameof(req.SmtpPort));
        if (req.SmtpUsername is not null) changed.Add(nameof(req.SmtpUsername));
        if (req.SmtpPassword is not null) changed.Add(nameof(req.SmtpPassword));
        if (req.SmtpFromAddress is not null) changed.Add(nameof(req.SmtpFromAddress));
        if (req.SmtpTls is not null) changed.Add(nameof(req.SmtpTls));
        if (req.RequireTotpForAdmins is not null) changed.Add(nameof(req.RequireTotpForAdmins));

        var actorId = current.Id;
        var updated = await settings.UpdateAsync(s =>
        {
            if (name is not null) s.InstanceName = name;
            if (req.AllowPublicRegistration is { } reg) s.AllowPublicRegistration = reg;
            if (req.AllowPublicSpaces is { } pub) s.AllowPublicSpaces = pub;
            if (req.EmailEnabled is { } mail) s.EmailEnabled = mail;
            if (req.SmtpHost is not null) s.SmtpHost = Blank(req.SmtpHost);
            if (req.SmtpPort is { } p) s.SmtpPort = p;
            if (req.SmtpUsername is not null) s.SmtpUsername = Blank(req.SmtpUsername);
            if (req.SmtpFromAddress is not null) s.SmtpFromAddress = Blank(req.SmtpFromAddress);
            if (req.SmtpTls is { } tls) s.SmtpTls = tls;
            if (req.RequireTotpForAdmins is { } totp) s.RequireTotpForAdmins = totp;
            if (req.SmtpPassword is not null)
                s.SmtpPasswordProtected = req.SmtpPassword.Length == 0
                    ? null
                    : settings.Protect(req.SmtpPassword);
        }, actorId);

        // Audited on its own unit of work: ISiteSettingsService.UpdateAsync has
        // already saved, so the audit entry needs its own SaveChanges.
        audit.Record("settings.updated", "instance", null, new { Changed = changed });
        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(updated));
    }

    /// <summary>Trims, and turns an all-whitespace value into null rather than storing blanks.</summary>
    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SettingsResponse ToResponse(SiteSettings s) => new(
        s.InstanceName,
        s.AllowPublicRegistration,
        s.AllowPublicSpaces,
        s.EmailEnabled,
        s.SmtpHost,
        s.SmtpPort,
        s.SmtpUsername,
        SmtpPasswordSet: !string.IsNullOrEmpty(s.SmtpPasswordProtected),
        s.SmtpFromAddress,
        s.SmtpTls,
        s.RequireTotpForAdmins,
        s.UpdatedAt);
}
