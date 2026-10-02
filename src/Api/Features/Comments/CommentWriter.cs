using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;

namespace Tesria.Api.Features.Comments;

public enum CommentWriteStatus { Ok, NotFound, Invalid, Forbidden, Unprocessable, Unavailable }

/// <summary>The outcome of adding a comment, in terms both callers can translate: REST into a status code, MCP into a message.</summary>
public sealed record CommentWriteResult(
    CommentWriteStatus Status, Comment? Comment = null, string? Field = null, string? Message = null,
    string? Code = null, int? Count = null)
{
    public static CommentWriteResult NotFound() => new(CommentWriteStatus.NotFound);
    public static CommentWriteResult Invalid(string field, string message) => new(CommentWriteStatus.Invalid, Field: field, Message: message);
    public static CommentWriteResult Forbidden(string message) => new(CommentWriteStatus.Forbidden, Message: message);
    /// <summary>A quote that cannot be placed once (dev-plan 22.2): a code a script can check, and how often it appears.</summary>
    public static CommentWriteResult Unprocessable(string code, string message, int? count = null) =>
        new(CommentWriteStatus.Unprocessable, Message: message, Code: code, Count: count);
    public static CommentWriteResult Unavailable(string message) =>
        new(CommentWriteStatus.Unavailable, Message: message, Code: "live_editing_unavailable");
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
    /// is given. With <paramref name="quote"/>, an inline comment on that
    /// passage (dev-plan 22.2): its highlight is placed in the page's shared
    /// draft first, and the comment saved only once it is. The saved comment
    /// comes back with its author joined.
    /// </summary>
    Task<CommentWriteResult> CreateAsync(
        Guid pageId, string? body, Guid? parentCommentId, string? anchorJson, CancellationToken ct = default,
        string? quote = null, int? occurrence = null);
}

