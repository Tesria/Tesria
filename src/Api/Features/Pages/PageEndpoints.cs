using System.Text.Json;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Audit;
using ConfluenceClone.Api.Infrastructure.Auth;
using ConfluenceClone.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Pages;

public static class PageEndpoints
{
    // An empty ProseMirror document; used when a page is created without content.
    private const string EmptyDoc = """{"type":"doc","content":[]}""";

    public record CreatePageRequest(Guid SpaceId, Guid? ParentPageId, string Title, string? ContentJson);
    public record UpdatePageRequest(string? Title, string ContentJson, string? ChangeComment);
    public record MovePageRequest(Guid? ParentPageId, int Position);

    public record PageDetailResponse(
        Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, int Position, PageStatus Status,
        int CurrentVersionNumber, string ContentJson, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    public record PageVersionResponse(
        Guid Id, int VersionNumber, string? ChangeComment, Guid AuthorId, DateTimeOffset CreatedAt);
    public record PageVersionContentResponse(
        Guid Id, int VersionNumber, string ContentJson, string? ChangeComment,
        Guid AuthorId, DateTimeOffset CreatedAt);
    public record PageTreeNode(Guid Id, string Title, int Position, List<PageTreeNode> Children);
    public record TrashedPageResponse(Guid Id, string Title, DateTimeOffset DeletedAt, Guid? DeletedById);

    public static IEndpointRouteBuilder MapPageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/pages").WithTags("Pages").RequireAuthorization();

        group.MapGet("/tree", Tree);
        group.MapGet("/trash", Trash);
        group.MapPost("/", Create);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Update);
        group.MapPut("/{id:guid}/move", Move);
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
        IPermissionService perms)
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
        audit.Record("page.created", "page", page.Id, new { page.Title, page.SpaceId });
        await db.SaveChangesAsync();

        return Results.Created($"/api/pages/{page.Id}", ToDetail(page, version));
    }

    private static async Task<IResult> Get(Guid id, AppDbContext db, IPermissionService perms)
    {
        var page = await db.Pages.AsNoTracking()
            .Include(p => p.CurrentVersion)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (page?.CurrentVersion is null) return Results.NotFound();
        // 404 rather than 403 so restricted pages aren't discoverable.
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        return Results.Ok(ToDetail(page, page.CurrentVersion));
    }

    private static async Task<IResult> Update(
        Guid id, UpdatePageRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        IPermissionService perms)
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
        audit.Record("page.updated", "page", page.Id, new { page.Title, Version = nextNumber });

        await db.SaveChangesAsync();
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

        page.ParentPageId = req.ParentPageId;
        page.Position = req.Position;
        page.UpdatedAt = DateTimeOffset.UtcNow;
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
        if (!await db.Pages.AnyAsync(p => p.Id == id)) return Results.NotFound();
        if (!await perms.CanViewPageAsync(id)) return Results.NotFound();
        var versions = await db.PageVersions.AsNoTracking()
            .Where(v => v.PageId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new PageVersionResponse(v.Id, v.VersionNumber, v.ChangeComment, v.AuthorId, v.CreatedAt))
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
            .FirstOrDefaultAsync(v => v.PageId == id && v.VersionNumber == number);
        return v is null
            ? Results.NotFound()
            : Results.Ok(new PageVersionContentResponse(
                v.Id, v.VersionNumber, v.ContentJson, v.ChangeComment, v.AuthorId, v.CreatedAt));
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
        $"{title} {ExtractPlainText(contentJson)}".Trim();

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
        version.VersionNumber, version.ContentJson, page.CreatedAt, page.UpdatedAt);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
