using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Pages;

public static class PageEndpoints
{
    // An empty ProseMirror document; used when a page is created without content.
    private const string EmptyDoc = """{"type":"doc","content":[]}""";

    public record CreatePageRequest(Guid SpaceId, Guid? ParentPageId, string Title, string? ContentJson);
    public record UpdatePageRequest(string? Title, string ContentJson, string? ChangeComment);
    public record MovePageRequest(Guid? ParentPageId, int Index);
    public record CreateDraftRequest(Guid SpaceId, Guid? ParentPageId);
    public record PublishPageRequest(string Title, string ContentJson);
    public record DraftResponse(Guid Id);
    public record SetLayoutRequest(bool FullWidth);

    public record PageDetailResponse(
        Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, int Position, PageStatus Status,
        int CurrentVersionNumber, string ContentJson, bool FullWidth, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    public record PageVersionResponse(
        Guid Id, int VersionNumber, string? ChangeComment, Guid AuthorId,
        string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        DateTimeOffset CreatedAt);
    public record PageVersionContentResponse(
        Guid Id, int VersionNumber, string ContentJson, string? ChangeComment,
        Guid AuthorId, string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        DateTimeOffset CreatedAt);
    public record PageTreeNode(Guid Id, string Title, int Position, List<PageTreeNode> Children);
    public record TrashedPageResponse(Guid Id, string Title, DateTimeOffset DeletedAt, Guid? DeletedById);

    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/pages").WithTags("Pages").RequireAuthorization();

        group.MapGet("/tree", Tree);
        group.MapGet("/trash", Trash);
        group.MapPost("/", Create);
        group.MapPost("/draft", CreateDraft);
        group.MapPost("/{id:guid}/publish", Publish);
        group.MapDelete("/{id:guid}/draft", DeleteDraft);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Update);
        group.MapPut("/{id:guid}/move", Move);
        group.MapPut("/{id:guid}/layout", SetLayout);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/restore", Restore);
        group.MapDelete("/{id:guid}/purge", Purge);
        group.MapGet("/{id:guid}/versions", ListVersions);
        group.MapGet("/{id:guid}/versions/{number:int}", GetVersion);
        group.MapPost("/{id:guid}/versions/{number:int}/restore", RestoreVersion);