public sealed partial class CommentWriter(
    AppDbContext db, CurrentUser current, IPermissionService perms,
    INotificationService notifications, IWebhookDispatcher webhooks, ICollabNotifier collab) : ICommentWriter
{
    /// <summary>The longest quote anyone may ask for, after whitespace is collapsed (commentQuote.ts says the same).</summary>
    public const int MaxQuoteLength = 1000;

    public async Task<CommentWriteResult> CreateAsync(
        Guid pageId, string? body, Guid? parentCommentId, string? anchorJson, CancellationToken ct = default,
        string? quote = null, int? occurrence = null)
    {
        // Commenting requires being able to see the page.
        if (!await perms.CanReadPageAsync(pageId)) return CommentWriteResult.NotFound();

        var text = (body ?? "").Trim();
        if (text.Length == 0)
            return CommentWriteResult.Invalid("body", "Comment body is required.");
        if (anchorJson is not null && !CommentEndpoints.IsValidJson(anchorJson))
            return CommentWriteResult.Invalid("anchorJson", "Anchor must be valid JSON.");

        // An inline comment on a quoted passage (dev-plan 22.2). Everything is
        // checked before the live-editing service is asked: rights in the
        // order the review set (read, then edit; a read-only token never got
        // this far), then the quote itself.
        string? passage = null;
        if (occurrence is not null && quote is null)
            return CommentWriteResult.Invalid("occurrence", "Occurrence goes with a quote: say which passage first.");
        if (quote is not null)
        {
            if (parentCommentId is not null)
                return CommentWriteResult.Invalid("quote", "A reply belongs to its thread and cannot quote a passage; quote it in a new comment.");
            if (anchorJson is not null)
                return CommentWriteResult.Invalid("quote", "Give a quote or an anchor, not both.");
            // The highlight goes into the draft, which needs the editor's rights.
            if (!await perms.CanEditPageAsync(pageId))
                return CommentWriteResult.Forbidden(
                    "Commenting on a passage needs edit rights on the page, because the highlight goes into its draft. "
                    + "A comment on the whole page needs only read access.");
            passage = NormalizeQuote(quote);
            if (passage.Length == 0)
                return CommentWriteResult.Invalid("quote", "The quote is empty: quote the words the comment is about, or leave quote out for a comment on the whole page.");
            if (passage.Length > MaxQuoteLength)
                return CommentWriteResult.Unprocessable("quote_too_long",
                    $"The quote is {passage.Length} characters long; quote at most {MaxQuoteLength}, a sentence or two is plenty.");
            if (occurrence is < 1)
                return CommentWriteResult.Invalid("occurrence", "Occurrence counts from 1: 1 is the first time the passage appears on the page.");
        }

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

        // The id is minted here so the highlight can carry it before the row
        // exists; the row is saved only once the highlight is placed.
        var commentId = Guid.NewGuid();
        if (passage is not null)
        {
            var placed = await collab.PlaceCommentAsync(pageId, commentId, passage, occurrence, ct);
            switch (placed.Status)
            {
                case InlineCommentPlacement.Placed:
                    anchorJson = JsonSerializer.Serialize(new { type = "text", quote = passage, occurrence = placed.Occurrence ?? occurrence ?? 1 });
                    break;
                case InlineCommentPlacement.Refused:
                    return CommentWriteResult.Unprocessable(placed.Code ?? "quote_not_found",
                        placed.Message ?? "That passage could not be found on the page.", placed.Count);
                case InlineCommentPlacement.PageNotFound:
                    return CommentWriteResult.NotFound();
                case InlineCommentPlacement.Unavailable:
                    // It may have been placed and the answer lost on the way back.
                    await collab.RemoveCommentAsync(pageId, commentId, CancellationToken.None);
                    return CommentWriteResult.Unavailable(
                        "Live editing is not reachable right now, so the passage could not be highlighted and no comment was added. Try again in a moment.");
                default:
                    return CommentWriteResult.Unavailable(
                        "Live editing is not set up on this server, so a comment cannot be placed on a passage. Comment on the whole page instead.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var authorId = current.RequireId();
        var comment = new Comment
        {
            Id = commentId,
            PageId = pageId,
            ParentCommentId = parentCommentId,
            Body = text,
            AnchorJson = anchorJson,
            AuthorId = authorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Comments.Add(comment);
        // An inline comment says so, with its passage, to watchers and
        // webhooks (dev-plan 22.2); a page comment's payload is as it was.
        var anchoredQuote = QuoteOf(anchorJson);
        object Payload(string bodyText) => anchorJson is null
            ? new { Body = bodyText }
            : new { Body = bodyText, Quote = anchoredQuote, Inline = true };
        if (!isDraft)
        {
            await notifications.NotifyPageWatchersAsync(
                pageId, target.SpaceId, "comment.created", authorId,
                Payload(CommentEndpoints.Truncate(CommentEndpoints.Readable(text))));
            await CommentEndpoints.NotifyMentionsAsync(pageId, text, previous: null, authorId, db, perms, notifications);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch when (passage is not null)
        {
            // A highlight with no comment behind it: take it off again, then fail.
            await collab.RemoveCommentAsync(pageId, commentId, CancellationToken.None);
            throw;
        }
        if (!isDraft)
            await webhooks.DispatchAsync(
                target.SpaceId, "comment.created", "page", pageId, Payload(CommentEndpoints.Truncate(text)));
        // Re-read with the author joined rather than assigning the navigation:
        // attaching a detached User makes EF try to INSERT it, which trips the
        // unique-email constraint. One extra read on create is the cheap,
        // obviously-correct option.
        var saved = await db.Comments.AsNoTracking()
            .Include(c => c.Author)
            .FirstAsync(c => c.Id == comment.Id, ct);
        return new CommentWriteResult(CommentWriteStatus.Ok, saved);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>A quote as it is compared: NFC, each whitespace run one space, trimmed (commentQuote.ts normalizeQuote).</summary>
    public static string NormalizeQuote(string quote) =>
        Whitespace().Replace(quote.Normalize(NormalizationForm.FormC), " ").Trim();

    /// <summary>The passage an anchor records (<c>{"type":"text","quote":…}</c>), or null.</summary>
    public static string? QuoteOf(string? anchorJson)
    {
        if (string.IsNullOrEmpty(anchorJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(anchorJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.String
                && q.GetString() is { Length: > 0 } s ? s : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
