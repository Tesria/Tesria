using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Comments;

public static partial class CommentEndpoints
{
    public record CreateCommentRequest(string Body, Guid? ParentCommentId, string? AnchorJson);
    public record UpdateCommentRequest(string Body);
    public record CommentResponse(
        Guid Id, Guid PageId, Guid? ParentCommentId, string? Body, string? AnchorJson,
        Guid AuthorId, string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        bool IsInline, bool IsDeleted,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        /// <summary>Set on a resolved thread's first comment (dev-plan 15.3).</summary>
        DateTimeOffset? ResolvedAt = null, string? ResolvedByName = null);

    public static IEndpointRouteBuilder MapCommentEndpoints(this IEndpointRouteBuilder routes)
    {
        var pageScoped = routes.MapGroup("/pages/{pageId:guid}/comments")
            .WithTags("Comments").RequireAuthorization();
        pageScoped.MapGet("/", ListForPage).AllowAnonymous(); // dev-plan 5.2: only with PublicComments
        pageScoped.MapPost("/", Create);

        var byId = routes.MapGroup("/comments/{id:guid}")
            .WithTags("Comments").RequireAuthorization();
        byId.MapPut("/", Update);
        byId.MapDelete("/", Delete);
        byId.MapPost("/resolve", (Guid id, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            SetResolved(id, true, db, current, perms));
        byId.MapPost("/reopen", (Guid id, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            SetResolved(id, false, db, current, perms));

        return routes;
    }

    private static async Task<IResult> ListForPage(
        Guid pageId, AppDbContext db, IPermissionService perms, CurrentUser current)
    {
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();
        // Anonymous readers see comments only where the space allows it; the
        // page itself was already established as public, so 401 here reveals
        // nothing the page did not.
        if (current.Id is null)
        {
            var open = await db.Pages.AsNoTracking().Where(p => p.Id == pageId).Select(p => p.Space!.PublicComments).FirstOrDefaultAsync();
            if (!open) return Results.Unauthorized();
        }
        // Include the author rather than letting the client resolve ids: a
        // per-comment lookup is N round trips, and a client-side directory
        // fetch would hand the whole user list to anyone who can read a page.
        var comments = await db.Comments.AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.PageId == pageId)
            .ToListAsync();
        // Chronological; sorted in memory (bounded per page, and the SQLite test
        // provider cannot ORDER BY DateTimeOffset).
        var resolverIds = comments.Where(c => c.ResolvedById is not null).Select(c => c.ResolvedById!.Value).Distinct().ToList();
        var resolvers = await db.Users.AsNoTracking().Where(u => resolverIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        return Results.Ok(comments.OrderBy(c => c.CreatedAt).Select(c => ToResponse(c) with
        {
            ResolvedByName = c.ResolvedById is { } r && resolvers.TryGetValue(r, out var n) ? n : null,
        }));
    }

    private static async Task<IResult> Create(
        Guid pageId, CreateCommentRequest req, AppDbContext db, CurrentUser current,
        IPermissionService perms, INotificationService notifications, IWebhookDispatcher webhooks)
    {
        // Commenting requires being able to see the page.
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();

        var body = (req.Body ?? "").Trim();
        if (body.Length == 0)
            return Results.ValidationProblem(Error("body", "Comment body is required."));
        if (req.AnchorJson is not null && !IsValidJson(req.AnchorJson))
            return Results.ValidationProblem(Error("anchorJson", "Anchor must be valid JSON."));

        // perms.CanViewPageAsync (above) already resolved the page including
        // drafts; re-resolve SpaceId the same way so a not-yet-published
        // draft's own page can still receive comments.
        var spaceId = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.Id == pageId).Select(p => (Guid?)p.SpaceId).FirstOrDefaultAsync();
        if (spaceId is null) return Results.NotFound();

        if (req.ParentCommentId is { } parentId)
        {
            var parent = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == parentId);
            if (parent is null || parent.PageId != pageId)
                return Results.ValidationProblem(Error("parentCommentId", "Parent comment not found on this page."));
        }

        var now = DateTimeOffset.UtcNow;
        var authorId = current.RequireId();
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            ParentCommentId = req.ParentCommentId,
            Body = body,
            AnchorJson = req.AnchorJson,
            AuthorId = authorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Comments.Add(comment);
        await notifications.NotifyPageWatchersAsync(
            pageId, spaceId.Value, "comment.created", authorId, new { Body = Truncate(body) });
        await NotifyMentionsAsync(pageId, body, previous: null, authorId, db, perms, notifications);
        await db.SaveChangesAsync();
        await webhooks.DispatchAsync(
            spaceId.Value, "comment.created", "page", pageId, new { Body = Truncate(body) });
        // Re-read with the author joined rather than assigning the navigation:
        // attaching a detached User makes EF try to INSERT it, which trips the
        // unique-email constraint. One extra read on create is the cheap,
        // obviously-correct option.
        var saved = await db.Comments.AsNoTracking()
            .Include(c => c.Author)
            .FirstAsync(c => c.Id == comment.Id);
        return Results.Created($"/api/comments/{comment.Id}", ToResponse(saved));
    }