        return routes;
    }

    private static async Task<IResult> Create(
        CreatePageRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        IPermissionService perms, INotificationService notifications, IWebhookDispatcher webhooks)
    {
        // Creating a page needs edit rights on the space (and on the parent, if any).
        if (!await perms.CanViewSpaceAsync(req.SpaceId)) return Results.NotFound();
        if (!await perms.CanEditSpaceAsync(req.SpaceId)) return Results.Forbid();
        if (req.ParentPageId is { } parentForPerms && !await perms.CanEditPageAsync(parentForPerms))
            return Results.Forbid();

        var title = (req.Title ?? "").Trim();
        if (title.Length == 0)
            return Results.ValidationProblem(Error("title", "Title is required."));
        if (!TryNormalizeContent(req.ContentJson, out var content))
            return Results.ValidationProblem(Error("contentJson", "Content must be valid JSON."));

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
            Title = title,
            SearchText = BuildSearchText(title, content),
            Status = PageStatus.Current,
            Position = await NextPositionAsync(db, req.SpaceId, req.ParentPageId),
            CreatedById = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var version = NewVersion(page, versionNumber: 1, content, userId, changeComment: null, now);
        db.Pages.Add(page);
        db.PageVersions.Add(version);

        // Page.CurrentVersionId and PageVersion.PageId reference each other, so
        // insert both first (leaving the pointer null), then set the pointer —
        // otherwise EF cannot order the two inserts.
        await db.SaveChangesAsync();
        page.CurrentVersionId = version.Id;
        await RecordPageCreatedAsync(page, userId, audit, notifications);
        await db.SaveChangesAsync();
        // Dispatched only after the create is durably committed — webhooks are
        // fire-and-forget outbound calls, not part of the unit of work.
        await DispatchPageCreatedWebhookAsync(page, webhooks);

        return Results.Created($"/api/pages/{page.Id}", ToDetail(page, version));
    }

    private static async Task<IResult> CreateDraft(
        CreateDraftRequest req, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        // Same space/parent permission checks as Create — a draft still needs
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
        // A draft is invisible: no audit entry, notification, or webhook — it
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
        if (!TryNormalizeContent(req.ContentJson, out var content))
            return Results.ValidationProblem(Error("contentJson", "Content must be valid JSON."));

        // Nothing was ever "really" saved yet, so the published page starts
        // clean at v1 with the real content — mutate it in place rather than
        // appending a v2 that would leave a confusing empty-v1/real-v2 pair.
        var version = page.CurrentVersion;
        version.ContentJson = content;
        page.Title = title;
        page.SearchText = BuildSearchText(title, content);
        page.Status = PageStatus.Current;
        page.UpdatedAt = DateTimeOffset.UtcNow;

        await RecordPageCreatedAsync(page, userId, audit, notifications);
        await db.SaveChangesAsync();
        await DispatchPageCreatedWebhookAsync(page, webhooks);

        return Results.Ok(ToDetail(page, version));
    }

    private static async Task<IResult> DeleteDraft(
        Guid id, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.Status == PageStatus.Draft && p.DeletedAt == null);
        if (page is null) return Results.NotFound();

        var userId = current.RequireId();
        if (page.CreatedById != userId && !await perms.CanEditSpaceAsync(page.SpaceId))
            return Results.Forbid();

        // Nothing was ever really saved, so this is a hard delete, not a trash
        // — clear the current-version pointer first (the restrict FK would
        // otherwise block it), then remove the page; versions cascade.
        page.CurrentVersionId = null;
        await db.SaveChangesAsync();
        db.Pages.Remove(page);
        await db.SaveChangesAsync();
        return Results.NoContent();
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

        await RecordViewAsync(id, db, current, http);
        return Results.Ok(ToDetail(page, page.CurrentVersion));
    }

    /// <summary>
    /// Records a read for the usage KPIs (dev-plan 0.3), after the permission
    /// check so a refused read is never counted.
    ///
    /// Browser sessions only. An API token is a script — a nightly export would
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
        Guid id, UpdatePageRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        IPermissionService perms, INotificationService notifications, IWebhookDispatcher webhooks)
    {
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

        if (!TryNormalizeContent(req.ContentJson, out var content))
            return Results.ValidationProblem(Error("contentJson", "Content must be valid JSON."));

        var page = await db.Pages.Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page is null) return Results.NotFound();

        if (req.Title is not null)
        {
            var title = req.Title.Trim();
            if (title.Length == 0)
                return Results.ValidationProblem(Error("title", "Title cannot be empty."));
            page.Title = title;
        }

        var now = DateTimeOffset.UtcNow;
        var nextNumber = (page.CurrentVersion?.VersionNumber ?? 0) + 1;
        var version = NewVersion(page, nextNumber, content, current.RequireId(),
            string.IsNullOrWhiteSpace(req.ChangeComment) ? null : req.ChangeComment.Trim(), now);
        db.PageVersions.Add(version);
        page.CurrentVersionId = version.Id;
        page.SearchText = BuildSearchText(page.Title, content);
        page.UpdatedAt = now;
        var userId = current.RequireId();
        audit.Record("page.updated", "page", page.Id, new { page.Title, Version = nextNumber });
        await notifications.NotifyPageWatchersAsync(
            page.Id, page.SpaceId, "page.updated", userId, new { page.Title });

        await db.SaveChangesAsync();
        await webhooks.DispatchAsync(page.SpaceId, "page.updated", "page", page.Id, new { page.Title });
        return Results.Ok(ToDetail(page, version));
    }

    private static async Task<IResult> Move(
        Guid id, MovePageRequest req, AppDbContext db, IPermissionService perms)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();
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
                return Results.ValidationProblem(Error("parentPageId", "Parent page is in a different space."));
            if (await WouldCreateCycleAsync(db, movingPageId: id, newParentId))
                return Results.ValidationProblem(Error("parentPageId", "Cannot move a page beneath one of its own descendants."));
        }

        // Index is a slot among the destination's current siblings (0 = first),
        // not a raw Position value — the caller shouldn't have to know or
        // guess at other pages' Position ints. We insert the moving page at
        // that slot and renumber the whole sibling group, so a drag-and-drop
        // UI can just say "this landed at index 2" and never risk colliding
        // with — or leaving a gap relative to — its new neighbors.
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
    /// display metadata, like Move — no new version, audit entry, or webhook.
    /// Unlike Move, this must also reach an unpublished draft: the editor shows
    /// the full-width toggle while composing a brand-new page, and the draft is
    /// the only id that exists until Publish. Hence IgnoreQueryFilters() with
    /// the soft-delete half of the filter reapplied by hand — a trashed page
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

    private static async Task<IResult> Delete(
        Guid id, AppDbContext db, CurrentUser current, IAuditLogger audit, IPermissionService perms)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

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
        await db.SaveChangesAsync();
        return Results.NoContent();
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
        Guid id, AppDbContext db, IAuditLogger audit, IPermissionService perms)
    {
        var page = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);
        if (page is null) return Results.NotFound();
        // Permanent deletion is an admin-level act on the space.
        if (!await perms.CanAdminSpaceAsync(page.SpaceId)) return Results.Forbid();

        var subtree = await CollectTrashedSubtreeAsync(db, page.SpaceId, id);
        // Clear current-version pointers so the cascade to versions is not blocked
        // by the restrict FK, then hard-delete the subtree (versions, attachments,
        // and comments cascade).
        foreach (var p in subtree) p.CurrentVersionId = null;
        await db.SaveChangesAsync();
        db.Pages.RemoveRange(subtree);
        // Recorded before SaveChanges so the entry commits with the deletion.
        audit.Record("page.purged", "page", page.Id, new { page.Title, SubtreeCount = subtree.Count });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Trash(Guid spaceId, AppDbContext db, IPermissionService perms)
    {
        if (!await db.Spaces.AnyAsync(s => s.Id == spaceId)) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(spaceId)) return Results.NotFound();

        var trashed = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.SpaceId == spaceId && p.DeletedAt != null)
            .ToListAsync();
        var trashedIds = trashed.Select(p => p.Id).ToHashSet();

        // List only "trash roots" — the pages actually deleted (whose parent is
        // not itself trashed); each stands for one restorable subtree.
        var roots = trashed
            .Where(p => p.ParentPageId is null || !trashedIds.Contains(p.ParentPageId.Value))
            .OrderByDescending(p => p.DeletedAt)
            .Select(p => new TrashedPageResponse(p.Id, p.Title, p.DeletedAt!.Value, p.DeletedById))
            .ToList();
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
        Guid id, int number, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var page = await db.Pages.Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(id)) return Results.Forbid();

        var source = await db.PageVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PageId == id && v.VersionNumber == number);
        if (source is null) return Results.NotFound();

        // Rollback preserves history: it appends a new version copying the old
        // content rather than deleting anything.
        var now = DateTimeOffset.UtcNow;
        var nextNumber = (page.CurrentVersion?.VersionNumber ?? 0) + 1;
        var version = NewVersion(page, nextNumber, source.ContentJson, current.RequireId(),
            $"Restored from version {number}", now);
        db.PageVersions.Add(version);
        page.CurrentVersionId = version.Id;
        page.SearchText = BuildSearchText(page.Title, source.ContentJson);
        page.UpdatedAt = now;

        await db.SaveChangesAsync();
        return Results.Ok(ToDetail(page, version));
    }

    private static async Task<IResult> Tree(Guid spaceId, AppDbContext db, IPermissionService perms)
    {
        if (!await db.Spaces.AnyAsync(s => s.Id == spaceId)) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(spaceId)) return Results.NotFound();

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == spaceId)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.Position, p.ParentPageId })
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
                .Select(p => new PageTreeNode(p.Id, p.Title, p.Position, Build(p.Id)))
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
        // being moved. Guarded by a hop limit as defence against a corrupt tree.
        Guid? cursor = newParentId;
        for (var hops = 0; cursor is { } id && hops < 10_000; hops++)
        {
            if (id == movingPageId) return true;
            cursor = await db.Pages.Where(p => p.Id == id)
                .Select(p => p.ParentPageId).FirstOrDefaultAsync();
        }
        return false;
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

    /// <summary>Search text for a page: its title plus the plain text of its content.</summary>
    private static string BuildSearchText(string title, string contentJson) =>
        NormalizeForSearch($"{title} {ExtractPlainText(contentJson)}".Trim());

    /// <summary>
    /// Postgres's tsvector parser treats "word/word" (e.g. "Hocuspocus/Yjs",
    /// "OIDC/SSO") as a single compound lexeme instead of splitting it, which
    /// makes each half unsearchable on its own. Replacing slashes with spaces
    /// before indexing lets to_tsvector tokenize both halves normally.
    /// </summary>
    private static string NormalizeForSearch(string text) => text.Replace('/', ' ');

    /// <summary>Concatenates the text nodes of a ProseMirror document, ignoring structure.</summary>
    private static string ExtractPlainText(string contentJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            var sb = new System.Text.StringBuilder();
            Walk(doc.RootElement, sb);
            return sb.ToString().Trim();
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        static void Walk(JsonElement el, System.Text.StringBuilder sb)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    if (el.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(text.GetString());
                        sb.Append(' ');
                    }
                    if (el.TryGetProperty("content", out var content))
                        Walk(content, sb);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                        Walk(item, sb);
                    break;
            }
        }
    }

    private static bool TryNormalizeContent(string? input, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            normalized = EmptyDoc;
            return true;
        }
        try
        {
            using var _ = JsonDocument.Parse(input);
            normalized = input;
            return true;
        }
        catch (JsonException)
        {
            normalized = EmptyDoc;
            return false;
        }
    }

    private static PageDetailResponse ToDetail(Page page, PageVersion version) => new(
        page.Id, page.SpaceId, page.ParentPageId, page.Title, page.Position, page.Status,
        version.VersionNumber, version.ContentJson, page.FullWidth, page.CreatedAt, page.UpdatedAt);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
