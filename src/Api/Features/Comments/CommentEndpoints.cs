using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
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
        pageScoped.MapGet("/", ListForPage).AllowAnonymous().Produces<List<CommentResponse>>(); // dev-plan 5.2: only with PublicComments
        pageScoped.MapPost("/", Create).Produces<CommentResponse>(StatusCodes.Status201Created);

        var byId = routes.MapGroup("/comments/{id:guid}")
            .WithTags("Comments").RequireAuthorization();
        byId.MapPut("/", Update).Produces<CommentResponse>();
        byId.MapDelete("/", Delete).Produces(StatusCodes.Status204NoContent);
        byId.MapPost("/resolve", (Guid id, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            SetResolved(id, true, db, current, perms)).Produces<CommentResponse>();
        byId.MapPost("/reopen", (Guid id, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            SetResolved(id, false, db, current, perms)).Produces<CommentResponse>();

        return routes;
    }

    private static async Task<IResult> ListForPage(
        Guid pageId, AppDbContext db, IPermissionService perms, CurrentUser current)
    {
        if (!await perms.CanReadPageAsync(pageId)) return Results.NotFound();
        // Anonymous readers see comments only where the space allows it; the
        // page itself was already established as public, so 401 here reveals
        // nothing the page did not.
        if (current.Id is null)
        {
            var open = await db.Pages.AsNoTracking().Where(p => p.Id == pageId).Select(p => p.Space!.PublicComments).FirstOrDefaultAsync();
            if (!open) return Results.Unauthorized();
        }
        return Results.Ok(await LoadAsync(pageId, db));
    }

    /// <summary>
    /// A page's comments, oldest first, as the REST list returns them and the
    /// MCP list_comments tool nests them (dev-plan 22.1). The caller has
    /// already established that the page may be read.
    /// </summary>
    internal static async Task<List<CommentResponse>> LoadAsync(Guid pageId, AppDbContext db, CancellationToken ct = default)
    {
        // Include the author rather than letting the client resolve ids: a
        // per-comment lookup is N round trips, and a client-side directory
        // fetch would hand the whole user list to anyone who can read a page.
        var comments = await db.Comments.AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.PageId == pageId)
            .ToListAsync(ct);
        // Chronological; sorted in memory (bounded per page, and the SQLite test
        // provider cannot ORDER BY DateTimeOffset).
        var resolverIds = comments.Where(c => c.ResolvedById is not null).Select(c => c.ResolvedById!.Value).Distinct().ToList();
        var resolvers = await db.Users.AsNoTracking().Where(u => resolverIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return comments.OrderBy(c => c.CreatedAt).Select(c => ToResponse(c) with
        {
            ResolvedByName = c.ResolvedById is { } r && resolvers.TryGetValue(r, out var n) ? n : null,
        }).ToList();
    }

    private static async Task<IResult> Create(
        Guid pageId, CreateCommentRequest req, ICommentWriter writer)
    {
        // The checks, notifications and webhook live in the writer, which the
        // MCP comment tools share (dev-plan 22.1).
        var result = await writer.CreateAsync(pageId, req.Body, req.ParentCommentId, req.AnchorJson);
        return result.Status switch
        {
            CommentWriteStatus.NotFound => Results.NotFound(),
            CommentWriteStatus.Invalid => Results.ValidationProblem(Error(result.Field!, result.Message!)),
            _ => Results.Created($"/api/comments/{result.Comment!.Id}", ToResponse(result.Comment)),
        };
    }

    private static async Task<IResult> Update(
        Guid id, UpdateCommentRequest req, AppDbContext db, CurrentUser current,
        IPermissionService perms, INotificationService notifications)
    {
        // Author included so the response carries the name the client renders.
        var comment = await db.Comments.Include(c => c.Author).FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();
        // The author may have lost sight of the page since (dev-plan 14.1).
        if (!await perms.CanReadPageAsync(comment.PageId)) return Results.NotFound();
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

    private static async Task<IResult> Delete(Guid id, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();
        // The author may have lost sight of the page since (dev-plan 14.1).
        if (!await perms.CanReadPageAsync(comment.PageId)) return Results.NotFound();
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

    /// <summary>
    /// The comment as a person reads it, each mention as "@Name": what a
    /// notification or its email shows. Webhooks keep the tokens, for the ids.
    /// </summary>
    internal static string Readable(string body) =>
        MentionToken().Replace(body, m => "@" + m.Value[2..m.Value.IndexOf(']')]);

    /// <summary>
    /// The people a comment mentions, in the order written and each once:
    /// what the MCP tools show beside the readable text, so an assistant has
    /// the ids to mention someone back (dev-plan 22.1).
    /// </summary>
    internal static IReadOnlyList<(Guid Id, string Name)> MentionsIn(string? body) =>
        body is null ? [] : MentionToken().Matches(body)
            .Select(m => (Ok: Guid.TryParse(m.Groups["id"].Value, out var g), Id: g, Name: m.Value[2..m.Value.IndexOf(']')]))
            .Where(m => m.Ok).DistinctBy(m => m.Id).Select(m => (m.Id, m.Name)).ToList();

    private static HashSet<Guid> MentionedIn(string? body) =>
        body is null ? [] : MentionToken().Matches(body)
            .Select(m => Guid.TryParse(m.Groups["id"].Value, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToHashSet();

    /// <summary>
    /// Tells people newly mentioned in a comment, the way the editor does for a
    /// page: never the author, only active accounts, and only if they can see
    /// the page, since the notification names it.
    /// </summary>
    internal static async Task NotifyMentionsAsync(
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
        if (!await perms.CanReadPageAsync(comment.PageId)) return Results.NotFound();
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

    internal static bool IsValidJson(string input)
    {
        try { using var _ = JsonDocument.Parse(input); return true; }
        catch (JsonException) { return false; }
    }

    internal static string Truncate(string value) =>
        value.Length <= 140 ? value : value[..140].TrimEnd() + "…";

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