    private static async Task<IResult> Update(
        Guid id, UpdateCommentRequest req, AppDbContext db, CurrentUser current,
        IPermissionService perms, INotificationService notifications)
    {
        // Author included so the response carries the name the client renders.
        var comment = await db.Comments.Include(c => c.Author).FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();
        if (comment.AuthorId != current.RequireId()) return Results.Forbid();

        var body = (req.Body ?? "").Trim();
        if (body.Length == 0)
            return Results.ValidationProblem(Error("body", "Comment body is required."));

        var previous = comment.Body;
        comment.Body = body;
        comment.UpdatedAt = DateTimeOffset.UtcNow;
        await NotifyMentionsAsync(comment.PageId, body, previous, comment.AuthorId, db, perms, notifications);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(comment));
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, CurrentUser current)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();
        if (comment.AuthorId != current.RequireId()) return Results.Forbid();

        // Soft delete: retain the row so any replies keep their thread context.
        comment.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// A mention in a comment (dev-plan 15.3) is written as
    /// <c>@[Display Name](user:&lt;id&gt;)</c>: the name for anyone reading the raw
    /// text, the id so a rename cannot misdirect it.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"@\[[^\]\n]{1,200}\]\(user:(?<id>[0-9a-fA-F-]{36})\)")]
    private static partial System.Text.RegularExpressions.Regex MentionToken();

    private static HashSet<Guid> MentionedIn(string? body) =>
        body is null ? [] : MentionToken().Matches(body)
            .Select(m => Guid.TryParse(m.Groups["id"].Value, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToHashSet();

    /// <summary>
    /// Tells people newly mentioned in a comment, the way the editor does for a
    /// page: never the author, only active accounts, and only if they can see
    /// the page, since the notification names it.
    /// </summary>
    private static async Task NotifyMentionsAsync(
        Guid pageId, string body, string? previous, Guid authorId,
        AppDbContext db, IPermissionService perms, INotificationService notifications)
    {
        var fresh = MentionedIn(body);
        fresh.ExceptWith(MentionedIn(previous));
        fresh.Remove(authorId);
        if (fresh.Count == 0) return;
        var title = await db.Pages.IgnoreQueryFilters().Where(p => p.Id == pageId).Select(p => p.Title).FirstOrDefaultAsync();
        var active = await db.Users.AsNoTracking()
            .Where(u => fresh.Contains(u.Id) && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync();
        foreach (var userId in active)
        {
            if (!await perms.AsUser(userId).CanViewPageAsync(pageId)) continue;
            await notifications.NotifyUserAsync(userId, "user.mentioned", "page", pageId, authorId, new { Title = title });
        }
    }

    /// <summary>
    /// Resolves or reopens a thread (dev-plan 15.3). Its author, anyone who
    /// may edit the page, and the space's administrators may; a reply cannot
    /// be resolved on its own.
    /// </summary>
    private static async Task<IResult> SetResolved(
        Guid id, bool resolved, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var comment = await db.Comments.Include(c => c.Author).FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(comment.PageId)) return Results.NotFound();
        if (comment.ParentCommentId is not null)
            return Results.ValidationProblem(Error("id", "Resolve the thread's first comment; replies go with it."));
        var userId = current.RequireId();
        if (comment.AuthorId != userId && !await perms.CanEditPageAsync(comment.PageId)) return Results.Forbid();

        comment.ResolvedAt = resolved ? DateTimeOffset.UtcNow : null;
        comment.ResolvedById = resolved ? userId : null;
        await db.SaveChangesAsync();
        var name = resolved ? await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstAsync() : null;
        return Results.Ok(ToResponse(comment) with { ResolvedByName = name });
    }

    private static CommentResponse ToResponse(Comment c)
    {
        var deleted = c.DeletedAt is not null;
        return new CommentResponse(
            c.Id, c.PageId, c.ParentCommentId,
            deleted ? null : c.Body,
            c.AnchorJson,
            c.AuthorId,
            // 2.2 anonymizes a deleted user rather than removing the row, so
            // the navigation can still be null only if data is inconsistent.
            // Either way this must render as something, never blank.
            c.Author?.DisplayName ?? "Deleted user",
            c.Author?.AvatarKey is null ? null : c.Author.AvatarHash,
            c.Author?.AvatarVariant,
            IsInline: c.AnchorJson is not null,
            IsDeleted: deleted,
            c.CreatedAt, c.UpdatedAt,
            c.ResolvedAt);
    }

    private static bool IsValidJson(string input)
    {
        try { using var _ = JsonDocument.Parse(input); return true; }
        catch (JsonException) { return false; }
    }

    private static string Truncate(string value) =>
        value.Length <= 140 ? value : value[..140].TrimEnd() + "…";

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
