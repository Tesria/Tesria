using System.Text.RegularExpressions;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Spaces;

public static partial class SpaceEndpoints
{
    public record CreateSpaceRequest(string Key, string Name, string? Description);
    /// <summary>The key proves you are looking at the right space; the password proves it is you.</summary>
    public record DeleteSpaceRequest(string ConfirmKey, string? Password, string? Code);
    public record DeletionPreviewResponse(string Key, string Name, int Pages, int Attachments, long Bytes, bool IsPublic);
    public record UpdateSpaceRequest(
        string Name, string? Description,
        SpaceIconKind? IconKind = null, string? IconValue = null, int? IconColor = null);
    public record SpaceResponse(
        Guid Id, string Key, string Name, string? Description,
        bool Archived, Guid? HomepageId, DateTimeOffset CreatedAt,
        bool IsPublic, bool PublicComments,
        SpaceIconKind IconKind, string? IconValue, int? IconColor,
        SpaceExportsDto Exports);

    /// <summary>
    /// Which exports this space allows (dev-plan 12.3). Part of every space
    /// response, so the page view can leave out a download that would only
    /// be refused.
    /// </summary>
    public record SpaceExportsDto(bool Markdown, bool Html, bool Pdf, bool Site, bool Pack);

    // 2–50 chars, starts with a letter, letters/digits only. Stored upper-cased.
    [GeneratedRegex("^[A-Z][A-Z0-9]{1,49}$")]
    private static partial Regex KeyPattern();

    public static IEndpointRouteBuilder MapSpaceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/spaces").WithTags("Spaces").RequireAuthorization();

        // Readable without a session (dev-plan 5.2); the permission service decides what an anonymous caller sees.
        group.MapGet("/", List).AllowAnonymous();
        group.MapPost("/", Create)
            .RequirePermission(Infrastructure.Permissions.InstancePermissions.SpacesCreate);
        group.MapGet("/{key}", GetByKey).AllowAnonymous();
        group.MapPut("/{key}", Update);
        group.MapPost("/{key}/archive",
            (string key, AppDbContext db, IAuditLogger audit, IPermissionService perms)
                => ArchiveEndpoint(key, true, db, audit, perms));
        group.MapPost("/{key}/unarchive",
            (string key, AppDbContext db, IAuditLogger audit, IPermissionService perms)
                => ArchiveEndpoint(key, false, db, audit, perms));

        // Deleting a space is an instance right, not a space permission
        // (dev-plan 11.3): a space's own administrator archives, which is
        // reversible; destroying one is the instance's decision.
        group.MapGet("/{key}/deletion-preview", DeletionPreview)
            .RequirePermission(InstancePermissions.SpacesDelete);
        group.MapDelete("/{key}", Delete)
            .RequirePermission(InstancePermissions.SpacesDelete);

        // Which exports a space allows (dev-plan 12.3). An instance right,
        // like deleting: the setting exists for spaces more sensitive than
        // the rest, and that judgment belongs to the instance's
        // administrators rather than to whoever created the space.
        group.MapPut("/{key}/exports", UpdateExports)
            .RequirePermission(InstancePermissions.SpacesExports);

