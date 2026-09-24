using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Mentions;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Pages;

public enum PageWriteStatus { Ok, NotFound, Forbidden, Invalid, Conflict }

/// <summary>The outcome of a write, in terms both callers can translate: REST into a status code, MCP into a message.</summary>
public sealed record PageWriteResult(
    PageWriteStatus Status, Page? Page = null, PageVersion? Version = null,
    string? Field = null, string? Message = null)
{
    public static PageWriteResult NotFound() => new(PageWriteStatus.NotFound);
    public static PageWriteResult Forbidden(string message) => new(PageWriteStatus.Forbidden, Message: message);
    public static PageWriteResult Invalid(string field, string message) => new(PageWriteStatus.Invalid, Field: field, Message: message);
    public static PageWriteResult Ok(Page page, PageVersion version) => new(PageWriteStatus.Ok, page, version);

    /// <summary>
    /// The page has moved on since the caller last saw it (dev-plan 8.6).
    /// Carries the current page and version so the caller can show the
    /// difference rather than just being told no.
    /// </summary>
    public static PageWriteResult Conflict(Page page, PageVersion current) =>
        new(PageWriteStatus.Conflict, page, current,
            Message: "This page has changed since you started editing.");
}

/// <summary>
/// Creating and updating a page: permission checks, validation, versioning,
/// search text, audit, watcher and mention notifications, webhooks.
///
/// Extracted from <see cref="PageEndpoints"/> for dev-plan 8.4 so the MCP
/// write tools run *this* code rather than a second implementation that
/// would drift: a page created by an assistant must be indistinguishable
/// from one created in the browser, side effects included. 8.5's pack
/// importer will need the same guarantee.
/// </summary>
public interface IPageWriter
{
    Task<PageWriteResult> CreateAsync(Guid spaceId, Guid? parentPageId, string? title, string? contentJson, CancellationToken ct = default);
    /// <param name="baseVersion">
    /// The page version the caller believes it is editing (dev-plan 8.6).
    /// When given and the page has moved past it, the write is refused as a
    /// <see cref="PageWriteStatus.Conflict"/> instead of overwriting. Null
    /// keeps the last-write-wins behavior API and MCP callers have always
    /// had: they do not hold a draft that could be stale.
    /// </param>
    Task<PageWriteResult> UpdateAsync(
        Guid pageId, string? title, string? contentJson, string? changeComment,
        CancellationToken ct = default, int? baseVersion = null);
}

