using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Pages;

/// <summary>
/// Copying a page, and optionally the pages under it (dev-plan 15.3).
///
/// A copy is a new page: the content and labels come across, the history and
/// the comments do not. The root is titled "Copy of …" so the two cannot be
/// confused in a tree or a search.
///
/// Attachments are copied too, file and all, and the copy's content is
/// pointed at the copies. A copy that kept pointing at the original's files
/// would lose its pictures for anyone who can read the copy but not the
/// original, and again when the original is deleted.
///
/// Each page is created through the page writer, so it is audited, watchers
/// hear about it and webhooks fire, like any other new page. Pages the caller
/// cannot read are left out, with everything under them.
/// </summary>
public static class PageCopy
{
    public record CopyPageRequest(Guid? SpaceId, Guid? ParentPageId, bool IncludeChildren = false);
    public record CopyPageResponse(Guid Id, Guid SpaceId, string Title, int Pages);

    public static async Task<IResult> CopyAsync(
        Guid id, CopyPageRequest req, AppDbContext db, IPermissionService perms, IPageWriter writer,
        IAttachmentStorage storage, CurrentUser current, IAuditLogger audit, CancellationToken ct)
    {
        var source = await db.Pages.AsNoTracking().Include(p => p.CurrentVersion).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (source?.CurrentVersion is null || !await perms.CanViewPageAsync(id)) return Results.NotFound();

        var spaceId = req.SpaceId ?? source.SpaceId;
        // A space or parent the caller cannot see answers as a missing one
        // does (dev-plan 14.1).
        if (req.SpaceId is { } targetSpace && !await perms.CanViewSpaceAsync(targetSpace)) return Results.NotFound();
        if (req.ParentPageId is { } parentId)
        {
            var parent = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parentId, ct);
            if (parent is null || parent.SpaceId != spaceId || !await perms.CanViewPageAsync(parentId))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["parentPageId"] = ["Parent page not found in that space."] });
        }

        var userId = current.RequireId();
        var copied = 0;
        // Pages this copy made: never copied again. Copying a page with its
        // sub-pages to somewhere beneath itself would otherwise find its own
        // copies among the children and never finish.
        var made = new HashSet<Guid>();
        async Task<Guid?> CopyOneAsync(Page page, Guid? parent, bool root)
        {
            var version = page.CurrentVersion ?? await db.PageVersions.AsNoTracking().FirstAsync(v => v.Id == page.CurrentVersionId, ct);
            var title = root ? $"Copy of {page.Title}" : page.Title;
            var created = await writer.CreateAsync(spaceId, parent, title, version.ContentJson, ct);
            if (created.Status != PageWriteStatus.Ok || created.Page is null) return null;
            copied++;
            var newPageId = created.Page.Id;
            made.Add(newPageId);

            // The files first, then the content that points at them.
            var content = version.ContentJson;
            var attachments = await db.Attachments.AsNoTracking().Where(a => a.PageId == page.Id).ToListAsync(ct);
            foreach (var a in attachments)
            {
                await using var bytes = storage.OpenRead(a.StorageKey);
                if (bytes is null) continue;
                var copy = new Attachment
                {
                    Id = Guid.NewGuid(),
                    PageId = newPageId,
                    Filename = a.Filename,
                    ContentType = a.ContentType,
                    Size = a.Size,
                    StorageKey = Guid.NewGuid().ToString("N"),
                    UploadedById = userId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                await storage.SaveAsync(copy.StorageKey, bytes, ct);
                db.Attachments.Add(copy);
                // Ids are GUIDs, so a plain replace cannot touch anything else.
                content = content.Replace(a.Id.ToString(), copy.Id.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            if (content != version.ContentJson)
            {
                var first = await db.PageVersions.FirstAsync(v => v.PageId == newPageId, ct);
                first.ContentJson = content;
                var newPage = await db.Pages.FirstAsync(p => p.Id == newPageId, ct);
                newPage.SearchText = PageContent.BuildSearchText(newPage.Title, content);
            }

            // The emoji is the page's, not its content's, so the writer does
            // not carry it (dev-plan 15.7).
            if (page.Emoji is not null)
                (await db.Pages.FirstAsync(p => p.Id == newPageId, ct)).Emoji = page.Emoji;

            var labels = await db.PageLabels.AsNoTracking().Where(l => l.PageId == page.Id).Select(l => l.LabelId).ToListAsync(ct);
            foreach (var labelId in labels)
                db.PageLabels.Add(new PageLabel { PageId = newPageId, LabelId = labelId, AddedById = userId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            return newPageId;
        }

        var rootCopy = await CopyOneAsync(source, req.ParentPageId, root: true);
        if (rootCopy is null) return Results.Forbid();

        if (req.IncludeChildren)
        {
            // Breadth first, so every parent exists before its children.
            var queue = new Queue<(Guid From, Guid To)>();
            queue.Enqueue((source.Id, rootCopy.Value));
            while (queue.Count > 0)
            {
                var (from, to) = queue.Dequeue();
                var children = await db.Pages.AsNoTracking().Include(p => p.CurrentVersion)
                    .Where(p => p.ParentPageId == from).OrderBy(p => p.Position).ToListAsync(ct);
                foreach (var child in children)
                {
                    if (made.Contains(child.Id) || child.CurrentVersion is null || !await perms.CanViewPageAsync(child.Id)) continue;
                    if (await CopyOneAsync(child, to, root: false) is { } childCopy) queue.Enqueue((child.Id, childCopy));
                }
            }
        }

        audit.Record("page.copied", "page", rootCopy.Value, new { From = source.Id, source.Title, Pages = copied });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CopyPageResponse(rootCopy.Value, spaceId, $"Copy of {source.Title}", copied));
    }
}
