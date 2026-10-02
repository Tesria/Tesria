using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Tesria.Api.Features.Comments;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// The comment tools (dev-plan 22.1): an assistant can answer on a page, or
/// in a thread, without touching its text. Reading goes through the REST
/// list's loader and writing through <see cref="ICommentWriter"/>, the path
/// a comment from the browser takes, so the checks, notifications and webhook
/// are the same ones.
/// </summary>
public sealed partial class TesriaTools
{
    /// <param name="Name">The display name, as a mention token carries it.</param>
    public sealed record CommentMention(Guid Id, string Name);

    /// <param name="Body">Plain text, each mention as @Name. Null for a deleted comment.</param>
    /// <param name="Quote">For an inline comment, the passage it is about, as the published page has it.</param>
    public sealed record PageComment(
        Guid Id, Guid AuthorId, string Author, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        bool Resolved, DateTimeOffset? ResolvedAt, string? ResolvedBy, bool IsInline, string? Quote, bool IsDeleted, string? Body,
        IReadOnlyList<CommentMention> Mentions, IReadOnlyList<PageComment> Replies);

    public sealed record PageComments(Guid PageId, string Title, string SpaceKey, string Url, IReadOnlyList<PageComment> Threads);

    /// <param name="Quote">For an inline comment, the passage it is on, as matched.</param>
    /// <param name="Occurrence">For an inline comment, which time the passage appears on the page, from 1.</param>
    public sealed record CommentWritten(
        Guid Id, Guid PageId, Guid? ParentCommentId, Guid AuthorId, string Author, DateTimeOffset CreatedAt,
        string Body, IReadOnlyList<CommentMention> Mentions, string Url, bool IsInline = false, string? Quote = null,
        int? Occurrence = null);

    [McpServerTool(Name = "list_comments"), Description(
        "The comments on a page, as `threads` in the order they were started, each with its `replies` nested " +
        "beneath it, oldest first. Each comment has its `id` (what reply_to_comment takes), the `author` and " +
        "`authorId`, `createdAt` and `updatedAt`, and its `body` as plain text with each mention shown as @Name; " +
        "the people it mentions, with their ids, are in `mentions`. A thread's first comment says whether the " +
        "thread is `resolved`, and when (`resolvedAt`). An inline comment (`isInline`) is about one part of the " +
        "page: its `quote` is that passage, left out when there is none to show (a comment on an image, or an older " +
        "comment whose passage is no longer on the published page). A deleted comment keeps its place, so its " +
        "replies still make sense, with `isDeleted` true and no `body`. 'Not found' can mean the page does not " +
        "exist or that you may not see it; the two are deliberately indistinguishable.")]
    public static async Task<PageComments> ListComments(
        [Description("The page id (a GUID).")] Guid pageId,
        AppDbContext db, IPermissionService perms, ISiteSettingsService settings, IConfiguration config, CancellationToken ct)
    {
        // The REST list's check: readable, so not a draft of someone else's
        // and not in the trash.
        if (!await perms.CanReadPageAsync(pageId)) throw McpAccess.NotFound("Page");
        var page = await db.Pages.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.Id == pageId)
            .Select(p => new { p.Id, p.Title, SpaceKey = p.Space!.Key, Content = p.CurrentVersion == null ? null : p.CurrentVersion.ContentJson })
            .FirstOrDefaultAsync(ct) ?? throw McpAccess.NotFound("Page");