public sealed class PageWriter(
    AppDbContext db, CurrentUser current, IAuditLogger audit, IPermissionService perms,
    INotificationService notifications, IWebhookDispatcher webhooks,
    ICollabNotifier collab, IHttpContextAccessor accessor) : IPageWriter
{
    public async Task<PageWriteResult> CreateAsync(
        Guid spaceId, Guid? parentPageId, string? title, string? contentJson, CancellationToken ct = default)
    {
        // Creating a page needs edit rights on the space (and on the parent, if any).
        if (!await perms.CanViewSpaceAsync(spaceId)) return PageWriteResult.NotFound();
        if (!await perms.CanEditSpaceAsync(spaceId)) return PageWriteResult.Forbidden("You do not have edit rights on that space.");
        if (parentPageId is { } parentForPerms && !await perms.CanEditPageAsync(parentForPerms))
            return PageWriteResult.Forbidden("You do not have edit rights on that parent page.");

        var trimmed = (title ?? "").Trim();
        if (trimmed.Length == 0) return PageWriteResult.Invalid("title", "Title is required.");
        if (!PageContent.TryNormalize(contentJson, out var content))
            return PageWriteResult.Invalid("contentJson", "Content must be valid JSON.");

        if (!await db.Spaces.AnyAsync(s => s.Id == spaceId, ct))
            return PageWriteResult.Invalid("spaceId", "Space not found.");

        if (parentPageId is { } parentId)
        {
            var parent = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parentId, ct);
            if (parent is null) return PageWriteResult.Invalid("parentPageId", "Parent page not found.");
            if (parent.SpaceId != spaceId) return PageWriteResult.Invalid("parentPageId", "Parent page is in a different space.");
        }

        var now = DateTimeOffset.UtcNow;
        var userId = current.RequireId();
        var page = new Page
        {
            Id = Guid.NewGuid(),
            SpaceId = spaceId,
            ParentPageId = parentPageId,
            Title = trimmed,
            SearchText = PageContent.BuildSearchText(trimmed, content),
            Status = PageStatus.Current,
            Position = await NextPositionAsync(spaceId, parentPageId, ct),
            CreatedById = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var version = NewVersion(page, versionNumber: 1, content, userId, changeComment: null, now);
        db.Pages.Add(page);
        db.PageVersions.Add(version);

        // Page.CurrentVersionId and PageVersion.PageId reference each other, so
        // insert both first (leaving the pointer null), then set the pointer:
        // otherwise EF cannot order the two inserts.
        //
        // Both saves are one transaction. They used not to be, and a failure
        // in the second left a page that was committed, had a version, and had
        // no CurrentVersionId: invisible to every read, and permanently
        // unupdatable because the next version number collided with the one
        // already there. A half-created page is worse than no page.
        var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await db.SaveChangesAsync(ct);
            page.CurrentVersionId = version.Id;
            audit.Record("page.created", "page", page.Id, new { page.Title, page.SpaceId });
            // Space watchers hear about new pages; the page itself has no
            // watchers yet since nobody could watch it before it existed.
            await notifications.NotifyOfNewPageAsync(page.Id, page.SpaceId, userId, new { page.Title });
            await NotifyNewMentionsAsync(page, before: null, content, userId);
            await db.SaveChangesAsync(ct);
            if (owned is not null) await owned.CommitAsync(ct);
        }
        finally
        {
            if (owned is not null) await owned.DisposeAsync();
        }
        // Dispatched only after the create is durably committed: webhooks are
        // fire-and-forget outbound calls, not part of the unit of work.
        await webhooks.DispatchAsync(page.SpaceId, "page.created", "page", page.Id, new { page.Title });
        await NotifyCollabAsync(page.Id, content, version.VersionNumber, ct);

        return PageWriteResult.Ok(page, version);
    }

    public async Task<PageWriteResult> UpdateAsync(
        Guid pageId, string? title, string? contentJson, string? changeComment,
        CancellationToken ct = default, int? baseVersion = null)
    {
        if (!await perms.CanViewPageAsync(pageId)) return PageWriteResult.NotFound();
        if (!await perms.CanEditPageAsync(pageId)) return PageWriteResult.Forbidden("You do not have edit rights on that page.");

        var page = await db.Pages.Include(p => p.CurrentVersion).FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return PageWriteResult.NotFound();

        // No content means "leave the content alone": a rename. It used to be
        // read as an empty document, so renaming a page through the API or
        // MCP (update_page with only a title) wiped it (found 2026-09-23).
        string content;
        if (contentJson is null)
            content = page.CurrentVersion?.ContentJson ?? PageContent.EmptyDoc;
        else if (!PageContent.TryNormalize(contentJson, out content))
            return PageWriteResult.Invalid("contentJson", "Content must be valid JSON.");

        if (title is not null)
        {
            var trimmed = title.Trim();
            if (trimmed.Length == 0) return PageWriteResult.Invalid("title", "Title cannot be empty.");
            page.Title = trimmed;
        }

        // Captured before the new version is attached: setting
        // page.CurrentVersionId lets EF's navigation fix-up repoint
        // page.CurrentVersion at the *new* version, which would make the
        // mention diff below compare the content against itself.
        var previousContent = page.CurrentVersion?.ContentJson;

        var now = DateTimeOffset.UtcNow;
        // From the versions themselves, not from the current-version pointer.
        // A page whose pointer is missing still has versions, and numbering
        // from zero would collide with them on the unique index and fail every
        // future edit. Reading the maximum makes such a page repair itself on
        // its next save.
        var highest = await db.PageVersions
            .Where(v => v.PageId == page.Id)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;
        var currentNumber = Math.Max(page.CurrentVersion?.VersionNumber ?? 0, highest);

        // Optimistic concurrency (dev-plan 8.6). The editor sends the version
        // its draft was last reconciled to; if the page has moved past that,
        // something wrote to it and this draft has not seen it, so publishing
        // would overwrite. Refusing and handing back what the page says now
        // lets the editor show the difference instead.
        //
        // Only when the caller asks for it. An API or MCP caller holds no
        // draft that could be stale, so last-write-wins remains right for
        // them, and requiring a version would break every existing script.
        if (baseVersion is { } expected && expected != currentNumber && page.CurrentVersion is { } latest)
            return PageWriteResult.Conflict(page, latest);

        var nextNumber = currentNumber + 1;
        var userId = current.RequireId();
        var version = NewVersion(page, nextNumber, content, userId,
            string.IsNullOrWhiteSpace(changeComment) ? null : changeComment.Trim(), now);
        db.PageVersions.Add(version);
        page.CurrentVersionId = version.Id;
        page.SearchText = PageContent.BuildSearchText(page.Title, content);
        page.UpdatedAt = now;
        audit.Record("page.updated", "page", page.Id, new { page.Title, Version = nextNumber });
        await notifications.NotifyPageWatchersAsync(page.Id, page.SpaceId, "page.updated", userId, new { page.Title });
        await NotifyNewMentionsAsync(page, previousContent, content, userId);

        await db.SaveChangesAsync(ct);
        await webhooks.DispatchAsync(page.SpaceId, "page.updated", "page", page.Id, new { page.Title });
        await NotifyCollabAsync(page.Id, content, nextNumber, ct);
        return PageWriteResult.Ok(page, version);
    }

    /// <summary>
    /// Tells the collaboration sidecar that this page has moved on (dev-plan
    /// 8.6), so anyone with it open sees the change as tracked edits instead
    /// of writing over it with their draft.
    ///
    /// After the commit, and after webhooks, for the same reason those are:
    /// it is an outbound call about something that has already happened. It
    /// cannot fail the write, and a sidecar that is down simply means the
    /// reconciliation waits for the document's next load.
    /// </summary>
    private Task NotifyCollabAsync(Guid pageId, string content, int version, CancellationToken ct) =>
        collab.NotifyAsync(pageId, content, WriteSources.Of(accessor), version, ct);

    /// <summary>
    /// Tells anyone newly mentioned that they were, checked with
    /// <see cref="IPermissionService.AsUser"/> first: the notification carries
    /// the page title, and mentioning someone must not leak the title of a
    /// page they cannot open.
    /// </summary>
    private async Task NotifyNewMentionsAsync(Page page, string? before, string after, Guid authorId)
    {
        var mentioned = Mentions.NewlyMentioned(before, after, authorId).ToList();
        if (mentioned.Count == 0) return;

        // Only ids that are really accounts. A mention carries whatever id the
        // document says, and a document can arrive from the API, from MCP or
        // from an import; an id with no user behind it used to reach the
        // notification insert and fail the whole write on a foreign key, which
        // turned bad content into a 500 on save.
        var real = await db.Users.AsNoTracking()
            .Where(u => mentioned.Contains(u.Id) && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var userId in real)
        {
            if (!await perms.AsUser(userId).CanViewPageAsync(page.Id)) continue;
            await notifications.NotifyUserAsync(userId, "user.mentioned", "page", page.Id, authorId, new { page.Title });
        }
    }

    private async Task<int> NextPositionAsync(Guid spaceId, Guid? parentId, CancellationToken ct)
    {
        var max = await db.Pages
            .Where(p => p.SpaceId == spaceId && p.ParentPageId == parentId)
            .Select(p => (int?)p.Position)
            .MaxAsync(ct);
        return (max ?? -1) + 1;
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
}
