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
        // The framework attaches its own anti-forgery requirement to IFormFile
        // endpoints; it is switched off because that scheme (form tokens) is
        // not the one in use. Cross-site protection for this endpoint is the
        // CsrfHeaderMiddleware (dev-plan 3.4), the same as for the JSON ones.
        pageScoped.MapPost("/", Upload).DisableAntiforgery();
        pageScoped.MapGet("/", ListForPage).AllowAnonymous(); // dev-plan 5.2: checked through the page

        var byId = routes.MapGroup("/attachments/{id:guid}")
            .WithTags("Attachments").RequireAuthorization();
        byId.MapGet("/", GetMetadata).AllowAnonymous();
        byId.MapGet("/download", Download).AllowAnonymous();
        byId.MapGet("/view", View).AllowAnonymous();
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

        // The type the file will be served as is decided from its bytes and
        // its declared type together: see ContentTypes.
        var head = new byte[16];
        int headLength;
        await using (var peek = file.OpenReadStream())
            headLength = await peek.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            PageId = pageId,
            Filename = Path.GetFileName(file.FileName),
            ContentType = ContentTypes.Resolve(head.AsSpan(0, headLength), file.ContentType),
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
        if (!await perms.CanReadPageAsync(pageId)) return Results.NotFound();
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
        if (!await perms.CanReadPageAsync(a.PageId)) return Results.NotFound();
        return Results.Ok(ToResponse(a));
    }

    private static async Task<IResult> Download(
        Guid id, AppDbContext db, IAttachmentStorage storage, IPermissionService perms)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (a is null) return Results.NotFound();
        // Attachments are addressed by their own id, so the page's view rules
        // must be re-checked here or restricted files would leak.
        if (!await perms.CanReadPageAsync(a.PageId)) return Results.NotFound();
        var stream = storage.OpenRead(a.StorageKey);
        if (stream is null) return Results.NotFound();
        return Results.File(stream, a.ContentType, a.Filename);
    }

    /// <summary>
    /// A PDF for the page's own viewer (the file block frames it). The
    /// download above says "attachment", and every response refuses to be
    /// framed at all, so the viewer used to download the PDF the moment its
    /// page opened, for every reader (found 2026-09-24). This one says
    /// "inline" and may be framed by this site's own pages, and it serves PDFs
    /// only: a PDF's scripts run in the browser's PDF viewer, not with this
    /// origin, which is not true of everything a browser might show.
    /// </summary>
    private static async Task<IResult> View(
        Guid id, AppDbContext db, IAttachmentStorage storage, IPermissionService perms, HttpContext http)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (a is null || !string.Equals(a.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();
        if (!await perms.CanReadPageAsync(a.PageId)) return Results.NotFound();
        var stream = storage.OpenRead(a.StorageKey);
        if (stream is null) return Results.NotFound();

        var headers = http.Response.Headers;
        headers["X-Frame-Options"] = "SAMEORIGIN";
        // The site's policy would forbid framing and restrict what the PDF
        // viewer loads; a PDF needs neither, only who may frame it.
        headers.Remove("Content-Security-Policy-Report-Only");
        headers["Content-Security-Policy"] = "frame-ancestors 'self'";
        headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline")
        {
            FileNameStar = a.Filename,
        }.ToString();
        return Results.File(stream, a.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> Delete(
        Guid id, AppDbContext db, IAttachmentStorage storage, IPermissionService perms)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(a => a.Id == id);
        if (a is null) return Results.NotFound();
        if (!await perms.CanReadPageAsync(a.PageId)) return Results.NotFound();
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
