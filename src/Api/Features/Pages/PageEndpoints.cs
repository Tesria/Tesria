using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Mentions;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Storage;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Pages;

public static class PageEndpoints
{
    // An empty ProseMirror document; used when a page is created without content.
    private const string EmptyDoc = PageContent.EmptyDoc;

    public record CreatePageRequest(Guid SpaceId, Guid? ParentPageId, string Title, string? ContentJson);
    /// <param name="BaseVersion">
    /// Which published version this edit started from (dev-plan 8.6). The
    /// editor sends it so a write that would overwrite an unseen change is
    /// refused with 409 rather than silently winning. Optional: API and MCP
    /// callers omit it and keep last-write-wins.
    /// </param>
    public record UpdatePageRequest(string? Title, string ContentJson, string? ChangeComment, int? BaseVersion = null);
    /// <summary><paramref name="SpaceId"/>: another space to move to, with the pages under it (dev-plan 15.3).</summary>
    public record MovePageRequest(Guid? ParentPageId, int Index, Guid? SpaceId = null);
    public record CreateDraftRequest(Guid SpaceId, Guid? ParentPageId);
    public record PublishPageRequest(string Title, string ContentJson);
    public record DraftResponse(Guid Id);
    public record SetLayoutRequest(bool FullWidth);
    /// <summary>An emoji for the page, or null to take it away (dev-plan 15.7).</summary>
    public record SetEmojiRequest(string? Emoji);

    public record PageDetailResponse(
        Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, int Position, PageStatus Status,
        int CurrentVersionNumber, string ContentJson, bool FullWidth, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        /// <summary>Who created it: the SPA needs it to know whether "delete your own pages" applies (dev-plan 11.1).</summary>
        Guid CreatedById,
        /// <summary>
        /// Whether the caller may edit it, so the SPA shows Edit only to people
        /// who can use it (it was shown to every reader, 2026-09-23). Set on the
        /// single-page read only; null elsewhere.
        /// </summary>
        bool? CanEdit = null,
        /// <summary>Shown before the title (dev-plan 15.7).</summary>
        string? Emoji = null);
    public record PageVersionResponse(
        Guid Id, int VersionNumber, string? ChangeComment, Guid AuthorId,
        string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        DateTimeOffset CreatedAt);
    public record PageVersionContentResponse(
        Guid Id, int VersionNumber, string ContentJson, string? ChangeComment,
        Guid AuthorId, string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        DateTimeOffset CreatedAt);
    public record PageTreeNode(Guid Id, string Title, int Position, List<PageTreeNode> Children, string? Emoji = null);
    public record TrashedPageResponse(Guid Id, string Title, DateTimeOffset DeletedAt, Guid? DeletedById);

    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/pages").WithTags("Pages").RequireAuthorization();

        // Open to anonymous readers (dev-plan 5.2); the permission service masks what they may not see.
        group.MapGet("/tree", Tree).AllowAnonymous();
        group.MapGet("/trash", Trash);
        group.MapPost("/", Create);
        group.MapPost("/draft", CreateDraft);
        group.MapPost("/{id:guid}/publish", Publish);
        group.MapDelete("/{id:guid}/draft", DeleteDraft);
        group.MapGet("/{id:guid}", Get).AllowAnonymous();
        group.MapPut("/{id:guid}", Update);
        group.MapPut("/{id:guid}/move", Move);
        group.MapPost("/{id:guid}/copy", PageCopy.CopyAsync);
        group.MapPut("/{id:guid}/layout", SetLayout);
        group.MapPut("/{id:guid}/emoji", SetEmoji);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/restore", Restore);
        group.MapDelete("/{id:guid}/purge", Purge);
        group.MapGet("/{id:guid}/versions", ListVersions);
        group.MapGet("/{id:guid}/versions/{number:int}", GetVersion);
        group.MapPost("/{id:guid}/versions/{number:int}/restore", RestoreVersion);

