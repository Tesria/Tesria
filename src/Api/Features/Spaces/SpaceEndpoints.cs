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
    /// <param name="EveryoneAccess">
    /// What every signed-in account may do in the new space (dev-plan 21.2):
    /// null for nothing (only the people in its groups), or 0, 1 or 2 for
    /// View, Edit or Admin. A raw element, because "left out" and "null" mean
    /// different things here: left out keeps a new space as open as it has
    /// always been (Admin), so API clients written before the wizard do not
    /// change behavior.
    /// </param>
    /// <param name="Members">Who goes in each of the space's groups from the start (21.2). The creator is always in Admins.</param>
    public record CreateSpaceRequest(
        string Key, string Name, string? Description,
        List<GroupMembersRequest>? Members = null)
    {
        // A property rather than a parameter: the OpenAPI document cannot
        // describe a JsonElement parameter's default value.
        public System.Text.Json.JsonElement EveryoneAccess { get; init; }
    }
    /// <summary>The people to put in one of a new space's groups.</summary>
    public record GroupMembersRequest(SpaceGroupRole Role, List<Guid>? UserIds);

    /// <summary>More than a wizard sends; enough that nobody needs to add the rest afterwards one by one.</summary>
    public const int MaxMembersAtCreate = 500;
    /// <summary>The key proves you are looking at the right space; the password proves it is you.</summary>
    public record DeleteSpaceRequest(string ConfirmKey, string? Password, string? Code);
    public record DeletionPreviewResponse(string Key, string Name, int Pages, int Attachments, long Bytes, bool IsPublic);
    public record UpdateSpaceRequest(
        string Name, string? Description,
        SpaceIconKind? IconKind = null, string? IconValue = null, int? IconColor = null,
        /// <summary>Plain, numbered or bulleted page tree (dev-plan 15.8); omitted leaves it alone.</summary>
        SpaceTreeStyle? TreeStyle = null);
    public record SpaceResponse(
        Guid Id, string Key, string Name, string? Description,
        bool Archived, Guid? HomepageId, DateTimeOffset CreatedAt,
        bool IsPublic, bool PublicComments,
        SpaceIconKind IconKind, string? IconValue, int? IconColor,
        SpaceExportsDto Exports,
        SpaceTreeStyle TreeStyle = SpaceTreeStyle.Plain,
        /// <summary>
        /// What the caller may do here, on the single-space read only (null
        /// elsewhere): whether they may add and change pages, and whether
        /// they may change the space itself. The page leaves out what would
        /// only be refused (QA T3-012).
        /// </summary>
        bool? CanEdit = null, bool? CanAdmin = null);

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
        group.MapGet("/", List).AllowAnonymous().Produces<List<SpaceResponse>>();
        group.MapPost("/", Create).Produces<SpaceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Infrastructure.Permissions.InstancePermissions.SpacesCreate);
        group.MapGet("/{key}", GetByKey).AllowAnonymous().Produces<SpaceResponse>();
        group.MapPut("/{key}", Update).Produces<SpaceResponse>();
        group.MapPost("/{key}/archive",
            (string key, AppDbContext db, IAuditLogger audit, IPermissionService perms)
                => ArchiveEndpoint(key, true, db, audit, perms)).Produces<SpaceResponse>();
        group.MapPost("/{key}/unarchive",
            (string key, AppDbContext db, IAuditLogger audit, IPermissionService perms)
                => ArchiveEndpoint(key, false, db, audit, perms)).Produces<SpaceResponse>();

        // Deleting a space is an instance right, not a space permission
        // (dev-plan 11.3): a space's own administrator archives, which is
        // reversible; destroying one is the instance's decision.
        group.MapGet("/{key}/deletion-preview", DeletionPreview).Produces<DeletionPreviewResponse>()
            .RequirePermission(InstancePermissions.SpacesDelete);
        group.MapDelete("/{key}", Delete).Produces(StatusCodes.Status204NoContent)
            .RequirePermission(InstancePermissions.SpacesDelete);

        // Which exports a space allows (dev-plan 12.3). An instance right,
        // like deleting: the setting exists for spaces more sensitive than
        // the rest, and that judgment belongs to the instance's
        // administrators rather than to whoever created the space.
        group.MapPut("/{key}/exports", UpdateExports).Produces<SpaceResponse>()
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

    /// <summary>The database's limit, said before the database refuses it (QA T3-005).</summary>
    internal static readonly string NameTooLong = $"Name can be at most {Space.MaxNameLength} characters.";
    internal static readonly string DescriptionTooLong =
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Description can be at most {Space.MaxDescriptionLength:N0} characters.");

    /// <summary>The description to store, or null for none (blank).</summary>
    private static string? NormalizeDescription(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

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
        if (name.Length > Space.MaxNameLength)
            return Results.ValidationProblem(Error("name", NameTooLong));
        var description = NormalizeDescription(req.Description);
        if (description is { Length: > Space.MaxDescriptionLength })
            return Results.ValidationProblem(Error("description", DescriptionTooLong));

        if (!TryEveryoneAccess(req.EveryoneAccess, out var everyone))
            return Results.ValidationProblem(Error("everyoneAccess",
                "Choose null (only the people in its groups), 0 (view), 1 (edit) or 2 (administer)."));

        var creatorId = current.RequireId();
        var members = new List<(SpaceGroupRole Role, Guid UserId)>();
        foreach (var entry in req.Members ?? [])
        {
            if (!Enum.IsDefined(entry.Role))
                return Results.ValidationProblem(Error("members", "Choose Viewers, Editors, Admins or Reviewers."));
            foreach (var userId in (entry.UserIds ?? []).Distinct())
                // The creator is in Admins already; naming them there again is not an error.
                if (!(entry.Role == SpaceGroupRole.Admins && userId == creatorId)
                    && !members.Contains((entry.Role, userId)))
                    members.Add((entry.Role, userId));
        }
        if (members.Count > MaxMembersAtCreate)
            return Results.ValidationProblem(Error("members",
                $"Add at most {MaxMembersAtCreate} people while creating a space; add the rest in its Permissions tab."));
        var memberIds = members.Select(m => m.UserId).Distinct().ToList();
        if (memberIds.Count > 0 && await db.Users.CountAsync(u => memberIds.Contains(u.Id)) != memberIds.Count)
            return Results.ValidationProblem(Error("members", "Someone chosen no longer has an account here. Choose again."));

        if (await db.Spaces.AnyAsync(s => s.Key == key))
            return Results.Conflict(new { message = $"A space with key '{key}' already exists." });

        var space = new Space
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = description,
            CreatedById = creatorId,
            CreatedAt = DateTimeOffset.UtcNow,
            // Admin when the request does not say (as open as a new space has
            // always been). No level needs the password again here, unlike
            // widening it later (21.1 keeps 15.3's make-open protections for
            // that): the space is new and empty and its creator is its
            // administrator, so nothing anybody can already reach is opened.
            // The level is in the audit entry below.
            EveryoneAccess = everyone,
        };
        db.Spaces.Add(space);
        // Its four groups (dev-plan 21.1), with its creator in Admins: an
        // explicit administrator from the start, so they can close it later.
        var groups = Infrastructure.Permissions.SpaceGroups.Add(db, space.Id, space.CreatedAt);
        db.UserGroups.Add(new UserGroup
        {
            GroupId = groups[SpaceGroupRole.Admins].Id, UserId = space.CreatedById, AddedAt = space.CreatedAt,
        });
        audit.Record("space.created", "space", space.Id,
            new { space.Key, space.Name, EveryoneAccess = everyone?.ToString() ?? "None" });
        // The wizard's people (21.2). The creator is an explicit administrator
        // of the new space, so 21.1's rules allow every one of these, Admins
        // included. Each is audited against the space, as an addition in the
        // Permissions tab is.
        foreach (var (role, userId) in members)
        {
            db.UserGroups.Add(new UserGroup { GroupId = groups[role].Id, UserId = userId, AddedAt = space.CreatedAt });
            audit.Record("space.group_member_added", "space", space.Id,
                new { GroupId = groups[role].Id, Role = role.ToString(), UserId = userId, Via = "space.created" });
        }
        // One save, so one transaction: the space, its groups, what everyone
        // gets and its members are made together or not at all.
        await db.SaveChangesAsync();

        return Results.Created($"/api/spaces/{space.Key}", ToResponse(space));
    }

    /// <summary>The level a create asks for: left out is Admin (21.2's compatibility rule), JSON null is nothing.</summary>
    internal static bool TryEveryoneAccess(System.Text.Json.JsonElement raw, out SpaceOperation? access)
    {
        access = null;
        switch (raw.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Undefined:
                access = SpaceOperation.Admin;
                return true;
            case System.Text.Json.JsonValueKind.Null:
                return true;
            case System.Text.Json.JsonValueKind.Number when raw.TryGetInt32(out var n) && Enum.IsDefined((SpaceOperation)n):
                access = (SpaceOperation)n;
                return true;
            default:
                return false;
        }
    }

    private static async Task<IResult> GetByKey(string key, AppDbContext db, IPermissionService perms, CurrentUser current)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == normalizedKey);
        if (space is null) return Results.NotFound();
        // 404 rather than 403 so a hidden space's existence isn't disclosed.
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        return Results.Ok(ToResponse(space) with
        {
            CanEdit = current.Id is not null && await perms.CanEditSpaceAsync(space.Id),
            CanAdmin = current.Id is not null && await perms.CanAdminSpaceAsync(space.Id),
        });
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
        if (name.Length > Space.MaxNameLength)
            return Results.ValidationProblem(Error("name", NameTooLong));
        // A description longer than the limit from before it existed may be
        // sent back unchanged (the Details form always sends it), so renaming
        // such a space still works; a new or edited one is held to the limit.
        var description = NormalizeDescription(req.Description);
        if (description is { Length: > Space.MaxDescriptionLength } && description != space.Description)
            return Results.ValidationProblem(Error("description", DescriptionTooLong));

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

        if (req.TreeStyle is { } style)
        {
            if (!Enum.IsDefined(style)) return Results.ValidationProblem(Error("treeStyle", "Choose plain, numbered or bulleted."));
            space.TreeStyle = style;
        }

        space.Name = name;
        space.Description = description;
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
            s.IconKind, s.IconValue, s.IconColor, ExportsOf(s), s.TreeStyle);

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
