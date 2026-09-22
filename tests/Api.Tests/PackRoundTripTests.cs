using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Tesria.Api.Domain;
using Tesria.Api.Features.Export;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// A pack through HTTP and back (dev-plan 8.5 step 5).
///
/// <para>Everything below this line is covered by a unit test somewhere: the
/// format round-trips, the rewriter rewrites. What is only true end to end is
/// that the export and the import agree with each other, and that the things
/// the design says do <i>not</i> travel actually do not. Those are the two
/// ways this feature fails in a way nobody notices until it matters.</para>
///
/// <para>The import runs as a different user on purpose. Attribution and
/// permissions are the parts a same-user test would pass without proving
/// anything.</para>
/// </summary>
public class PackRoundTripTests
{
    private const string Plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"%TEXT%"}]}]}""";

    private static string Doc(string text) => Plain.Replace("%TEXT%", text);

    private record PageDto(Guid Id, string Title, Guid SpaceId, Guid? ParentPageId, int Version, string ContentJson);
    private record CommentDto(Guid Id, Guid? ParentCommentId, string Body);
    private record AttachmentDto(Guid Id, string Filename);
    private record TemplateDto(Guid Id, string Name);
    private record SpaceDto(Guid Id, string Key, string Name, bool IsPublic, bool Archived);
    private record ImportDto(
        string Key, string Name, int Pages, int Versions, int Attachments, int Comments,
        int Templates, int Labels, string? Source, int SpaceRestrictions, int PageRestrictions,
        string[] Authors);

    /// <summary>
    /// A space with one of everything the format claims to carry, so the
    /// round trip is tested against a space rather than a paragraph.
    /// </summary>
    private static async Task<(Guid SpaceId, string Key, Guid Parent, Guid Child, Guid Attachment)>
        SeedAsync(HttpClient client)
    {
        var spaceId = await client.CreateSpaceAsync("SRC");

        var parent = await CreatePageAsync(client, spaceId, null, "Handbook", Doc("the first thing"));
        var child = await CreatePageAsync(client, spaceId, parent.Id, "Onboarding", Doc("the second thing"));

        // A second version, so history has something to be.
        var edit = await client.PutAsJsonAsync($"/api/pages/{parent.Id}", new
        {
            ContentJson = Doc("the first thing, revised"),
            ChangeComment = "tightened it",
        });
        edit.EnsureSuccessStatusCode();

        // A link from one page in the pack to another, which is the rewrite
        // that matters most and the one nothing else here would catch.
        var linked = """
            {"type":"doc","content":[{"type":"paragraph","content":[
              {"type":"text","text":"see onboarding","marks":[{"type":"link","attrs":{"href":"/spaces/SRC/pages/%CHILD%"}}]}]}]}
            """.Replace("%CHILD%", child.Id.ToString());
        (await client.PutAsJsonAsync($"/api/pages/{parent.Id}", new { ContentJson = linked }))
            .EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync($"/api/pages/{parent.Id}/labels", new { Name = "handbook" }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/pages/{child.Id}/labels", new { Name = "handbook" }))
            .EnsureSuccessStatusCode();

        var attachment = await UploadAsync(client, child.Id, "notes.txt", "the bytes that must survive");

        var thread = await client.PostAsJsonAsync($"/api/pages/{parent.Id}/comments",
            new { Body = "does this still hold?", ParentCommentId = (Guid?)null, AnchorJson = (string?)null });
        thread.EnsureSuccessStatusCode();
        var top = (await thread.Content.ReadFromJsonAsync<CommentDto>())!;
        (await client.PostAsJsonAsync($"/api/pages/{parent.Id}/comments",
            new { Body = "it does", ParentCommentId = (Guid?)top.Id, AnchorJson = (string?)null }))
            .EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/templates", new
        {
            SpaceId = spaceId,
            Name = "Meeting notes",
            Description = "The usual shape",
            ContentJson = Doc("Attendees:"),
        })).EnsureSuccessStatusCode();

        return (spaceId, "SRC", parent.Id, child.Id, attachment.Id);
    }

    private static async Task<PageDto> CreatePageAsync(
        HttpClient client, Guid spaceId, Guid? parent, string title, string content)
    {
        var res = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = content });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PageDto>())!;
    }

    private static async Task<AttachmentDto> UploadAsync(
        HttpClient client, Guid pageId, string filename, string body)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(bytes, "file", filename);
        var res = await client.PostAsync($"/api/pages/{pageId}/attachments", form);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AttachmentDto>())!;
    }

    private static async Task<byte[]> ExportAsync(HttpClient client, string key)
    {
        var res = await client.GetAsync($"/api/spaces/{key}/export/pack");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync();
    }

    private static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client, byte[] pack, string key, string? name = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pack);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "pack.zip");
        form.Add(new StringContent(key), "key");
        if (name is not null) form.Add(new StringContent(name), "name");
        return await client.PostAsync("/api/spaces/import", form);
    }

    // --- The round trip.

    [Fact]
    public async Task A_space_survives_an_export_and_an_import_by_somebody_else()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        var importer = app.CreateClient();
        var authorId = await author.RegisterAndSignInAsync();
        var importerId = await importer.RegisterAndSignInAsync();

        await SeedAsync(author);
        var pack = await ExportAsync(author, "SRC");

        var res = await ImportAsync(importer, pack, "DEST");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var result = (await res.Content.ReadFromJsonAsync<ImportDto>())!;

        Assert.Equal("DEST", result.Key);
        Assert.Equal(2, result.Pages);
        Assert.Equal(1, result.Attachments);
        Assert.Equal(2, result.Comments);
        Assert.Equal(1, result.Templates);
        Assert.Equal(1, result.Labels);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var space = await db.Spaces.SingleAsync(s => s.Key == "DEST");

        // The tree, by shape rather than by id.
        var pages = await db.Pages.Where(p => p.SpaceId == space.Id).ToListAsync();
        Assert.Equal(2, pages.Count);
        var parent = pages.Single(p => p.Title == "Handbook");
        var child = pages.Single(p => p.Title == "Onboarding");
        Assert.Null(parent.ParentPageId);
        Assert.Equal(parent.Id, child.ParentPageId);

        // History: three versions on the parent, renumbered from one.
        var versions = await db.PageVersions.Where(v => v.PageId == parent.Id)
            .OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal([1, 2, 3], versions.Select(v => v.VersionNumber));
        Assert.Contains("the first thing", versions[0].ContentJson);
        Assert.Equal("tightened it", versions[1].ChangeComment);

        // Attribution is the importer's, all the way down, and the original
        // author is nowhere in the rows.
        Assert.Equal(importerId, space.CreatedById);
        Assert.All(pages, p => Assert.Equal(importerId, p.CreatedById));
        Assert.All(versions, v => Assert.Equal(importerId, v.AuthorId));
        Assert.DoesNotContain(await db.Comments.Where(c => c.PageId == parent.Id).ToListAsync(),
            c => c.AuthorId == authorId);

        // Comments, as a thread rather than as two rows.
        var comments = await db.Comments.Where(c => c.PageId == parent.Id).ToListAsync();
        Assert.Equal(2, comments.Count);
        var top = comments.Single(c => c.ParentCommentId is null);
        var reply = comments.Single(c => c.ParentCommentId is not null);
        Assert.Equal(top.Id, reply.ParentCommentId);
        Assert.Equal("it does", reply.Body);

        // Labels found the existing instance-wide label rather than making a
        // second one with the same name.
        Assert.Equal(1, await db.Labels.CountAsync(l => l.Name == "handbook"));
        Assert.Equal(2, await db.PageLabels.CountAsync(pl => pages.Select(p => p.Id).Contains(pl.PageId)));

        var template = await db.PageTemplates.SingleAsync(t => t.SpaceId == space.Id);
        Assert.Equal("Meeting notes", template.Name);
        Assert.Equal(importerId, template.CreatedById);
    }

    [Fact]
    public async Task An_imported_space_is_private_and_carries_no_permissions()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        var importer = app.CreateClient();
        await author.RegisterAndSignInAsync();
        await importer.RegisterAndSignInAsync();

        await SeedAsync(author);
        // Published at the source, so "private on arrival" is a decision the
        // import made rather than a value it happened to copy.
        (await author.PostAsJsonAsync("/api/spaces/SRC/publish", new { Confirm = true }))
            .Dispose();

        var pack = await ExportAsync(author, "SRC");
        (await ImportAsync(importer, pack, "DEST")).EnsureSuccessStatusCode();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var space = await db.Spaces.SingleAsync(s => s.Key == "DEST");

        Assert.False(space.IsPublic);
        Assert.False(space.Archived);
        Assert.Null(space.PublicSince);
        Assert.Equal(0, await db.SpacePermissions.CountAsync(p => p.SpaceId == space.Id));
        Assert.Equal(0, await db.PageRestrictions.CountAsync(
            r => db.Pages.Where(p => p.SpaceId == space.Id).Select(p => p.Id).Contains(r.PageId)));
    }

    [Fact]
    public async Task A_link_between_two_pages_in_the_pack_points_at_the_new_ones()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        var importer = app.CreateClient();
        await author.RegisterAndSignInAsync();
        await importer.RegisterAndSignInAsync();

        var seeded = await SeedAsync(author);
        var pack = await ExportAsync(author, "SRC");
        (await ImportAsync(importer, pack, "DEST")).EnsureSuccessStatusCode();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var space = await db.Spaces.SingleAsync(s => s.Key == "DEST");
        var parent = await db.Pages.SingleAsync(p => p.SpaceId == space.Id && p.Title == "Handbook");
        var child = await db.Pages.SingleAsync(p => p.SpaceId == space.Id && p.Title == "Onboarding");
        var current = await db.PageVersions.SingleAsync(v => v.Id == parent.CurrentVersionId);

        Assert.Contains($"/spaces/DEST/pages/{child.Id}", current.ContentJson);
        // The source instance's ids are gone, not merely accompanied.
        Assert.DoesNotContain(seeded.Child.ToString(), current.ContentJson);
        Assert.DoesNotContain("/spaces/SRC/", current.ContentJson);
    }

    [Fact]
    public async Task An_attachment_arrives_byte_for_byte_under_a_new_id()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        var importer = app.CreateClient();
        await author.RegisterAndSignInAsync();
        await importer.RegisterAndSignInAsync();

        var seeded = await SeedAsync(author);
        var pack = await ExportAsync(author, "SRC");
        (await ImportAsync(importer, pack, "DEST")).EnsureSuccessStatusCode();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var space = await db.Spaces.SingleAsync(s => s.Key == "DEST");
        var child = await db.Pages.SingleAsync(p => p.SpaceId == space.Id && p.Title == "Onboarding");
        var attachment = await db.Attachments.SingleAsync(a => a.PageId == child.Id);

        Assert.NotEqual(seeded.Attachment, attachment.Id);
        Assert.Equal("notes.txt", attachment.Filename);

        var download = await importer.GetAsync($"/api/attachments/{attachment.Id}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("the bytes that must survive", await download.Content.ReadAsStringAsync());
    }

    // --- Refusals.

    [Fact]
    public async Task The_same_key_twice_is_a_conflict_rather_than_a_merge()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        await author.RegisterAndSignInAsync();

        await SeedAsync(author);
        var pack = await ExportAsync(author, "SRC");

        Assert.Equal(HttpStatusCode.Created, (await ImportAsync(author, pack, "DEST")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ImportAsync(author, pack, "DEST")).StatusCode);
    }

    [Fact]
    public async Task The_same_pack_under_a_second_key_is_a_second_space()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        await author.RegisterAndSignInAsync();

        await SeedAsync(author);
        var pack = await ExportAsync(author, "SRC");
        (await ImportAsync(author, pack, "ONE")).EnsureSuccessStatusCode();
        (await ImportAsync(author, pack, "TWO")).EnsureSuccessStatusCode();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var one = await db.Spaces.SingleAsync(s => s.Key == "ONE");
        var two = await db.Spaces.SingleAsync(s => s.Key == "TWO");

        Assert.NotEqual(one.Id, two.Id);
        Assert.Equal(2, await db.Pages.CountAsync(p => p.SpaceId == one.Id));
        Assert.Equal(2, await db.Pages.CountAsync(p => p.SpaceId == two.Id));
    }

    [Fact]
    public async Task A_pack_from_a_newer_Tesria_is_refused_in_words()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        await author.RegisterAndSignInAsync();

        await SeedAsync(author);
        var pack = Repack(await ExportAsync(author, "SRC"),
            WikiPack.ManifestEntry, json => json.Replace("\"format\": 1", "\"format\": 2"));

        var res = await ImportAsync(author, pack, "DEST");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("newer version", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_zip_with_a_path_that_escapes_the_pack_is_refused()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        await author.RegisterAndSignInAsync();

        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("../../etc/passwd").Open());
            await writer.WriteAsync("nope");
        }

        var res = await ImportAsync(author, buffer.ToArray(), "DEST");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        // And nothing was created on the way to refusing it.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Spaces.AnyAsync(s => s.Key == "DEST"));
    }

    [Fact]
    public async Task Something_that_is_not_a_zip_at_all_is_refused_politely()
    {
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        await author.RegisterAndSignInAsync();

        var res = await ImportAsync(author, Encoding.UTF8.GetBytes("this is a text file"), "DEST");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not a Tesria pack", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_page_the_exporter_cannot_see_is_left_out_and_counted()
    {
        await using var app = new TestAppFactory();
        var owner = app.CreateClient();
        var other = app.CreateClient();
        await owner.RegisterAndSignInAsync();
        await other.RegisterAndSignInAsync();

        var seeded = await SeedAsync(owner);

        // The child is restricted to the owner, so the other member can see
        // the space and the parent but not the child.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var space = await db.Spaces.SingleAsync(s => s.Key == "SRC");
            db.PageRestrictions.Add(new PageRestriction
            {
                Id = Guid.NewGuid(),
                PageId = seeded.Child,
                PrincipalType = PrincipalType.User,
                PrincipalId = space.CreatedById,
                Operation = PageOperation.View,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var theirs = WikiPack.Read(new MemoryStream(await ExportAsync(other, "SRC")));

        // This is the whole reason the export walks pages one at a time
        // instead of taking the space: a pack is a file that leaves the
        // building, and it must not contain a page its author could not open.
        Assert.Equal(1, theirs.Manifest.Counts.Pages);
        Assert.Equal(1, theirs.Manifest.Omitted.Pages);
        Assert.DoesNotContain(theirs.Pages, p => p.Title == "Onboarding");
        Assert.DoesNotContain("the second thing", Encoding.UTF8.GetString(await ExportAsync(other, "SRC")));

        // The owner's own export still has both, so the omission is about who
        // asked rather than about the page.
        var mine = WikiPack.Read(new MemoryStream(await ExportAsync(owner, "SRC")));
        Assert.Equal(2, mine.Manifest.Counts.Pages);
        Assert.Equal(0, mine.Manifest.Omitted.Pages);
    }

    [Fact]
    public async Task The_manifest_says_restrictions_existed_without_saying_what_they_were()
    {
        await using var app = new TestAppFactory();
        var owner = app.CreateClient();
        var importer = app.CreateClient();
        await owner.RegisterAndSignInAsync();
        await importer.RegisterAndSignInAsync();

        var seeded = await SeedAsync(owner);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var them = await db.Users.FirstAsync();
            db.PageRestrictions.Add(new PageRestriction
            {
                Id = Guid.NewGuid(),
                PageId = seeded.Child,
                PrincipalType = PrincipalType.User,
                PrincipalId = them.Id,
                Operation = PageOperation.View,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var pack = await ExportAsync(owner, "SRC");
        var res = await ImportAsync(importer, pack, "DEST");
        var result = (await res.Content.ReadFromJsonAsync<ImportDto>())!;

        // The number, so the importer knows to go and set them again; never
        // the principals, which are ids on an instance this one has not met.
        Assert.Equal(1, result.PageRestrictions);
        var text = Encoding.UTF8.GetString(pack);
        Assert.DoesNotContain("PageRestriction", text);
    }

    [Fact]
    public async Task Signing_in_is_required_to_import()
    {
        await using var app = new TestAppFactory();
        var anonymous = app.CreateClient();

        var res = await ImportAsync(anonymous, [1, 2, 3], "DEST");

        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected a refusal, got {res.StatusCode}");
    }

    [Fact]
    public async Task A_pack_unzipped_and_zipped_up_again_still_imports()
    {
        // This is 10.5's actual workflow: the manual's pack is committed
        // unzipped so a page edit diffs as a page edit, and a script zips it
        // back to import it. Every zip tool writes a directory entry for each
        // folder, which this writer never does, so nothing else here produces
        // one and the format refused the workflow it exists for.
        await using var app = new TestAppFactory();
        var author = app.CreateClient();
        await author.RegisterAndSignInAsync();

        await SeedAsync(author);
        var pack = WithDirectoryEntries(await ExportAsync(author, "SRC"));

        var res = await ImportAsync(author, pack, "DEST");

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var result = (await res.Content.ReadFromJsonAsync<ImportDto>())!;
        Assert.Equal(2, result.Pages);
        Assert.Equal(1, result.Attachments);
    }

    /// <summary>Rebuilds a pack the way `zip -r` would, folder entries and all.</summary>
    private static byte[] WithDirectoryEntries(byte[] pack)
    {
        using var source = new System.IO.Compression.ZipArchive(
            new MemoryStream(pack), System.IO.Compression.ZipArchiveMode.Read);
        using var buffer = new MemoryStream();
        using (var target = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var folder in source.Entries
                         .Select(e => e.FullName)
                         .Where(n => n.Contains('/'))
                         .Select(n => n[..(n.IndexOf('/') + 1)])
                         .Distinct())
                target.CreateEntry(folder);

            foreach (var original in source.Entries)
            {
                using var from = original.Open();
                using var to = target.CreateEntry(original.FullName).Open();
                from.CopyTo(to);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>Rewrites one entry of a pack, to make a pack that is wrong in exactly one way.</summary>
    private static byte[] Repack(byte[] pack, string entry, Func<string, string> edit)
    {
        using var source = new System.IO.Compression.ZipArchive(
            new MemoryStream(pack), System.IO.Compression.ZipArchiveMode.Read);
        using var buffer = new MemoryStream();
        using (var target = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var original in source.Entries)
            {
                using var reader = new StreamReader(original.Open());
                var text = reader.ReadToEnd();
                using var writer = new StreamWriter(target.CreateEntry(original.FullName).Open());
                writer.Write(original.FullName == entry ? edit(text) : text);
            }
        }
        return buffer.ToArray();
    }
}