        return routes;
    }

    /// <summary>
    /// Thin over <see cref="IPageWriter"/>: the one place a page is created
    /// (dev-plan 8.4). This method's whole job is turning that result into an
    /// HTTP one; the MCP tool turns the same result into a tool response.
    /// </summary>
    private static async Task<IResult> Create(
        CreatePageRequest req, IPageWriter writer, CancellationToken ct)
    {
        var result = await writer.CreateAsync(req.SpaceId, req.ParentPageId, req.Title, req.ContentJson, ct);
        return result.Status switch
        {
            PageWriteStatus.NotFound => Results.NotFound(),
            PageWriteStatus.Forbidden => Results.Forbid(),
            PageWriteStatus.Invalid => Results.ValidationProblem(Error(result.Field!, result.Message!)),
            _ => Results.Created($"/api/pages/{result.Page!.Id}", ToDetail(result.Page, result.Version!)),
        };
    }

    private static async Task<IResult> CreateDraft(
        CreateDraftRequest req, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        // Same space/parent permission checks as Create: a draft still needs
        // edit rights on the space it will live in.
        if (!await perms.CanViewSpaceAsync(req.SpaceId)) return Results.NotFound();
        if (!await perms.CanEditSpaceAsync(req.SpaceId)) return Results.Forbid();
        if (req.ParentPageId is { } parentForPerms && !await perms.CanEditPageAsync(parentForPerms))
            return Results.Forbid();

        if (!await db.Spaces.AnyAsync(s => s.Id == req.SpaceId))
            return Results.ValidationProblem(Error("spaceId", "Space not found."));

        if (req.ParentPageId is { } parentId)
        {
            var parent = await db.Pages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == parentId);
            if (parent is null)
                return Results.ValidationProblem(Error("parentPageId", "Parent page not found."));
            if (parent.SpaceId != req.SpaceId)
                return Results.ValidationProblem(Error("parentPageId", "Parent page is in a different space."));
        }

        var now = DateTimeOffset.UtcNow;
        var userId = current.RequireId();
        var page = new Page
        {
            Id = Guid.NewGuid(),
            SpaceId = req.SpaceId,
            ParentPageId = req.ParentPageId,
            Title = "Untitled",
            SearchText = string.Empty,
            Status = PageStatus.Draft,
            Position = await NextPositionAsync(db, req.SpaceId, req.ParentPageId),
            CreatedById = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var version = NewVersion(page, versionNumber: 1, EmptyDoc, userId, changeComment: null, now);
        db.Pages.Add(page);
        db.PageVersions.Add(version);

        await db.SaveChangesAsync();
        page.CurrentVersionId = version.Id;
        // A draft is invisible: no audit entry, notification, or webhook: it
        // isn't a real event until Publish.
        await db.SaveChangesAsync();

        return Results.Ok(new DraftResponse(page.Id));
    }

    private static async Task<IResult> Publish(
        Guid id, PublishPageRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        IPermissionService perms, INotificationService notifications, IWebhookDispatcher webhooks)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null);
        if (page is null || page.CurrentVersion is null) return Results.NotFound();

        // Tolerate a retried/double-clicked publish as a safe no-op rather than
        // erroring, instead of treating "already published" as not-found.
        if (page.Status != PageStatus.Draft)
            return Results.Ok(ToDetail(page, page.CurrentVersion));

        var userId = current.RequireId();
        // Ownership guard: the draft's own creator, or anyone with edit rights
        // on its space. Page ids are unguessable in practice, but check anyway.
        if (page.CreatedById != userId && !await perms.CanEditSpaceAsync(page.SpaceId))
            return Results.Forbid();

        var title = (req.Title ?? "").Trim();
        if (title.Length == 0)
            return Results.ValidationProblem(Error("title", "Title is required."));
        if (!PageContent.TryNormalize(req.ContentJson, out var content))
            return Results.ValidationProblem(Error("contentJson", "Content must be valid JSON."));

        // Nothing was ever "really" saved yet, so the published page starts
        // clean at v1 with the real content: mutate it in place rather than
        // appending a v2 that would leave a confusing empty-v1/real-v2 pair.
        var version = page.CurrentVersion;
        version.ContentJson = content;
        page.Title = title;
        page.SearchText = PageContent.BuildSearchText(title, content);
        page.Status = PageStatus.Current;
        page.UpdatedAt = DateTimeOffset.UtcNow;

        await RecordPageCreatedAsync(page, userId, audit, notifications);
        await NotifyNewMentionsAsync(page, before: null, content, userId, perms, notifications);
        await db.SaveChangesAsync();
        await DispatchPageCreatedWebhookAsync(page, webhooks);

        return Results.Ok(ToDetail(page, version));
    }

    private static async Task<IResult> DeleteDraft(
        Guid id, AppDbContext db, CurrentUser current, IPermissionService perms,
        IAttachmentStorage storage, ILoggerFactory logs)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.Status == PageStatus.Draft && p.DeletedAt == null);
        if (page is null) return Results.NotFound();

        var userId = current.RequireId();
        if (page.CreatedById != userId && !await perms.CanEditSpaceAsync(page.SpaceId))
            return Results.Forbid();

        // Nothing was ever really saved, so this is a hard delete, not a trash:
        //clear the current-version pointer first (the restrict FK would
        // otherwise block it), then remove the page; versions cascade.
        var storageKeys = await StorageKeysOfAsync(db, [page.Id]);
        page.CurrentVersionId = null;
        await db.SaveChangesAsync();
        db.Pages.Remove(page);
        await db.SaveChangesAsync();
        DeleteFiles(storage, logs, storageKeys, "discarding a draft");
        return Results.NoContent();
    }

    /// <summary>The files behind a set of pages' attachments, read before the rows cascade away.</summary>
    private static Task<List<string>> StorageKeysOfAsync(AppDbContext db, IReadOnlyCollection<Guid> pageIds) =>
        db.Attachments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => pageIds.Contains(a.PageId))
            .Select(a => a.StorageKey)
            .ToListAsync();

    /// <summary>
    /// Removes attachment files once their rows are gone. Hard-deleting a page
    /// used to drop the rows and leave the files on the uploads volume, where
    /// nothing pointed at them (found 2026-09-23); deleting a space already
    /// did this. After the commit and best effort, as there: a file that will
    /// not delete is logged by key for the runbook's sweep.
    /// </summary>
    private static void DeleteFiles(IAttachmentStorage storage, ILoggerFactory logs, List<string> storageKeys, string what)
    {
        var log = logs.CreateLogger(typeof(PageEndpoints));
        foreach (var storageKey in storageKeys)
        {
            try { storage.Delete(storageKey); }
            catch (Exception ex) { log.LogError(ex, "Orphaned attachment file after {What}: {StorageKey}", what, storageKey); }
        }
    }

    private static async Task<IResult> Get(
        Guid id, AppDbContext db, IPermissionService perms, CurrentUser current, HttpContext http)
    {
        var page = await db.Pages.AsNoTracking()
            .Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page?.CurrentVersion is null) return Results.NotFound();
        // 404 rather than 403 so restricted pages aren't discoverable.
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();

        // Anonymous readers (dev-plan 5.2) may cache for a minute, and revalidate
        // by ETag; unpublishing therefore takes effect within that minute.
        // Signed-in responses are never shared-cacheable.
        if (current.Id is null)
        {
            var etag = $"\"{page.CurrentVersion.Id:N}-{(page.FullWidth ? 1 : 0)}\"";
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "public, max-age=60";
            if (http.Request.Headers.IfNoneMatch.Any(v => string.Equals(v, etag, StringComparison.Ordinal)))
                return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        else
        {
            http.Response.Headers.CacheControl = "private, no-store";
        }

        await RecordViewAsync(id, db, current, http);
        var canEdit = current.Id is not null && await perms.CanEditPageAsync(id);
        return Results.Ok(ToDetail(page, page.CurrentVersion) with { CanEdit = canEdit });
    }

    /// <summary>
    /// Records a read for the usage KPIs (dev-plan 0.3), after the permission
    /// check so a refused read is never counted.
    ///
    /// Browser sessions only. An API token is a script: a nightly export would
    /// otherwise dwarf every human in "most viewed pages" and make the number
    /// meaningless. The test matches the one the Smart policy scheme uses to
    /// pick its handler, so the two cannot disagree about what a token request is.
    ///
    /// <see cref="PageView.UserId"/> is left nullable for anonymous readers
    /// (dev-plan Phase 5); today the caller is always signed in.
    /// </summary>
    private static async Task RecordViewAsync(
        Guid pageId, AppDbContext db, CurrentUser current, HttpContext http)
    {
        if (http.Request.Headers.ContainsKey("Authorization")) return;

        try
        {
            db.PageViews.Add(new PageView
            {
                Id = Guid.NewGuid(),
                PageId = pageId,
                UserId = current.Id,
                ViewedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception)
        {
            // A telemetry write must never fail the read it is measuring.
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<IResult> Update(
        Guid id, UpdatePageRequest req, IPageWriter writer, CancellationToken ct)
    {
        var result = await writer.UpdateAsync(id, req.Title, req.ContentJson, req.ChangeComment, ct, req.BaseVersion);
        return result.Status switch
        {
            PageWriteStatus.NotFound => Results.NotFound(),
            PageWriteStatus.Forbidden => Results.Forbid(),
            PageWriteStatus.Invalid => Results.ValidationProblem(Error(result.Field!, result.Message!)),
            // 409 carries the page as it stands, not just a refusal: the
            // editor reconciles against it and shows the difference, which it
            // cannot do from a status code alone (dev-plan 8.6).
            PageWriteStatus.Conflict => Results.Json(
                ToDetail(result.Page!, result.Version!), statusCode: StatusCodes.Status409Conflict),
            _ => Results.Ok(ToDetail(result.Page!, result.Version!)),
        };
    }

    private static async Task<IResult> Move(
        Guid id, MovePageRequest req, AppDbContext db, IPermissionService perms, IAuditLogger audit)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

        // To another space (dev-plan 15.3): the page and everything under it
        // change space together, drafts and trash included, so nothing is
        // left behind pointing at a parent that is no longer beside it.
        var fromSpace = page.SpaceId;
        if (req.SpaceId is { } targetSpace && targetSpace != page.SpaceId)
        {
            if (!await db.Spaces.AnyAsync(sp => sp.Id == targetSpace)) return Results.NotFound();
            if (!await perms.CanEditSpaceAsync(targetSpace)) return Results.Forbid();
            if (req.ParentPageId is { } p0)
            {
                var parent = await db.Pages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p0);
                if (parent is null || parent.SpaceId != targetSpace)
                    return Results.ValidationProblem(Error("parentPageId", "Parent page not found in that space."));
                if (!await perms.CanEditPageAsync(p0)) return Results.Forbid();
            }
            var subtree = await SubtreeIdsAsync(db, page.Id, page.SpaceId);
            var moving = await db.Pages.IgnoreQueryFilters().Where(x => subtree.Contains(x.Id)).ToListAsync();
            foreach (var x in moving) x.SpaceId = targetSpace;
            page.SpaceId = targetSpace;
            audit.Record("page.moved", "page", page.Id, new { page.Title, FromSpace = fromSpace, ToSpace = targetSpace, Pages = moving.Count });
        }
        // Re-parenting also needs edit rights on the destination.
        if (req.ParentPageId is { } destination && !await perms.CanEditPageAsync(destination))
            return Results.Forbid();

        if (req.ParentPageId is { } newParentId)
        {
            if (newParentId == id)
                return Results.ValidationProblem(Error("parentPageId", "A page cannot be its own parent."));
            var newParent = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == newParentId);
            if (newParent is null)
                return Results.ValidationProblem(Error("parentPageId", "Parent page not found."));
            if (newParent.SpaceId != page.SpaceId)
                return Results.ValidationProblem(Error("parentPageId", "Parent page is in a different space. Name the space to move it there."));
            if (await WouldCreateCycleAsync(db, movingPageId: id, newParentId))
                return Results.ValidationProblem(Error("parentPageId", "Cannot move a page beneath one of its own descendants."));
        }

        // Index is a slot among the destination's current siblings (0 = first),
        // not a raw Position value: the caller shouldn't have to know or
        // guess at other pages' Position ints. We insert the moving page at
        // that slot and renumber the whole sibling group, so a drag-and-drop
        // UI can just say "this landed at index 2" and never risk colliding
        // with, or leaving a gap relative to, its new neighbors.
        var siblings = await db.Pages
            .Where(p => p.SpaceId == page.SpaceId && p.ParentPageId == req.ParentPageId && p.Id != id)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .ToListAsync();
        var index = Math.Clamp(req.Index, 0, siblings.Count);
        siblings.Insert(index, page);

        page.ParentPageId = req.ParentPageId;
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].Position = i;
        page.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Sets the page's reading-width preference (normal vs. full-width). Pure
    /// display metadata, like Move: no new version, audit entry, or webhook.
    /// Unlike Move, this must also reach an unpublished draft: the editor shows
    /// the full-width toggle while composing a brand-new page, and the draft is
    /// the only id that exists until Publish. Hence IgnoreQueryFilters() with
    /// the soft-delete half of the filter reapplied by hand: a trashed page
    /// still has no business changing its layout.
    /// </summary>
    private static async Task<IResult> SetLayout(
        Guid id, SetLayoutRequest req, AppDbContext db, IPermissionService perms)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

        page.FullWidth = req.FullWidth;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Sets or clears the page's emoji (dev-plan 15.7). Whoever may edit the
    /// page may, as with its width; like the width it is not content, so it
    /// makes no new version.
    /// </summary>
    private static async Task<IResult> SetEmoji(
        Guid id, SetEmojiRequest req, AppDbContext db, IPermissionService perms)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

        if (string.IsNullOrWhiteSpace(req.Emoji))
            page.Emoji = null;
        else
        {
            var (emoji, error) = Features.Spaces.SpaceIcons.NormalizeEmoji(req.Emoji);
            if (error is not null) return Results.ValidationProblem(Error("emoji", "Pick a single emoji."));
            page.Emoji = emoji;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        Guid id, AppDbContext db, CurrentUser current, IAuditLogger audit, IPermissionService perms,
        Infrastructure.Permissions.IInstancePermissions rights,
        ISecurityDetector detector)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();
        // The space's own rules decide where; this decides whether at all
        // (dev-plan 11.1). Users may trash what they wrote unless the right is
        // taken away, and other people's work only if the right is granted.
        if (await DeniedByInstanceRightAsync(db, rights, current, id) is { } refusal) return refusal;

        // Soft-delete (trash) the page and its whole subtree, so the tree stays
        // consistent and the deletion can be restored (PLAN §5 in-app safety net).
        var now = DateTimeOffset.UtcNow;
        var userId = current.RequireId();
        var subtree = await CollectLiveSubtreeAsync(db, page.SpaceId, id);
        foreach (var p in subtree)
        {
            p.DeletedAt = now;
            p.DeletedById = userId;
        }
        audit.Record("page.trashed", "page", page.Id, new { page.Title, SubtreeCount = subtree.Count });
        await detector.PagesRemovedAsync(userId, subtree.Count, "page.trashed", page.Id);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Refuses a deletion the caller's role does not allow: their own page
    /// needs <c>pages.delete_own</c>, anyone else's needs
    /// <c>pages.delete_any</c> (dev-plan 11.1).
    /// </summary>
    private static async Task<IResult?> DeniedByInstanceRightAsync(
        AppDbContext db, Infrastructure.Permissions.IInstancePermissions rights,
        CurrentUser current, Guid pageId)
    {
        var authorId = await db.Pages.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == pageId).Select(p => (Guid?)p.CreatedById).FirstOrDefaultAsync();
        var mine = authorId == current.Id;
        var key = mine
            ? Infrastructure.Permissions.InstancePermissions.PagesDeleteOwn
            : Infrastructure.Permissions.InstancePermissions.PagesDeleteAny;
        if (await rights.HasAsync(key)) return null;

        return Results.Json(new
        {
            title = "Forbidden",
            status = 403,
            code = "permission_required",
            permission = key,
            message = mine
                ? "Your role does not allow deleting pages."
                : "Your role only allows deleting pages you created.",
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> Restore(
        Guid id, AppDbContext db, IAuditLogger audit, IPermissionService perms)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);
        if (page is null) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

        // If the original parent no longer exists (still trashed or purged),
        // restore to the space root so the page is not orphaned.
        if (page.ParentPageId is { } pid && !await db.Pages.AnyAsync(p => p.Id == pid))
            page.ParentPageId = null;

        foreach (var p in await CollectTrashedSubtreeAsync(db, page.SpaceId, id))
        {
            p.DeletedAt = null;
            p.DeletedById = null;
        }
        audit.Record("page.restored", "page", page.Id, new { page.Title });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Purge(
        Guid id, AppDbContext db, IAuditLogger audit, IPermissionService perms,
        CurrentUser current, ISecurityDetector detector, HttpContext http, IConfiguration config,
        Infrastructure.Permissions.IInstancePermissions rights, IAttachmentStorage storage, ILoggerFactory logs)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);
        if (page is null) return Results.NotFound();
        // Permanent deletion is an admin-level act on the space.
        if (!await perms.CanAdminSpaceAsync(page.SpaceId)) return Results.Forbid();
        if (await DeniedByInstanceRightAsync(db, rights, current, id) is { } refusal) return refusal;
        // ...and irreversible, so it is sudo territory (dev-plan 3.5).
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        var subtree = await CollectTrashedSubtreeAsync(db, page.SpaceId, id);
        var storageKeys = await StorageKeysOfAsync(db, subtree.Select(p => p.Id).ToList());
        // Clear current-version pointers so the cascade to versions is not blocked
        // by the restrict FK, then hard-delete the subtree (versions, attachments,
        // and comments cascade).
        foreach (var p in subtree) p.CurrentVersionId = null;
        await db.SaveChangesAsync();
        db.Pages.RemoveRange(subtree);
        // Recorded before SaveChanges so the entry commits with the deletion.
        audit.Record("page.purged", "page", page.Id, new { page.Title, SubtreeCount = subtree.Count });
        await detector.PagesRemovedAsync(current.RequireId(), subtree.Count, "page.purged", page.Id);
        await db.SaveChangesAsync();
        DeleteFiles(storage, logs, storageKeys, "deleting a page permanently");
        return Results.NoContent();
    }

    /// <summary>A page and every page under it, drafts and trash included.</summary>
    internal static async Task<HashSet<Guid>> SubtreeIdsAsync(AppDbContext db, Guid rootId, Guid spaceId)
    {
        var all = await db.Pages.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.SpaceId == spaceId).Select(p => new { p.Id, p.ParentPageId }).ToListAsync();
        var ids = new HashSet<Guid> { rootId };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var p in all)
                if (p.ParentPageId is { } parent && ids.Contains(parent) && ids.Add(p.Id)) added = true;
        }
        return ids;
    }

    private static async Task<IResult> Trash(Guid spaceId, AppDbContext db, IPermissionService perms)
    {
        if (!await db.Spaces.AnyAsync(s => s.Id == spaceId)) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(spaceId)) return Results.NotFound();

        var trashed = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.SpaceId == spaceId && p.DeletedAt != null)
            .ToListAsync();
        var trashedIds = trashed.Select(p => p.Id).ToHashSet();

        // List only "trash roots": the pages actually deleted (whose parent is
        // not itself trashed); each stands for one restorable subtree.
        var candidates = trashed
            .Where(p => p.ParentPageId is null || !trashedIds.Contains(p.ParentPageId.Value))
            .OrderByDescending(p => p.DeletedAt)
            .ToList();
        // Viewing the space is not viewing every page in it: a restricted
        // page's title stayed restricted in the tree and search, and showed
        // here to anyone who could open the space (found 2026-09-23).
        var roots = new List<TrashedPageResponse>();
        foreach (var p in candidates)
            if (await perms.CanViewPageAsync(p.Id))
                roots.Add(new TrashedPageResponse(p.Id, p.Title, p.DeletedAt!.Value, p.DeletedById));
        return Results.Ok(roots);
    }

    private static async Task<IResult> ListVersions(Guid id, AppDbContext db, IPermissionService perms)
    {
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        var versions = await db.PageVersions.AsNoTracking()
            .Where(v => v.PageId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new PageVersionResponse(
                v.Id, v.VersionNumber, v.ChangeComment, v.AuthorId,
                v.Author == null ? "Deleted user" : v.Author.DisplayName,
                v.Author == null || v.Author.AvatarKey == null ? null : v.Author.AvatarHash,
                v.Author == null ? null : v.Author.AvatarVariant,
                v.CreatedAt))
            .ToListAsync();
        return Results.Ok(versions);
    }

    private static async Task<IResult> GetVersion(
        Guid id, int number, AppDbContext db, IPermissionService perms)
    {
        // Guard here too: page versions are queried by page id, so they would
        // otherwise bypass the page's view restrictions.
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();

        var v = await db.PageVersions.AsNoTracking()
            .Include(v => v.Author)
            .FirstOrDefaultAsync(v => v.PageId == id && v.VersionNumber == number);
        return v is null
            ? Results.NotFound()
            : Results.Ok(new PageVersionContentResponse(
                v.Id, v.VersionNumber, v.ContentJson, v.ChangeComment, v.AuthorId,
                v.Author?.DisplayName ?? "Deleted user",
                v.Author?.AvatarKey is null ? null : v.Author.AvatarHash,
                v.Author?.AvatarVariant,
                v.CreatedAt));
    }

    private static async Task<IResult> RestoreVersion(
        Guid id, int number, AppDbContext db, IPermissionService perms, IPageWriter writer, CancellationToken ct)
    {
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        var source = await db.PageVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PageId == id && v.VersionNumber == number, ct);
        if (source is null) return Results.NotFound();

        // Rollback preserves history: it appends a new version copying the old
        // content rather than deleting anything. It goes through the writer
        // like any other update, so watchers, webhooks, the audit log and
        // anyone with the page open hear about it; a restore used to change
        // the page without a word to any of them (found 2026-09-23).
        var result = await writer.UpdateAsync(id, null, source.ContentJson, $"Restored from version {number}", ct);
        return result.Status switch
        {
            PageWriteStatus.NotFound => Results.NotFound(),
            PageWriteStatus.Forbidden => Results.Forbid(),
            PageWriteStatus.Invalid => Results.ValidationProblem(Error(result.Field!, result.Message!)),
            _ => Results.Ok(ToDetail(result.Page!, result.Version!)),
        };
    }

    private static async Task<IResult> Tree(Guid spaceId, AppDbContext db, IPermissionService perms)
    {
        if (!await db.Spaces.AnyAsync(s => s.Id == spaceId)) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(spaceId)) return Results.NotFound();

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == spaceId)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.Position, p.ParentPageId, p.Emoji })
            .ToListAsync();

        // Drop pages the caller may not view (restrictions are inherited, so a
        // hidden parent's children are hidden with it).
        var visible = new List<Guid>();
        foreach (var p in pages)
            if (await perms.CanViewPageAsync(p.Id)) visible.Add(p.Id);
        pages = pages.Where(p => visible.Contains(p.Id)).ToList();

        var byParent = pages.ToLookup(p => p.ParentPageId);
        List<PageTreeNode> Build(Guid? parentId) =>
            byParent[parentId]
                .Select(p => new PageTreeNode(p.Id, p.Title, p.Position, Build(p.Id), p.Emoji))
                .ToList();

        return Results.Ok(Build(null));
    }

    // -- helpers --------------------------------------------------------------

    /// <summary>The live page <paramref name="rootId"/> and all its live descendants (tracked).</summary>
    private static Task<List<Page>> CollectLiveSubtreeAsync(AppDbContext db, Guid spaceId, Guid rootId) =>
        CollectSubtreeAsync(db.Pages.Where(p => p.SpaceId == spaceId), rootId);

    /// <summary>The trashed page <paramref name="rootId"/> and all its trashed descendants (tracked).</summary>
    private static Task<List<Page>> CollectTrashedSubtreeAsync(AppDbContext db, Guid spaceId, Guid rootId) =>
        CollectSubtreeAsync(
            db.Pages.IgnoreQueryFilters().Where(p => p.SpaceId == spaceId && p.DeletedAt != null),
            rootId);

    private static async Task<List<Page>> CollectSubtreeAsync(IQueryable<Page> scope, Guid rootId)
    {
        var pages = await scope.ToListAsync();
        var byParent = pages.ToLookup(p => p.ParentPageId);
        var result = new List<Page>();
        var stack = new Stack<Guid>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var node = pages.FirstOrDefault(p => p.Id == current);
            if (node is null) continue;
            result.Add(node);
            foreach (var child in byParent[current]) stack.Push(child.Id);
        }
        return result;
    }

    private static async Task<int> NextPositionAsync(AppDbContext db, Guid spaceId, Guid? parentId)
    {
        var max = await db.Pages
            .Where(p => p.SpaceId == spaceId && p.ParentPageId == parentId)
            .Select(p => (int?)p.Position)
            .MaxAsync();
        return (max ?? -1) + 1;
    }

    private static async Task<bool> WouldCreateCycleAsync(AppDbContext db, Guid movingPageId, Guid newParentId)
    {
        // Walk up from the proposed parent; a cycle exists if we reach the page
        // being moved. Guarded by a hop limit as defense against a corrupt tree.
        Guid? cursor = newParentId;
        for (var hops = 0; cursor is { } id && hops < 10_000; hops++)
        {
            if (id == movingPageId) return true;
            cursor = await db.Pages.Where(p => p.Id == id)
                .Select(p => p.ParentPageId).FirstOrDefaultAsync();
        }
        return false;
    }

    /// <summary>
    /// Tells anyone newly mentioned in the page's content that they were
    /// (dev-plan Phase 7 Wave C): the people already mentioned in the
    /// previous version are skipped, so fixing a typo does not re-ping
    /// everyone the page names.
    ///
    /// Each recipient is checked with <see cref="IPermissionService.AsUser"/>
    /// before anything is queued: the notification carries the page title,
    /// and mentioning someone must not be a way to leak the title of a page
    /// they cannot open. They are told nothing, rather than told and then
    /// given a 404.
    /// </summary>
    private static async Task NotifyNewMentionsAsync(
        Page page, string? before, string after, Guid authorId,
        IPermissionService perms, INotificationService notifications)
    {
        foreach (var userId in Mentions.NewlyMentioned(before, after, authorId))
        {
            if (!await perms.AsUser(userId).CanViewPageAsync(page.Id)) continue;
            await notifications.NotifyUserAsync(
                userId, "user.mentioned", "page", page.Id, authorId, new { page.Title });
        }
    }

    /// <summary>The shared "a new page exists" side effects fired by both Create and Publish.</summary>
    private static async Task RecordPageCreatedAsync(
        Page page, Guid userId, IAuditLogger audit, INotificationService notifications)
    {
        audit.Record("page.created", "page", page.Id, new { page.Title, page.SpaceId });
        // Space watchers hear about new pages; the page itself has no watchers
        // yet since nobody could watch it before it existed. The notification
        // points at the new page so its recipient can go straight to it.
        await notifications.NotifyOfNewPageAsync(page.Id, page.SpaceId, userId, new { page.Title });
    }

    private static Task DispatchPageCreatedWebhookAsync(Page page, IWebhookDispatcher webhooks) =>
        webhooks.DispatchAsync(page.SpaceId, "page.created", "page", page.Id, new { page.Title });

    private static PageVersion NewVersion(
        Page page, int versionNumber, string content, Guid authorId,
        string? changeComment, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        PageId = page.Id,
        VersionNumber = versionNumber,
        ContentJson = content,
        AuthorId = authorId,
        ChangeComment = changeComment,
        CreatedAt = createdAt,
    };


    /// <summary>Concatenates the text nodes of a ProseMirror document, ignoring structure.</summary>
    private static PageDetailResponse ToDetail(Page page, PageVersion version) => new(
        page.Id, page.SpaceId, page.ParentPageId, page.Title, page.Position, page.Status,
        version.VersionNumber, version.ContentJson, page.FullWidth, page.CreatedAt, page.UpdatedAt,
        page.CreatedById, Emoji: page.Emoji);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
