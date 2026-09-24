using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The pack format itself (dev-plan 8.5, step 1).
///
/// Two jobs, and they pull in opposite directions. Writing must be
/// deterministic, because the manual's pack is committed and a one-word edit
/// has to diff as one word. Reading must be suspicious, because a pack
/// arrives as an upload from somewhere else entirely.
/// </summary>
public class WikiPackTests
{
    private static JsonNode Doc(string text) =>
        JsonNode.Parse($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""")!;

    private static readonly Guid PageOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PageTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Author = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FileId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateTimeOffset When = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static WikiPack.Model Sample(bool withAttachment = false)
    {
        var attachments = withAttachment
            ? new List<WikiPack.PackAttachment>
            {
                new(FileId, "diagram.png", "image/png", 4, WikiPack.AttachmentEntry(FileId)),
            }
            : [];

        return new WikiPack.Model(
            new WikiPack.Manifest(
                WikiPack.Format, "Tesria", When, "Example",
                new WikiPack.ManifestSpace("DOCS", "Documentation"),
                new WikiPack.Counts(2, 2, attachments.Count, 1, 1, 1),
                new WikiPack.Omitted(0),
                new WikiPack.Restrictions(0, 0)),
            new WikiPack.PackSpace(
                "DOCS", "Documentation", "A space", PageOne,
                new WikiPack.PackIcon(0, null, null, null),
                [new WikiPack.PackTemplate("Meeting", "Notes", Doc("template").ToJsonString())]),
            new Dictionary<Guid, WikiPack.Author> { [Author] = new("Ada") },
            [
                new WikiPack.PackPage(
                    PageOne, null, 0, "Home", false, When, Author, 1,
                    [new WikiPack.PackVersion(1, When, Author, null, Doc("home"))],
                    ["guide"], attachments,
                    [new WikiPack.PackComment(Guid.Empty, null, Author, "Nice", null, When, When, null)]),
                new WikiPack.PackPage(
                    PageTwo, PageOne, 0, "Child", true, When, Author, 1,
                    [new WikiPack.PackVersion(1, When, Author, "why", Doc("child"))],
                    [], [], []),
            ]);
    }

    private static Task<Stream?> NoFiles(string _, CancellationToken __) => Task.FromResult<Stream?>(null);

    private static Task<Stream?> OneFile(string name, CancellationToken _) =>
        Task.FromResult<Stream?>(name == WikiPack.AttachmentEntry(FileId)
            ? new MemoryStream("abcd"u8.ToArray())
            : null);

    private static async Task<byte[]> Write(WikiPack.Model model, Func<string, CancellationToken, Task<Stream?>>? files = null)
    {
        var buffer = new MemoryStream();
        await WikiPack.WriteAsync(buffer, model, files ?? NoFiles);
        return buffer.ToArray();
    }

    [Fact]
    public async Task A_pack_round_trips()
    {
        var model = Sample();

        var read = WikiPack.Read(new MemoryStream(await Write(model)));

        Assert.Equal("DOCS", read.Space.Key);
        Assert.Equal("Documentation", read.Space.Name);
        Assert.Equal(PageOne, read.Space.Homepage);
        Assert.Equal(2, read.Pages.Count);
        Assert.Equal("Home", read.Pages[0].Title);
        Assert.Equal(PageOne, read.Pages[1].Parent);
        Assert.True(read.Pages[1].FullWidth);
        Assert.Equal("Ada", read.Authors[Author].DisplayName);
        Assert.Equal("guide", Assert.Single(read.Pages[0].Labels));
        Assert.Equal("Meeting", Assert.Single(read.Space.Templates).Name);
        Assert.Equal("why", read.Pages[1].Versions[0].Comment);
    }

    [Fact]
    public async Task The_document_survives_exactly()
    {
        // The whole point of the file. Anything less than byte-equality here
        // means an export quietly rewrites people's pages.
        var model = Sample();

        var read = WikiPack.Read(new MemoryStream(await Write(model)));

        Assert.Equal(
            Doc("home").ToJsonString(),
            read.Pages[0].Versions[0].Content.ToJsonString());
    }

    [Fact]
    public async Task Writing_the_same_model_twice_gives_the_same_bytes()
    {
        // Why this matters: dev-plan 10.5 commits the manual's pack, so an
        // export of an unchanged space must produce an unchanged file. Zip
        // entry timestamps are the usual reason this fails.
        var model = Sample(withAttachment: true);

        var first = await Write(model, OneFile);
        await Task.Delay(1100); // long enough for a DOS timestamp (2s resolution) to tick
        var second = await Write(model, OneFile);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_one_word_edit_changes_one_page_file()
    {
        // The other half of the same promise: the diff is proportionate.
        var before = await Write(Sample());
        var model = Sample();
        var edited = model with
        {
            Pages =
            [
                model.Pages[0] with { Versions = [model.Pages[0].Versions[0] with { Content = Doc("home, edited") }] },
                model.Pages[1],
            ],
        };

        var after = await Write(edited);

        var changed = ChangedEntries(before, after);
        Assert.Equal(WikiPack.PageEntry(PageOne), Assert.Single(changed));
    }

    private static List<string> ChangedEntries(byte[] before, byte[] after)
    {
        static Dictionary<string, string> Read(byte[] bytes)
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return zip.Entries.ToDictionary(e => e.FullName, e =>
            {
                using var reader = new StreamReader(e.Open());
                return reader.ReadToEnd();
            });
        }
        var a = Read(before);
        var b = Read(after);
        return a.Keys.Union(b.Keys)
            .Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y)
            .ToList();
    }

    [Fact]
    public async Task The_json_is_readable_by_a_person()
    {
        // A committed pack is read and reviewed in a diff, so it is indented,
        // and an apostrophe stays an apostrophe rather than becoming \u0027.
        var model = Sample();
        var edited = model with { Space = model.Space with { Name = "Ada's notes" } };

        using var zip = new ZipArchive(new MemoryStream(await Write(edited)), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(WikiPack.SpaceEntry)!.Open());
        var json = await reader.ReadToEndAsync();

        Assert.Contains("\n", json);
        Assert.Contains("Ada's notes", json);
        Assert.DoesNotContain("\\u0027", json);
    }

    [Fact]
    public async Task Attachment_bytes_travel()
    {
        var read = WikiPack.Read(new MemoryStream(await Write(Sample(withAttachment: true), OneFile)));

        var attachment = Assert.Single(read.Pages[0].Attachments);
        Assert.Equal("diagram.png", attachment.Filename);
        Assert.Equal(WikiPack.AttachmentEntry(FileId), attachment.File);
    }

    /* ---- refusing what should be refused -------------------------------- */

    [Fact]
    public async Task A_pack_from_a_newer_Tesria_is_refused_rather_than_guessed_at()
    {
        var bytes = await Write(Sample());
        var tampered = Retamper(bytes, WikiPack.ManifestEntry, json => json.Replace("\"format\": 1", "\"format\": 2"));

        var ex = Assert.Throws<WikiPack.PackException>(() => WikiPack.Read(new MemoryStream(tampered)));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public async Task A_file_that_is_not_a_pack_is_refused_with_a_sentence_that_helps()
    {
        var empty = new MemoryStream();
        using (var zip = new ZipArchive(empty, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry(WikiPack.SpaceEntry);
        }
        empty.Position = 0;

        var ex = Assert.Throws<WikiPack.PackException>(() => WikiPack.Read(empty));
        Assert.Contains("not a Tesria pack", ex.Message);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/etc/passwd")]
    [InlineData("pages/../../escape.json")]
    [InlineData("pages/not-a-guid.json")]
    [InlineData("attachments/../secret")]
    [InlineData("random.txt")]
    [InlineData("C:\\windows\\system32")]
    public void An_entry_name_outside_the_format_is_refused(string name)
    {
        // An allow-list of the shapes this format defines, so a name nobody
        // thought of fails by default rather than by inspection.
        Assert.False(WikiPack.IsAllowedName(name));
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("space.json")]
    [InlineData("authors.json")]
    [InlineData("space-icon.webp")]
    [InlineData("pages/11111111-1111-1111-1111-111111111111.json")]
    [InlineData("attachments/ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void The_names_the_format_defines_are_allowed(string name) =>
        Assert.True(WikiPack.IsAllowedName(name));

    [Fact]
    public async Task A_zip_slip_entry_is_refused_when_the_pack_is_read()
    {
        var bytes = await Write(Sample());
        var withExtra = AddEntry(bytes, "../escaped.json", "{}");

        var ex = Assert.Throws<WikiPack.PackException>(() => WikiPack.Read(new MemoryStream(withExtra)));
        Assert.Contains("unexpected file", ex.Message);
    }

    [Fact]
    public async Task A_page_claiming_a_file_the_pack_does_not_have_is_refused()
    {
        // Otherwise an import creates an attachment row pointing at nothing.
        var model = Sample(withAttachment: true);

        var ex = Assert.Throws<WikiPack.PackException>(() =>
            WikiPack.Read(new MemoryStream(Write(model, NoFiles).Result)));
        Assert.Contains("does not contain", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Malformed_json_names_the_file_it_was_in()
    {
        var bytes = await Write(Sample());
        var broken = Retamper(bytes, WikiPack.SpaceEntry, _ => "{ not json");

        var ex = Assert.Throws<WikiPack.PackException>(() => WikiPack.Read(new MemoryStream(broken)));
        Assert.Contains("space.json", ex.Message);
    }

    [Fact]
    public async Task A_control_character_in_an_entry_name_cannot_reach_the_message()
    {
        // The name comes from the archive, and the message goes to a person.
        var bytes = await Write(Sample());
        var nasty = AddEntry(bytes, "pages/\u0007bell.json", "{}");

        var ex = Assert.Throws<WikiPack.PackException>(() => WikiPack.Read(new MemoryStream(nasty)));
        Assert.DoesNotContain('\u0007', ex.Message);
    }

    /* ---- the tree ------------------------------------------------------- */

    [Fact]
    public async Task A_page_whose_parent_is_not_in_the_pack_is_lifted_to_the_root()
    {
        var model = Sample();
        var orphaned = model with
        {
            Pages = [model.Pages[0], model.Pages[1] with { Parent = Guid.NewGuid() }],
        };

        var read = WikiPack.Read(new MemoryStream(await Write(orphaned)));

        Assert.Null(read.Pages[1].Parent);
        Assert.Equal(2, read.Pages.Count); // kept, not dropped
    }

    [Fact]
    public async Task A_cycle_in_the_tree_is_broken_rather_than_refused()
    {
        // A cycle cannot be imported at all, and a pack that cannot be
        // imported is worse than one with a page at the root.
        var model = Sample();
        var cyclic = model with
        {
            Pages = [model.Pages[0] with { Parent = PageTwo }, model.Pages[1]],
        };

        var read = WikiPack.Read(new MemoryStream(await Write(cyclic)));

        Assert.Equal(2, read.Pages.Count);
        Assert.Contains(read.Pages, p => p.Parent is null);
        foreach (var page in read.Pages) Assert.NotEqual(page.Id, page.Parent);
    }

    /* ---- helpers -------------------------------------------------------- */

    private static byte[] Retamper(byte[] original, string entry, Func<string, string> edit)
    {
        var buffer = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read))
        using (var target = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var e in source.Entries)
            {
                using var reader = new StreamReader(e.Open());
                var text = reader.ReadToEnd();
                var created = target.CreateEntry(e.FullName);
                using var writer = new StreamWriter(created.Open());
                writer.Write(e.FullName == entry ? edit(text) : text);
            }
        }
        return buffer.ToArray();
    }

    private static byte[] AddEntry(byte[] original, string name, string content)
    {
        var buffer = new MemoryStream(original.Length + 256);
        buffer.Write(original);
        buffer.Position = 0;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return buffer.ToArray();
    }

    /* ---- upgrading an older format (dev-plan 16.3) ----------------------- */

    // Format 1 is the only format there has been, so these pretend the
    // current one is 2 and prove the path a real second format will take.

    private static JsonNode Shout(JsonNode doc) =>
        JsonNode.Parse(doc.ToJsonString().Replace("\"text\":\"home\"", "\"text\":\"HOME\"")
            .Replace("\"text\":\"template\"", "\"text\":\"TEMPLATE\""))!;

    [Fact]
    public async Task An_older_format_is_upgraded_step_by_step_before_it_is_read()
    {
        var steps = new List<PackUpgrade> { new(1, "shout", pack => pack.ForEachDocument(Shout)) };

        var read = WikiPack.Read(new MemoryStream(await Write(Sample())), steps, currentFormat: 2);

        Assert.Equal(2, read.Manifest.Format);
        var home = read.Pages.Single(p => p.Id == PageOne);
        Assert.Contains("HOME", home.Versions.Single().Content.ToJsonString());
        Assert.Contains("TEMPLATE", read.Space.Templates.Single().Content);
        // A page the step had nothing to change in comes through as it was.
        Assert.Contains("child", read.Pages.Single(p => p.Id == PageTwo).Versions.Single().Content.ToJsonString());
    }

    [Fact]
    public async Task A_missing_step_is_refused_in_words()
    {
        var bytes = await Write(Sample());

        var ex = Assert.Throws<WikiPack.PackException>(() =>
            WikiPack.Read(new MemoryStream(bytes), [], currentFormat: 2));
        Assert.Contains("no way to upgrade a pack from format 1", ex.Message);
    }

    [Fact]
    public async Task A_step_that_fails_says_which_step_and_why()
    {
        var steps = new List<PackUpgrade>
        {
            new(1, "renames the callout node", _ => throw new InvalidOperationException("no content")),
        };
        var bytes = await Write(Sample());

        var ex = Assert.Throws<WikiPack.PackException>(() =>
            WikiPack.Read(new MemoryStream(bytes), steps, currentFormat: 2));
        Assert.Contains("from format 1 to 2 (renames the callout node) failed: no content", ex.Message);
    }

    [Fact]
    public async Task A_pack_from_a_newer_format_names_what_made_it()
    {
        var model = Sample();
        var bytes = await Write(model with { Manifest = model.Manifest with { Generator = "Tesria 0.9.0" } });

        var ex = Assert.Throws<WikiPack.PackException>(() =>
            WikiPack.Read(new MemoryStream(bytes), [], currentFormat: 0));
        Assert.Contains("made by Tesria 0.9.0 (pack format 1)", ex.Message);
        Assert.Contains("Upgrade Tesria to import it", ex.Message);
    }

    [Fact]
    public void Every_step_there_is_starts_from_a_format_before_this_one_and_none_is_missing()
    {
        // Guards the list itself: when format 2 arrives, the step from 1
        // must be there too, or every 0.5 pack stops importing.
        var froms = PackUpgrades.All.Select(s => s.From).OrderBy(f => f).ToList();
        Assert.Equal(Enumerable.Range(1, WikiPack.Format - 1), froms);
    }
}