        return routes;
    }

    private static async Task<IResult> List(
        AppDbContext db, IPermissionService perms, bool includeArchived = false)
    {
        var query = db.Spaces.AsNoTracking();
        if (!includeArchived) query = query.Where(s => !s.Archived);
        var spaces = await query.OrderBy(s => s.Name).ToListAsync();

        // Only surface spaces the caller may view.
        var viewable = await perms.ViewableSpaceIdsAsync();
        return Results.Ok(spaces.Where(s => viewable.Contains(s.Id)).Select(ToResponse));
    }

    private static async Task<IResult> Create(
        CreateSpaceRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var key = (req.Key ?? "").Trim().ToUpperInvariant();
        var name = (req.Name ?? "").Trim();

        if (!KeyPattern().IsMatch(key))
            return Results.ValidationProblem(Error("key",
                "Key must be 2–50 characters, start with a letter, and contain only letters and digits."));
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));

        if (await db.Spaces.AnyAsync(s => s.Key == key))
            return Results.Conflict(new { message = $"A space with key '{key}' already exists." });

        var space = new Space
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            CreatedById = current.RequireId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Spaces.Add(space);
        audit.Record("space.created", "space", space.Id, new { space.Key, space.Name });
        await db.SaveChangesAsync();

        return Results.Created($"/api/spaces/{space.Key}", ToResponse(space));
    }

    private static async Task<IResult> GetByKey(string key, AppDbContext db, IPermissionService perms)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == normalizedKey);
        if (space is null) return Results.NotFound();
        // 404 rather than 403 so a hidden space's existence isn't disclosed.
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        return Results.Ok(ToResponse(space));
    }

    private static async Task<IResult> Update(
        string key, UpdateSpaceRequest req, AppDbContext db, IPermissionService perms,
        IProfileMediaService media)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));

        if (SpaceIcons.ValidateColor(req.IconColor) is { } colorError)
            return Results.ValidationProblem(Error("iconColor", colorError));

        // The icon (dev-plan 6). An omitted kind leaves it alone; the picture
        // case is not settable here: it needs the upload endpoint, which has
        // the bytes.
        switch (req.IconKind)
        {
            case SpaceIconKind.None:
                SpaceIcons.Clear(space, media);
                break;

            case SpaceIconKind.Emoji:
                var (emoji, emojiError) = SpaceIcons.NormalizeEmoji(req.IconValue);
                if (emojiError is not null) return Results.ValidationProblem(Error("iconValue", emojiError));
                SpaceIcons.Clear(space, media);
                space.IconKind = SpaceIconKind.Emoji;
                space.IconValue = emoji;
                break;

            case SpaceIconKind.Image:
                return Results.ValidationProblem(Error("iconKind", "Upload a picture through the icon endpoint."));
        }

        if (req.IconColor is { } color) space.IconColor = color;

        space.Name = name;
        space.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    private static async Task<IResult> ArchiveEndpoint(
        string key, bool archived, AppDbContext db, IAuditLogger audit, IPermissionService perms)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        space.Archived = archived;
        audit.Record(archived ? "space.archived" : "space.unarchived", "space", space.Id, new { space.Key });
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    /// <summary>
    /// What the delete dialog shows before asking. Counts every page,
    /// including drafts and the ones already in the trash, because they all go.
    /// </summary>
    private static async Task<IResult> DeletionPreview(string key, AppDbContext db, IPermissionService perms)
    {
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();

        var pageIds = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.SpaceId == space.Id).Select(p => p.Id).ToListAsync();
        var files = await db.Attachments.Where(a => pageIds.Contains(a.PageId))
            .Select(a => a.Size).ToListAsync();

        return Results.Ok(new DeletionPreviewResponse(
            space.Key, space.Name, pageIds.Count, files.Count, files.Sum(), space.IsPublic));
    }

    /// <summary>
    /// Destroys a space and everything in it (dev-plan 11.3).
    ///
    /// The password is verified in this request rather than leaning on the
    /// five-minute sudo window: this is the one action where "you signed in a
    /// few minutes ago" must not stand in for "you mean it". The rows go in one
    /// transaction; the files afterwards, best effort, because a crash between
    /// the two should leave orphaned bytes rather than a half-deleted space.
    /// </summary>
    private static async Task<IResult> Delete(
        string key, [Microsoft.AspNetCore.Mvc.FromBody] DeleteSpaceRequest req,
        AppDbContext db, CurrentUser current, IAuditLogger audit,
        ISecurityDetector detector, IAttachmentStorage storage, IProfileMediaService media,
        IPasswordHasher hasher, ITotpService totp, ISiteSettingsService siteSettings,
        IPermissionService perms, HttpContext http, ILoggerFactory logs)
    {
        // Archived spaces are deletable too: archiving first is not required.
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        // The right says a person may destroy spaces at all, not that they may
        // destroy one they cannot even see (dev-plan 11.1: rights are additive
        // over space permissions, never a bypass). An administrator who needs
        // to reach a space they hold no grant for uses recover-access first,
        // which is audited. 404 rather than 403, as everywhere else, so a
        // hidden space's existence is not disclosed.
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();

        // Case-sensitive, and against the stored key, which is what the dialog
        // displays: this step exists to prove the right space is on screen.
        if (!string.Equals(req.ConfirmKey, space.Key, StringComparison.Ordinal))
            return Results.ValidationProblem(Error("confirmKey", $"Type {space.Key} exactly to confirm."));

        var user = await db.Users.FirstAsync(u => u.Id == current.RequireId());
        if (!await Auth.AuthEndpoints.VerifyPasswordOrCodeAsync(
                req.Password, req.Code, user, db, hasher, totp, siteSettings, detector, http, DateTimeOffset.UtcNow))
            return Results.Unauthorized();

        var pages = await db.Pages.IgnoreQueryFilters().Where(p => p.SpaceId == space.Id).ToListAsync();
        var pageIds = pages.Select(p => p.Id).ToList();
        var attachments = await db.Attachments.Where(a => pageIds.Contains(a.PageId)).ToListAsync();
        // Read the storage keys now; after the commit the rows are gone.
        var storageKeys = attachments.Select(a => a.StorageKey).ToList();
        var iconKey = space.IconKind == SpaceIconKind.Image
            ? media.KeyFor(ProfileMediaKind.SpaceIcon, space.Id)
            : null;

        var summary = new
        {
            space.Key,
            space.Name,
            Pages = pages.Count,
            Attachments = attachments.Count,
            Bytes = attachments.Sum(a => a.Size),
            WasPublic = space.IsPublic,
        };

        // The homepage pointer and the current-version pointers are restrict
        // FKs; clear them before the rows they point at are removed.
        space.HomepageId = null;
        foreach (var p in pages) p.CurrentVersionId = null;
        await db.SaveChangesAsync();

        // Neither of these has a foreign key to hang a cascade on: collab
        // documents are keyed by the page id as a string, and a watch names its
        // target by type and id.
        var documentNames = pageIds.Select(id => id.ToString()).ToList();
        db.CollabDocuments.RemoveRange(
            await db.CollabDocuments.Where(d => documentNames.Contains(d.DocumentName)).ToListAsync());
        db.Watches.RemoveRange(await db.Watches
            .Where(w => (w.TargetType == "space" && w.TargetId == space.Id)
                || (w.TargetType == "page" && pageIds.Contains(w.TargetId)))
            .ToListAsync());

        // Versions, attachments, comments, labels, views and restrictions
        // cascade from the page; webhooks, templates and space permissions
        // cascade from the space. Notifications are left alone: they are a
        // person's own history, and a link to a deleted page 404s gracefully.
        db.Pages.RemoveRange(pages);
        db.Spaces.Remove(space);
        audit.Record("space.deleted", "space", space.Id, summary);
        await detector.SpaceDeletedAsync(current.RequireId(), summary);
        await db.SaveChangesAsync();

        // Past the commit the space is gone whatever happens next, so a file
        // that will not delete is logged by path for the runbook's sweep
        // rather than failing a request that has already succeeded.
        var log = logs.CreateLogger(typeof(SpaceEndpoints));
        foreach (var storageKey in storageKeys)
        {
            try { storage.Delete(storageKey); }
            catch (Exception ex) { log.LogError(ex, "Orphaned attachment file after deleting space {Key}: {StorageKey}", summary.Key, storageKey); }
        }
        if (iconKey is not null)
        {
            try { media.Delete(iconKey); }
            catch (Exception ex) { log.LogError(ex, "Orphaned icon file after deleting space {Key}: {StorageKey}", summary.Key, iconKey); }
        }

        return Results.NoContent();
    }

    private static SpaceResponse ToResponse(Space s) =>
        new(s.Id, s.Key, s.Name, s.Description, s.Archived, s.HomepageId, s.CreatedAt, s.IsPublic, s.PublicComments,
            s.IconKind, s.IconValue, s.IconColor, ExportsOf(s));

    private static SpaceExportsDto ExportsOf(Space s) =>
        new(s.ExportMarkdown, s.ExportHtml, s.ExportPdf, s.ExportSite, s.ExportPack);

    /// <summary>
    /// Turns a space's export formats on and off. The whole set each time,
    /// so the audit entry says exactly what was on before and after. A space
    /// the caller cannot see is 404, as for every other right: holding the
    /// right means "may do this to spaces", not "may reach any space".
    /// </summary>
    private static async Task<IResult> UpdateExports(
        string key, SpaceExportsDto req, AppDbContext db, IAuditLogger audit, IPermissionService perms)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();

        var before = ExportsOf(space);
        space.ExportMarkdown = req.Markdown;
        space.ExportHtml = req.Html;
        space.ExportPdf = req.Pdf;
        space.ExportSite = req.Site;
        space.ExportPack = req.Pack;
        var after = ExportsOf(space);

        if (before != after)
            audit.Record("space.exports_changed", "space", space.Id, new { space.Key, Before = before, After = after });
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