        var comments = await CommentEndpoints.LoadAsync(pageId, db, ct);
        var quotes = comments.Any(c => c.IsInline) ? InlineQuotes(page.Content) : [];
        var byParent = comments.ToLookup(c => c.ParentCommentId);
        // A reply whose parent is somehow missing is still shown, as a thread
        // of its own, the way the Comments panel does.
        var known = comments.Select(c => c.Id).ToHashSet();
        List<PageComment> Build(IEnumerable<CommentEndpoints.CommentResponse> level) => level
            .Select(c => new PageComment(
                c.Id, c.AuthorId, c.AuthorName, c.CreatedAt, c.UpdatedAt,
                c.ResolvedAt is not null, c.ResolvedAt, c.ResolvedByName, c.IsInline,
                // The anchor's own copy of the passage (22.2), else the
                // highlight's text in the published page (older comments).
                c.IsInline && !c.IsDeleted ? CommentWriter.QuoteOf(c.AnchorJson) ?? quotes.GetValueOrDefault(c.Id) : null,
                c.IsDeleted,
                c.Body is null ? null : CommentEndpoints.Readable(c.Body),
                [.. CommentEndpoints.MentionsIn(c.Body).Select(m => new CommentMention(m.Id, m.Name))],
                Build(byParent[c.Id])))
            .ToList();
        var threads = Build(comments.Where(c => c.ParentCommentId is null || !known.Contains(c.ParentCommentId.Value)));

        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
        return new PageComments(page.Id, page.Title, page.SpaceKey, $"{baseUrl}/spaces/{page.SpaceKey}/pages/{page.Id}", threads);
    }

    [McpServerTool(Name = "add_comment"), Description(
        "Add a comment to a page: a remark, a suggestion or an answer that leaves the page's text alone. The " +
        "page's watchers and anyone it mentions are told, as for a comment written in the browser. Use " +
        "update_page instead when the text itself should change, and reply_to_comment to answer inside an " +
        "existing thread. Without `quote` the comment is about the whole page. With `quote` it is an inline " +
        "comment on that passage, highlighted on the page as a person's would be: quote the words exactly as " +
        "they read, plain words with no Markdown (no ** or [](), a mention as @Name, a status as get_page shows " +
        "it), within one paragraph, heading, list item or table cell. The quote is matched against the page's " +
        "live draft, which can be newer than what get_page returned; spacing and line breaks do not matter, " +
        "capitals do. If the passage appears more than once you are told how many times: pass `occurrence` " +
        "(1 for the first) or quote more of it. An inline comment needs edit rights on the page, since the " +
        "highlight goes into its draft (anyone editing sees it at once; readers see it once the draft is " +
        "published). `body` is plain text; to mention someone, write @[Their Name](user:<their user id>) with an " +
        "id from list_comments (an `authorId` or one of the `mentions`). A bare @Name is only text and tells " +
        "nobody. Returns the new comment's `id`, its `body` as list_comments shows it, the people it mentions, " +
        "the page's `url`, and for an inline comment its `quote` and `occurrence`. Needs a token with write access.")]
    public static async Task<CommentWritten> AddComment(
        [Description("The page id (a GUID).")] Guid pageId,
        [Description("The comment, as plain text.")] string body,
        ICommentWriter writer, AppDbContext db, CurrentUser current, IHttpContextAccessor accessor,
        ISiteSettingsService settings, IConfiguration config, CancellationToken ct,
        [Description("The passage the comment is about, exactly as the page reads, at most 1000 characters. Omit for a comment on the whole page.")]
        string? quote = null,
        [Description("Which time the quoted passage appears on the page, from 1, when it appears more than once.")]
        int? occurrence = null)
    {
        McpAccess.RequireWrite(current, accessor);
        // An empty quote is how some clients say "none".
        if (string.IsNullOrWhiteSpace(quote)) quote = null;
        var result = await writer.CreateAsync(pageId, body, parentCommentId: null, anchorJson: null, ct, quote, occurrence);
        return await WrittenOf(result, "Page", db, settings, config, ct);
    }

    [McpServerTool(Name = "reply_to_comment"), Description(
        "Reply to a comment, in its thread: the reply appears beneath the comment it answers, as a reply written " +
        "in the browser does, and the page's watchers and anyone it mentions are told. `commentId` is any " +
        "comment's id from list_comments, a thread's first comment or a reply in it. `body` and mentions work as " +
        "for add_comment. Returns the new reply, as add_comment does. Needs a token with write access.")]
    public static async Task<CommentWritten> ReplyToComment(
        [Description("The id of the comment to answer (a GUID).")] Guid commentId,
        [Description("The reply, as plain text.")] string body,
        ICommentWriter writer, AppDbContext db, IPermissionService perms, CurrentUser current,
        IHttpContextAccessor accessor, ISiteSettingsService settings, IConfiguration config, CancellationToken ct)
    {
        McpAccess.RequireWrite(current, accessor);
        var pageId = await db.Comments.AsNoTracking().Where(c => c.Id == commentId)
            .Select(c => (Guid?)c.PageId).FirstOrDefaultAsync(ct);
        // A comment on a page you may not read does not exist, as its page does not.
        if (pageId is null || !await perms.CanReadPageAsync(pageId.Value)) throw McpAccess.NotFound("Comment");
        var result = await writer.CreateAsync(pageId.Value, body, commentId, anchorJson: null, ct);
        return await WrittenOf(result, "Comment", db, settings, config, ct);
    }

    private static async Task<CommentWritten> WrittenOf(
        CommentWriteResult result, string what, AppDbContext db, ISiteSettingsService settings, IConfiguration config,
        CancellationToken ct)
    {
        var c = result.Status switch
        {
            // The same masking REST uses: not readable and not there are one answer.
            CommentWriteStatus.NotFound => throw McpAccess.NotFound(what),
            CommentWriteStatus.Invalid => throw new McpException(result.Field + ": " + result.Message),
            // The same words REST answers with (22.2); a refusal over a quote
            // carries its count in the words.
            CommentWriteStatus.Forbidden or CommentWriteStatus.Unprocessable or CommentWriteStatus.Unavailable =>
                throw new McpException(result.Message ?? "That comment could not be added."),
            _ => result.Comment!,
        };
        var key = await db.Pages.AsNoTracking().IgnoreQueryFilters().Where(p => p.Id == c.PageId)
            .Select(p => p.Space!.Key).FirstAsync(ct);
        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
        return new CommentWritten(
            c.Id, c.PageId, c.ParentCommentId, c.AuthorId, c.Author?.DisplayName ?? "Deleted user", c.CreatedAt,
            CommentEndpoints.Readable(c.Body),
            [.. CommentEndpoints.MentionsIn(c.Body).Select(m => new CommentMention(m.Id, m.Name))],
            $"{baseUrl}/spaces/{key}/pages/{c.PageId}",
            c.AnchorJson is not null, CommentWriter.QuoteOf(c.AnchorJson), OccurrenceOf(c.AnchorJson));
    }

    private static int? OccurrenceOf(string? anchorJson)
    {
        if (string.IsNullOrEmpty(anchorJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(anchorJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("occurrence", out var o) && o.TryGetInt32(out var n) ? n : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The longest quote list_comments returns; a passage is a sentence or two, not a chapter.</summary>
    private const int MaxQuoteLength = 1000;

    /// <summary>
    /// The text each inline comment's highlight covers, by comment id. The
    /// editor marks the passage with a <c>comment</c> mark carrying the
    /// comment's id (editor/commentAction.ts); the comment row itself holds
    /// no copy of it. A highlight split across blocks reads as one passage,
    /// its parts joined by a space.
    /// </summary>
    public static Dictionary<Guid, string> InlineQuotes(string? contentJson)
    {
        var found = new Dictionary<Guid, StringBuilder>();
        if (string.IsNullOrEmpty(contentJson)) return [];
        JsonDocument doc;
        try { doc = JsonDocument.Parse(contentJson); }
        catch (JsonException) { return []; }
        using (doc)
        {
            // Which highlights the previous text node in this block carried,
            // so a highlight that resumes in a new block gets a separator.
            var block = 0;
            var lastBlock = new Dictionary<Guid, int>();
            void Walk(JsonElement node)
            {
                if (node.ValueKind != JsonValueKind.Object) return;
                if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                    && node.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mark in marks.EnumerateArray())
                    {
                        if (mark.ValueKind != JsonValueKind.Object
                            || !mark.TryGetProperty("type", out var type) || type.GetString() != "comment"
                            || !mark.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object
                            || !attrs.TryGetProperty("commentId", out var idEl) || idEl.ValueKind != JsonValueKind.String
                            || !Guid.TryParse(idEl.GetString(), out var id))
                            continue;
                        if (!found.TryGetValue(id, out var sb)) found[id] = sb = new StringBuilder();
                        else if (lastBlock[id] != block && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                        lastBlock[id] = block;
                        sb.Append(text.GetString());
                    }
                }
                if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    var textblock = content.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("text", out _));
                    if (textblock) block++;
                    foreach (var child in content.EnumerateArray()) Walk(child);
                }
            }
            Walk(doc.RootElement);
        }
        return found.ToDictionary(kv => kv.Key, kv =>
        {
            var quote = kv.Value.ToString().Trim();
            return quote.Length <= MaxQuoteLength ? quote : quote[..MaxQuoteLength].TrimEnd() + "…";
        });
    }
}
