using System.Text.Json;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Auth;
using ConfluenceClone.Api.Infrastructure.Notifications;
using ConfluenceClone.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Comments;

public static class CommentEndpoints
{
    public record CreateCommentRequest(string Body, Guid? ParentCommentId, string? AnchorJson);
    public record UpdateCommentRequest(string Body);
    public record CommentResponse(
        Guid Id, Guid PageId, Guid? ParentCommentId, string? Body, string? AnchorJson,
        Guid AuthorId, bool IsInline, bool IsDeleted,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public static IEndpointRouteBuilder MapCommentEndpoints(this IEndpointRouteBuilder routes)
    {
        var pageScoped = routes.MapGroup("/pages/{pageId:guid}/comments")
            .WithTags("Comments").RequireAuthorization();
        pageScoped.MapGet("/", ListForPage);
        pageScoped.MapPost("/", Create);

        var byId = routes.MapGroup("/comments/{id:guid}")
            .WithTags("Comments").RequireAuthorization();
        byId.MapPut("/", Update);
        byId.MapDelete("/", Delete);

        return routes;
    }

    private static async Task<IResult> ListForPage(
        Guid pageId, AppDbContext db, IPermissionService perms)
    {
        if (!await db.Pages.AnyAsync(p => p.Id == pageId)) return Results.NotFound();
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.PageId == pageId)
            .ToListAsync();
        // Chronological; sorted in memory (bounded per page, and the SQLite test
        // provider cannot ORDER BY DateTimeOffset).
        return Results.Ok(comments.OrderBy(c => c.CreatedAt).Select(ToResponse));
    }

    private static async Task<IResult> Create(
        Guid pageId, CreateCommentRequest req, AppDbContext db, CurrentUser current,
        IPermissionService perms, INotificationService notifications)
    {
        // Commenting requires being able to see the page.
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();

        var body = (req.Body ?? "").Trim();
        if (body.Length == 0)
            return Results.ValidationProblem(Error("body", "Comment body is required."));
        if (req.AnchorJson is not null && !IsValidJson(req.AnchorJson))
            return Results.ValidationProblem(Error("anchorJson", "Anchor must be valid JSON."));

        var spaceId = await db.Pages.Where(p => p.Id == pageId).Select(p => (Guid?)p.SpaceId).FirstOrDefaultAsync();
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
        return Results.Created($"/api/comments/{comment.Id}", ToResponse(comment));
    }

    private static async Task<IResult> Update(
        Guid id, UpdateCommentRequest req, AppDbContext db, CurrentUser current)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
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
