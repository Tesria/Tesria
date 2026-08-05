using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Attachments;

public static class AttachmentEndpoints
{
    // Per-file upload ceiling. Kept modest for the MVP; configurable later.
    private const long MaxBytes = 25 * 1024 * 1024;

    public record AttachmentResponse(
        Guid Id, Guid PageId, string Filename, string ContentType, long Size,
        Guid UploadedById, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var pageScoped = routes.MapGroup("/pages/{pageId:guid}/attachments")
            .WithTags("Attachments").RequireAuthorization();
        // SameSite=Lax cookies guard against cross-site posts; antiforgery tokens
        // are not used for this same-origin SPA upload.
        pageScoped.MapPost("/", Upload).DisableAntiforgery();
        pageScoped.MapGet("/", ListForPage);

        var byId = routes.MapGroup("/attachments/{id:guid}")
            .WithTags("Attachments").RequireAuthorization();
        byId.MapGet("/", GetMetadata);
        byId.MapGet("/download", Download);
        byId.MapDelete("/", Delete);

        return routes;
    }

    private static async Task<IResult> Upload(
        Guid pageId, IFormFile? file, AppDbContext db, IAttachmentStorage storage, CurrentUser current,
        IPermissionService perms)
    {
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(pageId)) return Results.Forbid();

        if (file is null || file.Length == 0)
            return Results.ValidationProblem(Error("file", "A non-empty file is required."));
        if (file.Length > MaxBytes)
            return Results.ValidationProblem(Error("file", $"File exceeds the {MaxBytes / (1024 * 1024)} MB limit."));

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            Filename = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream" : file.ContentType,
            Size = file.Length,
            StorageKey = Guid.NewGuid().ToString("N"),
            UploadedById = current.RequireId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var stream = file.OpenReadStream())
            await storage.SaveAsync(attachment.StorageKey, stream);

        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        return Results.Created($"/api/attachments/{attachment.Id}", ToResponse(attachment));
    }

    private static async Task<IResult> ListForPage(
        Guid pageId, AppDbContext db, IPermissionService perms)
    {
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();
        var items = await db.Attachments.AsNoTracking()
            .Where(a => a.PageId == pageId)
            .ToListAsync();
        // Sort newest-first in memory: the set per page is bounded, and the
        // SQLite test provider cannot ORDER BY DateTimeOffset.
        return Results.Ok(items.OrderByDescending(a => a.CreatedAt).Select(ToResponse));
    }

    private static async Task<IResult> GetMetadata(Guid id, AppDbContext db, IPermissionService perms)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (a is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(a.PageId)) return Results.NotFound();
        return Results.Ok(ToResponse(a));
    }

    private static async Task<IResult> Download(
        Guid id, AppDbContext db, IAttachmentStorage storage, IPermissionService perms)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (a is null) return Results.NotFound();
        // Attachments are addressed by their own id, so the page's view rules
        // must be re-checked here or restricted files would leak.
        if (!await perms.CanViewPageAsync(a.PageId)) return Results.NotFound();
        var stream = storage.OpenRead(a.StorageKey);
        if (stream is null) return Results.NotFound();
        return Results.File(stream, a.ContentType, a.Filename);
    }

    private static async Task<IResult> Delete(
        Guid id, AppDbContext db, IAttachmentStorage storage, IPermissionService perms)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id);
        if (a is null) return Results.NotFound();
        if (!await perms.CanViewPageAsync(a.PageId)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(a.PageId)) return Results.Forbid();
        db.Attachments.Remove(a);
        await db.SaveChangesAsync();
        storage.Delete(a.StorageKey);
        return Results.NoContent();
    }

    private static AttachmentResponse ToResponse(Attachment a) =>
        new(a.Id, a.PageId, a.Filename, a.ContentType, a.Size, a.UploadedById, a.CreatedAt);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
