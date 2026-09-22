using System.IO.Compression;
using System.Text.RegularExpressions;
using Tesria.Api.Domain;
using Tesria.Api.Features.Pages;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Export;

/// <summary>
/// Reading a wiki pack back into an instance (dev-plan 8.5).
///
/// <para>Two things are true of every import and shape the whole file. The
/// zip is <b>untrusted</b>, so nothing in it is believed: names are matched
/// against the format's own shapes, documents go through the same door every
/// stored page goes through, and an attachment's type is decided by its bytes
/// rather than by what the manifest says about them. And an import is
/// <b>atomic</b>, because a half-imported space is worse than no space: a
/// thousand rows land in one transaction, and the bytes written to storage
/// before it are swept up again if it does not commit.</para>
///
/// <para>What comes out is a space that is private, unarchived, owned by
/// whoever imported it and attributed to them. None of that is carried from
/// the pack, and the reasons are in the dev plan: a zip file must not be able
/// to publish a space, and it must not be able to assert who wrote something
/// on an instance it has never seen.</para>
/// </summary>
public static partial class PackImportEndpoints
{
    public static IEndpointRouteBuilder MapPackImportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/spaces/import", Import)
            .WithTags("Export")
            .DisableAntiforgery()
            // Both of these default to far less than a pack may be: Kestrel
            // caps a request body at 30 MB and the form reader caps a
            // multipart section at 128 MB. Without them the format's own
            // ceiling would be decoration, and a large pack would fail with a
            // transport error rather than an answer. Caddy allows 100 MB in
            // front of this, which is the practical limit over the network.
            .WithMetadata(new RequestSizeLimitAttribute(WikiPack.MaxTotalBytes))
            .WithMetadata(new RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = WikiPack.MaxTotalBytes,
            })
            .RequireRateLimiting(RateLimits.ImportPolicy)
            .RequirePermission(InstancePermissions.SpacesCreate);
        return routes;
    }

    public sealed record ImportResponse(
        string Key, string Name, int Pages, int Versions, int Attachments,
        int Comments, int Templates, int Labels, string? Source,
        int SpaceRestrictions, int PageRestrictions, IReadOnlyList<string> Authors);

    private static async Task<IResult> Import(
        HttpRequest request, AppDbContext db, CurrentUser current, IAttachmentStorage storage,
        IProfileMediaService media, IAuditLogger audit, CancellationToken ct)
    {
        if (!request.HasFormContentType)
            return Problem("file", "Upload a pack file.");

        var form = await request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
            return Problem("file", "Upload a pack file.");

        var key = (form["key"].ToString() ?? "").Trim().ToUpperInvariant();
        if (!KeyPattern().IsMatch(key))
            return Problem("key",
                "Key must be 2–50 characters, start with a letter, and contain only letters and digits.");
        if (await db.Spaces.AnyAsync(s => s.Key == key, ct))
            return Results.Conflict(new { message = $"A space with key '{key}' already exists." });

        if (file.Length > WikiPack.MaxTotalBytes)
            return Problem("file", "That file is larger than a pack may be.");

        // Spooled to a temporary file rather than held in memory: a pack is
        // read twice, once for the model and once for the bytes, so it has to
        // be seekable, and half a gigabyte of somebody else's zip is not
        // something to keep in the heap while it is parsed. DeleteOnClose ties
        // the copy's life to this request, including when it throws.
        await using var buffer = new FileStream(
            Path.Combine(Path.GetTempPath(), $"tesria-import-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 64 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await using (var upload = file.OpenReadStream())
            await upload.CopyToAsync(buffer, ct);

        WikiPack.Model model;
        try
        {
            buffer.Position = 0;
            model = WikiPack.Read(buffer);
        }
        catch (WikiPack.PackException ex)
        {
            return Problem("file", ex.Message);
        }
        catch (InvalidDataException)
        {
            return Problem("file", "That file is not a Tesria pack.");
        }

        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);

        var name = (form["name"].ToString() ?? "").Trim();
        if (name.Length == 0) name = model.Space.Name;
        name = Clip(name, 200);

        // Written before the transaction commits and swept up if it does not.
        // Storage is a filesystem and cannot join a database transaction, so
        // the reconciliation is this list and the catch below.
        var written = new List<string>();
        var user = current.RequireId();
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var space = new Space
            {
                Id = Guid.NewGuid(),
                Key = key,
                Name = name,
                Description = model.Space.Description,
                // Not from the pack, deliberately: publishing is 5.5's
                // two-step opt-in and a zip file does not get to take it.
                IsPublic = false,
                Archived = false,
                CreatedById = user,
                CreatedAt = now,
            };
            // An id is a join key inside a pack, so the same one twice means
            // the pack contradicts itself and there is no honest way to guess
            // which row was meant. Refused, rather than quietly deduplicated,
            // and refused here: before this, nothing has been written anywhere.
            if (!TryMint(model.Pages.Select(p => p.Id), out var pageIds))
                return Problem("file", "This pack lists the same page twice.");
            if (!TryMint(model.Pages.SelectMany(p => p.Attachments ?? []).Select(a => a.Id), out var attachmentIds))
                return Problem("file", "This pack lists the same attachment twice.");
            if (!TryMint(model.Pages.SelectMany(p => p.Comments ?? []).Select(c => c.Id), out var commentIds))
                return Problem("file", "This pack lists the same comment twice.");
            var maps = new PackRewriter.Maps(key, pageIds, attachmentIds, commentIds);

            await ApplyIconAsync(space, model, zip, media, written, ct);
            db.Spaces.Add(space);

            var versions = 0;
            var position = 0;
            // A page points at its current version and a version points at its
            // page, so the two cannot be inserted in one statement batch. They
            // are paired up here and joined after the first save, which is the
            // same two-step the ordinary page-create path takes.
            var currents = new List<(Page Page, PageVersion Version)>();
            foreach (var packed in model.Pages)
            {
                var page = new Page
                {
                    Id = pageIds[packed.Id],
                    SpaceId = space.Id,
                    ParentPageId = maps.Page(packed.Parent),
                    Title = Clip(packed.Title, 500),
                    FullWidth = packed.FullWidth,
                    // Renormalised rather than carried: the pack is in tree
                    // order, so counting is more trustworthy than a number
                    // that was only ever relative to pages that may not have
                    // travelled.
                    Position = position++,
                    Status = PageStatus.Current,
                    CreatedById = user,
                    CreatedAt = packed.CreatedAt,
                    UpdatedAt = packed.CreatedAt,
                    SearchText = string.Empty,
                };
                db.Pages.Add(page);

                PageVersion? currentVersion = null;
                var number = 0;
                foreach (var packedVersion in (packed.Versions ?? []).OrderBy(v => v.CreatedAt).ThenBy(v => v.Number))
                {
                    var rewritten = PackRewriter.Rewrite(packedVersion.Content, maps);
                    // The one door every stored page goes through: it
                    // validates the shape and, since 8.6, strips tracked
                    // changes. A pack cannot smuggle either past it.
                    if (!PageContent.TryNormalize(rewritten.ToJsonString(), out var content))
                        return Problem("file", $"A page in this pack could not be read: '{Describe(packed.Title)}'.");

                    var version = new PageVersion
                    {
                        Id = Guid.NewGuid(),
                        PageId = page.Id,
                        VersionNumber = ++number,
                        ContentJson = content,
                        AuthorId = user,
                        ChangeComment = Clip(packedVersion.Comment, 500),
                        CreatedAt = packedVersion.CreatedAt,
                    };
                    db.PageVersions.Add(version);
                    currentVersion = version;
                    versions++;
                }

                if (currentVersion is null)
                    return Problem("file", $"A page in this pack has no content: '{Describe(packed.Title)}'.");

                currents.Add((page, currentVersion));
                page.UpdatedAt = currentVersion.CreatedAt;
                page.SearchText = PageContent.BuildSearchText(page.Title, currentVersion.ContentJson);

                foreach (var label in packed.Labels ?? [])
                    await AddLabelAsync(db, page.Id, label, user, now, ct);

                foreach (var packedAttachment in packed.Attachments ?? [])
                    await AddAttachmentAsync(
                        db, zip, storage, written, page.Id,
                        attachmentIds[packedAttachment.Id], packedAttachment, user, ct);

                foreach (var packedComment in packed.Comments ?? [])
                {
                    db.Comments.Add(new Comment
                    {
                        Id = commentIds[packedComment.Id],
                        PageId = page.Id,
                        ParentCommentId = packedComment.Parent is { } parent
                            && commentIds.TryGetValue(parent, out var mapped) ? mapped : null,
                        Body = PackRewriter.RewriteText(packedComment.Body, maps),
                        AnchorJson = packedComment.Anchor,
                        AuthorId = user,
                        CreatedAt = packedComment.CreatedAt,
                        UpdatedAt = packedComment.UpdatedAt,
                        DeletedAt = packedComment.DeletedAt,
                    });
                }
            }

            // The first of the two saves: rows only, no cycle in them yet.
            await db.SaveChangesAsync(ct);
            foreach (var (page, version) in currents) page.CurrentVersionId = version.Id;

            // After the pages, because a homepage is one of them.
            space.HomepageId = maps.Page(model.Space.Homepage);

            foreach (var template in model.Space.Templates ?? [])
            {
                if (!PageContent.TryNormalize(template.Content, out var content))
                    return Problem("file", $"A template in this pack could not be read: '{Describe(template.Name)}'.");
                db.PageTemplates.Add(new PageTemplate
                {
                    Id = Guid.NewGuid(),
                    SpaceId = space.Id,
                    Name = Clip(template.Name, 200),
                    Description = Clip(template.Description, 500),
                    ContentJson = content,
                    CreatedById = user,
                    CreatedAt = now,
                });
            }

            var authors = model.Authors.Values.Select(a => a.DisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Order(StringComparer.OrdinalIgnoreCase).ToList();

            // The record of who actually wrote this, which the rows no longer
            // carry. The audit log is where it survives.
            audit.Record("space.imported", "space", space.Id, new
            {
                space.Key,
                Source = model.Manifest.Source,
                model.Manifest.Format,
                Pages = model.Pages.Count,
                Versions = versions,
                model.Manifest.Restrictions,
                Authors = authors,
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            committed = true;

            var labels = model.Pages.SelectMany(p => p.Labels ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return Results.Created($"/api/spaces/{space.Key}", new ImportResponse(
                space.Key, space.Name, model.Pages.Count, versions,
                attachmentIds.Count, commentIds.Count, (model.Space.Templates ?? []).Count, labels,
                model.Manifest.Source,
                model.Manifest.Restrictions.Space, model.Manifest.Restrictions.Pages,
                authors));
        }
        finally
        {
            // Everything that is not a commit ends here: a thrown exception, a
            // cancelled request, and the validation failures that return from
            // inside the transaction once rows have already been added. All
            // three have to undo the same two things, so they say so once.
            if (!committed)
            {
                try { await tx.RollbackAsync(CancellationToken.None); }
                catch { /* the connection is already gone; the transaction with it */ }
                Sweep(storage, media, written);
            }
        }
    }

    private static async Task ApplyIconAsync(
        Space space, WikiPack.Model model, ZipArchive zip, IProfileMediaService media,
        List<string> written, CancellationToken ct)
    {
        var icon = model.Space.Icon;
        space.IconKind = Enum.IsDefined(typeof(SpaceIconKind), icon.Kind)
            ? (SpaceIconKind)icon.Kind
            : SpaceIconKind.None;
        space.IconColor = icon.Color;

        if (space.IconKind != SpaceIconKind.Image)
        {
            space.IconValue = icon.Value;
            return;
        }

        var entry = icon.File is null ? null : zip.GetEntry(icon.File);
        if (entry is null)
        {
            // The manifest said there was a picture and there is not. The
            // space keeps its initials rather than the import failing over
            // decoration.
            space.IconKind = SpaceIconKind.None;
            space.IconValue = null;
            return;
        }

        try
        {
            await using var bytes = entry.Open();
            // Re-encoded by the same service an upload goes through, so an
            // imported icon is no more trusted than one somebody chose.
            var stored = await media.StoreAsync(ProfileMediaKind.SpaceIcon, space.Id, bytes, ct);
            written.Add(media.KeyFor(ProfileMediaKind.SpaceIcon, space.Id));
            space.IconValue = stored.ContentHash;
        }
        catch (ProfileMediaException)
        {
            space.IconKind = SpaceIconKind.None;
            space.IconValue = null;
        }
    }

    private static async Task AddLabelAsync(
        AppDbContext db, Guid pageId, string raw, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        var name = (raw ?? "").Trim().ToLowerInvariant();
        if (!LabelPattern().IsMatch(name)) return;

        // Labels are instance-wide, so this is find-or-create by name, the
        // same as adding one by hand. Local tracking matters: several pages in
        // one import can introduce the same new label before any of it is saved.
        var label = db.Labels.Local.FirstOrDefault(l => l.Name == name)
            ?? await db.Labels.FirstOrDefaultAsync(l => l.Name == name, ct);
        if (label is null)
        {
            label = new Label { Id = Guid.NewGuid(), Name = name, CreatedAt = now };
            db.Labels.Add(label);
        }

        db.PageLabels.Add(new PageLabel
        {
            PageId = pageId,
            LabelId = label.Id,
            AddedById = user,
            AddedAt = now,
        });
    }

    private static async Task AddAttachmentAsync(
        AppDbContext db, ZipArchive zip, IAttachmentStorage storage, List<string> written,
        Guid pageId, Guid id, WikiPack.PackAttachment packed, Guid user, CancellationToken ct)
    {
        var entry = zip.GetEntry(packed.File)
            ?? throw new WikiPack.PackException($"This pack refers to a file it does not contain: {Describe(packed.File)}");

        var storageKey = Guid.NewGuid().ToString("N");
        var head = new byte[16];
        int headLength;
        await using (var peek = entry.Open())
            headLength = await peek.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);

        await using (var bytes = entry.Open())
            await storage.SaveAsync(storageKey, bytes, ct);
        written.Add(storageKey);

        db.Attachments.Add(new Attachment
        {
            Id = id,
            PageId = pageId,
            // GetFileName is the guard, not the tidying: a pack may name a
            // file "../../etc/passwd" and the storage key is minted here
            // anyway, so this is belt and braces on the display name.
            Filename = Clip(Path.GetFileName(packed.Filename ?? ""), 500) is { Length: > 0 } clean
                ? clean : "file",
            // From the bytes, never from the manifest: the declared type is
            // the one thing an attacker controls for free.
            ContentType = ContentTypes.Resolve(head.AsSpan(0, headLength), packed.ContentType),
            Size = entry.Length,
            StorageKey = storageKey,
            UploadedById = user,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>
    /// Best effort, and deliberately silent: the import has already failed,
    /// and an orphaned file is a cleanup job rather than a second error to
    /// show somebody who is looking at the first one.
    /// </summary>
    private static void Sweep(IAttachmentStorage storage, IProfileMediaService media, List<string> written)
    {
        foreach (var key in written)
        {
            try { storage.Delete(key); } catch { /* nothing left to do about it */ }
            try { media.Delete(key); } catch { /* ditto */ }
        }
        written.Clear();
    }

    /// <summary>
    /// Text out of a pack meets columns that have lengths. Cut rather than
    /// refused: an over-long title is a nuisance, and losing a page over one
    /// would be a worse answer than a shortened heading.
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    private static string? Clip(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max];

    /// <summary>
    /// A new id for each of the pack's, or false if it used one twice.
    /// </summary>
    private static bool TryMint(IEnumerable<Guid> ids, out Dictionary<Guid, Guid> minted)
    {
        minted = [];
        foreach (var id in ids)
        {
            if (!minted.TryAdd(id, Guid.NewGuid())) return false;
        }
        return true;
    }

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Names from a pack reach error messages, so they are declawed first.</summary>
    private static string Describe(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).Take(80).ToArray());
        return clean.Length == 0 ? "(unnamed)" : clean;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,49}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,49}$")]
    private static partial Regex LabelPattern();
}
