using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Exporting a space as a pack (dev-plan 8.5).
///
/// <para>The sibling of the site export, and worth telling apart: a site
/// (12.2) is for people to read, so it is captured HTML and defaults to what
/// the anonymous public may see. A pack is for Tesria to read back, so it is
/// the documents themselves and exports <i>everything the caller can see</i>.
/// Preservation, not publication.</para>
/// </summary>
public static class PackExportEndpoints
{
    public static IEndpointRouteBuilder MapPackExportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/spaces/{key}/export/pack", ExportPack)
            .WithTags("Export")
            .RequirePermission(InstancePermissions.PagesExport);
        return routes;
    }

    /// <param name="progress">An id to read how far along it is from <c>/api/export-progress/{id}</c> (dev-plan 20.1).</param>
    private static async Task<IResult> ExportPack(
        string key, string? progress, ExportProgress tracker, Infrastructure.Auth.CurrentUser current, AppDbContext db, IPermissionService perms, IAttachmentStorage storage,
        IProfileMediaService media, ISiteSettingsService settings, IAuditLogger audit,
        CancellationToken ct)
    {
        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant(), ct);
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        // Turned off for this space (dev-plan 12.3), for everyone. A pack is
        // the most complete export there is, history and all, so a space
        // sensitive enough to lock down is one where this matters most.
        if (!SpaceExports.Allows(space, ExportFormat.Pack)) return SpaceExports.Refused(space, ExportFormat.Pack);

        var report = tracker.Start(current.Id, progress);
        report.Stage("Checking which pages go in", 0);
        WikiPack.Model model;
        Dictionary<Guid, string> storageKeys;
        try { (model, storageKeys) = await BuildAsync(db, perms, settings, space, ct); }
        catch { report.Finish(); throw; }

        audit.Record("space.exported", "space", space.Id, new
        {
            space.Key,
            Format = WikiPack.Format,
            model.Manifest.Counts.Pages,
            model.Manifest.Omitted,
        });
        await db.SaveChangesAsync(ct);

        // Spooled to a temporary file, then streamed from it.
        //
        // Not because a pack is too large to hold (though one can be), but
        // because ZipArchive writes synchronously and finishes by writing its
        // central directory on Dispose, and Kestrel refuses synchronous writes
        // to a response. A file absorbs that, and DeleteOnClose means the copy
        // lives exactly as long as the response does, including when the
        // response fails halfway.
        var spool = new FileStream(
            Path.Combine(Path.GetTempPath(), $"tesria-pack-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await WikiPack.WriteAsync(
                spool, model,
                (name, _) => Task.FromResult(Open(name, storageKeys, storage, media, space)),
                ct, report);
            spool.Position = 0;
        }
        catch
        {
            await spool.DisposeAsync();
            throw;
        }
        finally
        {
            report.Finish();
        }

        return Results.Stream(spool, "application/zip", $"{space.Key.ToLowerInvariant()}-pack.zip");
    }

    /// <summary>
    /// Resolves a pack entry name back to the bytes behind it.
    ///
    /// The storage key never appears in the pack (it is this instance's
    /// business, and an importer mints its own), so it is looked up here from
    /// the map the build returned rather than carried in the format.
    /// </summary>
    private static Stream? Open(
        string entry, IReadOnlyDictionary<Guid, string> storageKeys,
        IAttachmentStorage storage, IProfileMediaService media, Space space)
    {
        if (entry == WikiPack.IconEntry)
            return media.OpenRead(media.KeyFor(ProfileMediaKind.SpaceIcon, space.Id));

        // The entry is named after the attachment, so the id is read back out
        // of the name rather than searched for across every page.
        if (!entry.StartsWith(WikiPack.AttachmentPrefix, StringComparison.Ordinal)) return null;
        if (!Guid.TryParse(entry[WikiPack.AttachmentPrefix.Length..], out var id)) return null;
        return storageKeys.TryGetValue(id, out var key) ? storage.OpenRead(key) : null;
    }

    /// <summary>
    /// Builds the model from a space, seeing exactly what the caller can see.
    ///
    /// The page walk is 12.2's: narrow in SQL, then ask
    /// <c>CanViewPageAsync</c> per page, so a page the caller may not read
    /// cannot reach the file. A page whose parent is hidden is lifted to the
    /// root rather than vanishing with it.
    /// </summary>
    private static async Task<(WikiPack.Model Model, Dictionary<Guid, string> StorageKeys)> BuildAsync(
        AppDbContext db, IPermissionService perms, ISiteSettingsService settings,
        Space space, CancellationToken ct)
    {
        var rows = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == space.Id && p.Status == PageStatus.Current && p.DeletedAt == null)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.ParentPageId, p.Title, p.Position, p.FullWidth, p.CreatedAt, p.CreatedById, p.Emoji })
            .ToListAsync(ct);

        var visible = new List<Guid>();
        foreach (var row in rows)
            if (await perms.CanViewPageAsync(row.Id)) visible.Add(row.Id);
        var allowed = visible.ToHashSet();
        var omitted = rows.Count - allowed.Count;

        var ids = allowed.ToList();
        var versions = await db.PageVersions.AsNoTracking()
            .Where(v => ids.Contains(v.PageId))
            .OrderBy(v => v.PageId).ThenBy(v => v.VersionNumber)
            .Select(v => new { v.PageId, v.VersionNumber, v.ContentJson, v.AuthorId, v.ChangeComment, v.CreatedAt })
            .ToListAsync(ct);
        var currentOf = await db.Pages.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, Number = p.CurrentVersion!.VersionNumber })
            .ToDictionaryAsync(p => p.Id, p => p.Number, ct);

        // Ordered in memory rather than in SQL: the deterministic order a
        // pack needs is by creation time, and SQLite (which the test suite
        // runs on) cannot sort a DateTimeOffset. The rows are all here anyway.
        var attachments = (await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.PageId))
            .Select(a => new { a.Id, a.PageId, a.Filename, a.ContentType, a.Size, a.StorageKey, a.UploadedById })
            .ToListAsync(ct))
            .OrderBy(a => a.PageId).ThenBy(a => a.Id)
            .ToList();
        var storageKeys = attachments.ToDictionary(a => a.Id, a => a.StorageKey);

        var comments = (await db.Comments.AsNoTracking()
            .Where(c => ids.Contains(c.PageId))
            .Select(c => new
            {
                c.Id, c.PageId, c.ParentCommentId, c.Body, c.AnchorJson,
                c.AuthorId, c.CreatedAt, c.UpdatedAt, c.DeletedAt,
            })
            .ToListAsync(ct))
            .OrderBy(c => c.PageId).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToList();

        var labels = await db.PageLabels.AsNoTracking()
            .Where(pl => ids.Contains(pl.PageId))
            .Join(db.Labels.AsNoTracking(), pl => pl.LabelId, l => l.Id, (pl, l) => new { pl.PageId, l.Name })
            .ToListAsync(ct);

        var templates = await db.PageTemplates.AsNoTracking()
            .Where(t => t.SpaceId == space.Id)
            .OrderBy(t => t.Name)
            .Select(t => new WikiPack.PackTemplate(t.Name, t.Description, t.ContentJson))
            .ToListAsync(ct);

        var pages = new List<WikiPack.PackPage>();
        foreach (var row in rows.Where(r => allowed.Contains(r.Id)))
        {
            var mine = versions.Where(v => v.PageId == row.Id).ToList();
            pages.Add(new WikiPack.PackPage(
                row.Id,
                // A parent nobody can see would otherwise take its children
                // out of the pack with it.
                row.ParentPageId is { } parent && allowed.Contains(parent) ? parent : null,
                row.Position,
                row.Title,
                row.FullWidth,
                row.CreatedAt,
                row.CreatedById,
                currentOf.TryGetValue(row.Id, out var number) ? number : mine.Count,
                [.. mine.Select(v => new WikiPack.PackVersion(
                    v.VersionNumber, v.CreatedAt, v.AuthorId, v.ChangeComment,
                    JsonNode.Parse(v.ContentJson) ?? new JsonObject()))],
                [.. labels.Where(l => l.PageId == row.Id).Select(l => l.Name).Order(StringComparer.Ordinal)],
                [.. attachments.Where(a => a.PageId == row.Id).Select(a => new WikiPack.PackAttachment(
                    a.Id, a.Filename, a.ContentType, a.Size, WikiPack.AttachmentEntry(a.Id)))],
                // A deleted comment keeps its place in its thread but not its
                // words: the app never shows them again, and a pack must not
                // either (the 14.1 review).
                [.. comments.Where(c => c.PageId == row.Id).Select(c => new WikiPack.PackComment(
                    c.Id, c.ParentCommentId, c.AuthorId, c.DeletedAt is null ? c.Body : "", c.DeletedAt is null ? c.AnchorJson : null,
                    c.CreatedAt, c.UpdatedAt, c.DeletedAt))],
                row.Emoji));
        }

        // Display names only, and only for the people who actually wrote
        // something in the pack. No email addresses: this file gets committed.
        var authorIds = pages
            .SelectMany(p => p.Versions.Select(v => v.Author)
                .Concat(p.Comments.Select(c => c.Author))
                .Append(p.CreatedBy))
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var authors = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => new WikiPack.Author(u.DisplayName), ct);

        var hasIcon = space.IconKind == SpaceIconKind.Image;
        var spaceRestrictions = await db.SpacePermissions.CountAsync(p => p.SpaceId == space.Id, ct);
        var pageRestrictions = await db.PageRestrictions.CountAsync(r => ids.Contains(r.PageId), ct);
        var instance = (await settings.GetAsync(ct)).InstanceName;

        return (new WikiPack.Model(
            new WikiPack.Manifest(
                WikiPack.Format,
                // Which release made it (dev-plan 16.3), so an import can say
                // so and a refusal can say which Tesria it needs.
                $"Tesria {Infrastructure.Versioning.AppVersion.Current}",
                DateTimeOffset.UtcNow,
                instance,
                new WikiPack.ManifestSpace(space.Key, space.Name),
                new WikiPack.Counts(
                    pages.Count,
                    pages.Sum(p => p.Versions.Count),
                    pages.Sum(p => p.Attachments.Count),
                    pages.Sum(p => p.Comments.Count),
                    templates.Count,
                    labels.Select(l => l.Name).Distinct().Count()),
                new WikiPack.Omitted(omitted),
                // That they existed, never what they said.
                new WikiPack.Restrictions(spaceRestrictions, pageRestrictions)),
            new WikiPack.PackSpace(
                space.Key,
                space.Name,
                space.Description,
                space.HomepageId is { } home && allowed.Contains(home) ? home : null,
                new WikiPack.PackIcon(
                    (int)space.IconKind,
                    hasIcon ? null : space.IconValue,
                    space.IconColor,
                    hasIcon ? WikiPack.IconEntry : null),
                templates,
                (int)space.TreeStyle),
            authors,
            pages), storageKeys);
    }
}
