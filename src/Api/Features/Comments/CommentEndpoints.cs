using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Comments;

public static class CommentEndpoints
{
    public record CreateCommentRequest(string Body, Guid? ParentCommentId, string? AnchorJson);
    public record UpdateCommentRequest(string Body);
    public record CommentResponse(
        Guid Id, Guid PageId, Guid? ParentCommentId, string? Body, string? AnchorJson,
        Guid AuthorId, string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        bool IsInline, bool IsDeleted,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

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
        return Results.Ok(comments.OrderBy(c => c.CreatedAt).Select(ToResponse));
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
        Guid id, UpdateCommentRequest req, AppDbContext db, CurrentUser current)
    {
        // Author included so the response carries the name the client renders.
        var comment = await db.Comments.Include(c => c.Author).FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();
        if (comment.AuthorId != current.RequireId()) return Results.Forbid();

        var body = (req.Body ?? "").Trim();
        if (body.Length == 0)
            return Results.ValidationProblem(Error("body", "Comment body is required."));

        comment.Body = body;
        comment.UpdatedAt = DateTimeOffset.UtcNow;
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

    private static CommentResponse ToResponse(Comment c)
    {
        var deleted = c.DeletedAt is not null;
        return new CommentResponse(
            c.Id, c.PageId, c.ParentCommentId,
            deleted ? null : c.Body,
            c.AnchorJson,
            c.AuthorId,
            // 2.2 anonymises a deleted user rather than removing the row, so
            // the navigation can still be null only if data is inconsistent.
            // Either way this must render as something, never blank.
            c.Author?.DisplayName ?? "Deleted user",
            c.Author?.AvatarKey is null ? null : c.Author.AvatarHash,
            c.Author?.AvatarVariant,
            IsInline: c.AnchorJson is not null,
            IsDeleted: deleted,
            c.CreatedAt, c.UpdatedAt);
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
