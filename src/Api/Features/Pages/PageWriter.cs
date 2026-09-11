using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Mentions;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Pages;

public enum PageWriteStatus { Ok, NotFound, Forbidden, Invalid }

/// <summary>The outcome of a write, in terms both callers can translate: REST into a status code, MCP into a message.</summary>
public sealed record PageWriteResult(
    PageWriteStatus Status, Page? Page = null, PageVersion? Version = null,
    string? Field = null, string? Message = null)
{
    public static PageWriteResult NotFound() => new(PageWriteStatus.NotFound);
    public static PageWriteResult Forbidden(string message) => new(PageWriteStatus.Forbidden, Message: message);
    public static PageWriteResult Invalid(string field, string message) => new(PageWriteStatus.Invalid, Field: field, Message: message);
    public static PageWriteResult Ok(Page page, PageVersion version) => new(PageWriteStatus.Ok, page, version);
}

/// <summary>
/// Creating and updating a page: permission checks, validation, versioning,
/// search text, audit, watcher and mention notifications, webhooks.
///
/// Extracted from <see cref="PageEndpoints"/> for dev-plan 8.4 so the MCP
/// write tools run *this* code rather than a second implementation that
/// would drift — a page created by an assistant must be indistinguishable
/// from one created in the browser, side effects included. 8.5's pack
/// importer will need the same guarantee.
/// </summary>
public interface IPageWriter
{
    Task<PageWriteResult> CreateAsync(Guid spaceId, Guid? parentPageId, string? title, string? contentJson, CancellationToken ct = default);
    Task<PageWriteResult> UpdateAsync(Guid pageId, string? title, string? contentJson, string? changeComment, CancellationToken ct = default);
}

public sealed class PageWriter(
    AppDbContext db, CurrentUser current, IAuditLogger audit, IPermissionService perms,
    INotificationService notifications, IWebhookDispatcher webhooks) : IPageWriter
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
        // insert both first (leaving the pointer null), then set the pointer —
        // otherwise EF cannot order the two inserts.
        await db.SaveChangesAsync(ct);
        page.CurrentVersionId = version.Id;
        audit.Record("page.created", "page", page.Id, new { page.Title, page.SpaceId });
        // Space watchers hear about new pages; the page itself has no watchers
        // yet since nobody could watch it before it existed.
        await notifications.NotifyOfNewPageAsync(page.Id, page.SpaceId, userId, new { page.Title });
        await NotifyNewMentionsAsync(page, before: null, content, userId);
        await db.SaveChangesAsync(ct);
        // Dispatched only after the create is durably committed — webhooks are
        // fire-and-forget outbound calls, not part of the unit of work.
        await webhooks.DispatchAsync(page.SpaceId, "page.created", "page", page.Id, new { page.Title });

        return PageWriteResult.Ok(page, version);
    }

    public async Task<PageWriteResult> UpdateAsync(
        Guid pageId, string? title, string? contentJson, string? changeComment, CancellationToken ct = default)
    {
        if (!await perms.CanViewPageAsync(pageId)) return PageWriteResult.NotFound();
        if (!await perms.CanEditPageAsync(pageId)) return PageWriteResult.Forbidden("You do not have edit rights on that page.");

        if (!PageContent.TryNormalize(contentJson, out var content))
            return PageWriteResult.Invalid("contentJson", "Content must be valid JSON.");

        var page = await db.Pages.Include(p => p.CurrentVersion).FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return PageWriteResult.NotFound();

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
        var nextNumber = (page.CurrentVersion?.VersionNumber ?? 0) + 1;
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
        return PageWriteResult.Ok(page, version);
    }

    /// <summary>
    /// Tells anyone newly mentioned that they were, checked with
    /// <see cref="IPermissionService.AsUser"/> first: the notification carries
    /// the page title, and mentioning someone must not leak the title of a
    /// page they cannot open.
    /// </summary>
    private async Task NotifyNewMentionsAsync(Page page, string? before, string after, Guid authorId)
    {
        foreach (var userId in Mentions.NewlyMentioned(before, after, authorId))
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
