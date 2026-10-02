using Microsoft.EntityFrameworkCore;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;

namespace Tesria.Api.Features.Comments;

public enum CommentWriteStatus { Ok, NotFound, Invalid }

/// <summary>The outcome of adding a comment, in terms both callers can translate: REST into a status code, MCP into a message.</summary>
public sealed record CommentWriteResult(
    CommentWriteStatus Status, Comment? Comment = null, string? Field = null, string? Message = null)
{
    public static CommentWriteResult NotFound() => new(CommentWriteStatus.NotFound);
    public static CommentWriteResult Invalid(string field, string message) => new(CommentWriteStatus.Invalid, Field: field, Message: message);
}

/// <summary>
/// The one place a comment is added (dev-plan 22.1), for REST and MCP alike,
/// as <see cref="Pages.IPageWriter"/> is for pages: the checks, the watcher
/// and mention notifications and the webhook all come from here, so a
/// comment an assistant writes is indistinguishable from one written in the
/// browser.
/// </summary>
public interface ICommentWriter
{
    /// <summary>
    /// A comment on a page, or a reply when <paramref name="parentCommentId"/>
    /// is given. The saved comment comes back with its author joined.
    /// </summary>
    Task<CommentWriteResult> CreateAsync(
        Guid pageId, string? body, Guid? parentCommentId, string? anchorJson, CancellationToken ct = default);
}

public sealed class CommentWriter(
    AppDbContext db, CurrentUser current, IPermissionService perms,
    INotificationService notifications, IWebhookDispatcher webhooks) : ICommentWriter
{
    public async Task<CommentWriteResult> CreateAsync(
        Guid pageId, string? body, Guid? parentCommentId, string? anchorJson, CancellationToken ct = default)
    {
        // Commenting requires being able to see the page.
        if (!await perms.CanReadPageAsync(pageId)) return CommentWriteResult.NotFound();

        var text = (body ?? "").Trim();
        if (text.Length == 0)
            return CommentWriteResult.Invalid("body", "Comment body is required.");
        if (anchorJson is not null && !CommentEndpoints.IsValidJson(anchorJson))
            return CommentWriteResult.Invalid("anchorJson", "Anchor must be valid JSON.");

        // perms.CanReadPageAsync (above) already resolved the page including
        // drafts (its author and editors only); re-resolve it the same way so a
        // not-yet-published draft can still receive comments.
        var target = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.Id == pageId).Select(p => new { p.SpaceId, p.Status }).FirstOrDefaultAsync(ct);
        if (target is null) return CommentWriteResult.NotFound();
        // A draft does not exist for anyone else yet: its comments tell no
        // watcher and fire no webhook (dev-plan 14.1).
        var isDraft = target.Status == PageStatus.Draft;

        if (parentCommentId is { } parentId)
        {
            var parent = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId, ct);
            if (parent is null || parent.PageId != pageId)
                return CommentWriteResult.Invalid("parentCommentId", "Parent comment not found on this page.");
        }

        var now = DateTimeOffset.UtcNow;
        var authorId = current.RequireId();
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            ParentCommentId = parentCommentId,
            Body = text,
            AnchorJson = anchorJson,
            AuthorId = authorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Comments.Add(comment);
        if (!isDraft)
        {
            await notifications.NotifyPageWatchersAsync(
                pageId, target.SpaceId, "comment.created", authorId,
                new { Body = CommentEndpoints.Truncate(CommentEndpoints.Readable(text)) });
            await CommentEndpoints.NotifyMentionsAsync(pageId, text, previous: null, authorId, db, perms, notifications);
        }
        await db.SaveChangesAsync(ct);
        if (!isDraft)
            await webhooks.DispatchAsync(
                target.SpaceId, "comment.created", "page", pageId, new { Body = CommentEndpoints.Truncate(text) });
        // Re-read with the author joined rather than assigning the navigation:
        // attaching a detached User makes EF try to INSERT it, which trips the
        // unique-email constraint. One extra read on create is the cheap,
        // obviously-correct option.
        var saved = await db.Comments.AsNoTracking()
            .Include(c => c.Author)
            .FirstAsync(c => c.Id == comment.Id, ct);
        return new CommentWriteResult(CommentWriteStatus.Ok, saved);
    }
}
