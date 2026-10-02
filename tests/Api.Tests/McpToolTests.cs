using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Features.Mcp;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The MCP tool surface (dev-plan 8.4). Every tool owes three: the result, a
/// leak test (a target the token's owner cannot see is absent or "not
/// found"), and, for writes, that a read-only token is refused before
/// anything changes.
/// </summary>
public class McpToolTests
{
    private record Created(Guid Id, string Token);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);

    private const int User = 0, View = 0;
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"pineapple notes"}]}]}""";

    private sealed record World(
        TestAppFactory F, HttpClient Alice, HttpClient Bob, Guid AliceId,
        SpaceDto Space, PageDetail Open, PageDetail Secret);

    /// <summary>Alice's space, shared with Bob, holding one open page and one restricted to Alice.</summary>
    private static async Task<World> Build()
    {
        var f = new TestAppFactory();
        var alice = f.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = f.CreateClient();
        await bob.RegisterAndSignInAsync();

        var space = (await (await alice.PostAsJsonAsync("/api/spaces",
            new { Key = "TOOLS", Name = "Tools", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>())!;

        var open = await NewPage(alice, space.Id, "Open plan");
        var secret = await NewPage(alice, space.Id, "Secret pineapple");
        (await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View })).EnsureSuccessStatusCode();

        (await alice.PostAsJsonAsync($"/api/pages/{open.Id}/labels", new { Name = "shared" })).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/labels", new { Name = "shared" })).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/labels", new { Name = "confidential" })).EnsureSuccessStatusCode();

        return new World(f, alice, bob, aliceId, space, open, secret);
    }

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title, Guid? parent = null) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    private static async Task<HttpClient> Mcp(TestAppFactory f, HttpClient session, bool readOnly = false)
    {
        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = "tools", ReadOnly = readOnly }))
            .Content.ReadFromJsonAsync<Created>();
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", created!.Token);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return client;
    }

    private static async Task<JsonElement> Call(HttpClient client, string name, object args)
    {
        var res = await client.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments = args } });
        var body = await res.Content.ReadAsStringAsync();
        if (res.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = string.Join("\n", body.Split('\n')
                .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                .Select(l => l["data:".Length..].Trim()));
        return JsonDocument.Parse(body).RootElement.GetProperty("result").Clone();
    }

    private static JsonElement Ok(JsonElement result)
    {
        Assert.False(result.TryGetProperty("isError", out var e) && e.GetBoolean(),
            result.GetProperty("content")[0].GetProperty("text").GetString());
        return result.TryGetProperty("structuredContent", out var sc)
            ? sc
            : JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private static string Error(JsonElement result)
    {
        Assert.True(result.GetProperty("isError").GetBoolean(), "expected the tool to fail");
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    /// <summary>The tool result's list, whether the SDK wrapped it in an object or not.</summary>
    private static JsonElement Items(JsonElement structured) =>
        structured.ValueKind == JsonValueKind.Array ? structured : structured.GetProperty("result");

    // -- reads -----------------------------------------------------------------

    [Fact]
    public async Task Get_space_tree_omits_a_restricted_page()
    {
        var w = await Build(); using var _ = w.F;
        var child = await NewPage(w.Alice, w.Space.Id, "Below secret", w.Secret.Id);

        var mine = Items(Ok(await Call(await Mcp(w.F, w.Alice), "get_space_tree", new { spaceKey = "TOOLS" })));
        Assert.Equal(["Open plan", "Secret pineapple"], mine.EnumerateArray().Select(n => n.GetProperty("title").GetString()).Order());

        var theirs = await Call(await Mcp(w.F, w.Bob), "get_space_tree", new { spaceKey = "TOOLS" });
        var json = JsonSerializer.Serialize(Ok(theirs));
        Assert.Contains("Open plan", json);
        // A hidden page takes its subtree with it.
        Assert.DoesNotContain("Secret", json);
        Assert.DoesNotContain("Below secret", json);
        GC.KeepAlive(child);
    }

    [Fact]
    public async Task Get_space_tree_masks_a_space_the_caller_cannot_see()
    {
        var w = await Build(); using var _ = w.F;
        var carol = w.F.CreateClient();
        var carolId = await carol.RegisterAndSignInAsync();
        // Lock the space down to Alice only.
        await w.Alice.MakePrivateAsync("TOOLS");

        Assert.Contains("not found", Error(await Call(await Mcp(w.F, carol), "get_space_tree", new { spaceKey = "TOOLS" })));
        GC.KeepAlive(carolId);
    }

    [Fact]
    public async Task Search_never_returns_a_page_the_caller_cannot_read()
    {
        var w = await Build(); using var _ = w.F;

        var mine = Items(Ok(await Call(await Mcp(w.F, w.Alice), "search_pages", new { query = "pineapple" })));
        Assert.Contains(mine.EnumerateArray(), h => h.GetProperty("title").GetString() == "Secret pineapple");

        var theirs = Ok(await Call(await Mcp(w.F, w.Bob), "search_pages", new { query = "pineapple" }));
        // The word is only on the restricted page, so Bob gets nothing at all.
        Assert.DoesNotContain("Secret", JsonSerializer.Serialize(theirs));
    }

    [Fact]
    public async Task Find_pages_by_label_and_list_labels_count_only_what_the_caller_may_read()
    {
        var w = await Build(); using var _ = w.F;

        var mine = Items(Ok(await Call(await Mcp(w.F, w.Alice), "find_pages_by_label", new { label = "shared" })));
        Assert.Equal(2, mine.GetArrayLength());

        var theirs = Items(Ok(await Call(await Mcp(w.F, w.Bob), "find_pages_by_label", new { label = "shared" })));
        Assert.Equal(["Open plan"], theirs.EnumerateArray().Select(p => p.GetProperty("title").GetString()));

        var labels = Items(Ok(await Call(await Mcp(w.F, w.Bob), "list_labels", new { spaceKey = "TOOLS" })));
        var names = labels.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToList();
        // "shared" is on two pages but Bob sees one; "confidential" is only on
        // the restricted page, so it must not appear at all.
        Assert.Equal(["shared"], names);
        Assert.Equal(1, labels.EnumerateArray().Single().GetProperty("pages").GetInt32());
    }

    // -- writes ----------------------------------------------------------------

    [Fact]
    public async Task Create_page_writes_markdown_as_a_real_page()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);

        // Note the ordered list: two `-` lists separated by a blank line are
        // one list in CommonMark, which would make these all task items.
        var markdown = "## Findings\n\nWe should **ship** it, see [the plan](https://example.com).\n\n"
            + "1. first\n2. second\n\n- [x] done\n- [ ] todo\n\n```csharp\nvar x = 1;\n```\n\n"
            + "| Name | Value |\n| --- | --- |\n| a | 1 |\n";
        var created = Ok(await Call(mcp, "create_page", new { spaceKey = "TOOLS", title = "From an assistant", content = markdown }));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(1, created.GetProperty("version").GetInt32());
        Assert.Contains("/spaces/TOOLS/pages/", created.GetProperty("url").GetString());

        // It is a real page: readable over REST, and the same side effects ran.
        var detail = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{id}");
        var json = detail.GetProperty("contentJson").GetString()!;
        Assert.Contains("\"heading\"", json);
        Assert.Contains("\"bold\"", json);
        Assert.Contains("https://example.com", json);
        Assert.Contains("\"taskItem\"", json);
        Assert.Contains("\"csharp\"", json);
        Assert.Contains("\"table\"", json);

        // Round-trips: reading it back gives Markdown again.
        var read = Ok(await Call(mcp, "get_page", new { pageId = id }));
        var back = read.GetProperty("content").GetString()!;
        Assert.Contains("## Findings", back);
        Assert.Contains("**ship**", back);
        Assert.Contains("1. first", back);
        // Exactly one space after the marker: the checkbox's own space must
        // not be left behind, or every round trip indents the text further.
        Assert.Contains("- [x] done", back);
        Assert.DoesNotContain("- [x]  done", back);
    }

    [Fact]
    public async Task Update_page_versions_the_page_and_refuses_both_content_forms_at_once()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);

        var updated = Ok(await Call(mcp, "update_page",
            new { pageId = w.Open.Id, content = "Rewritten.", title = "Open plan v2", changeComment = "via assistant" }));
        Assert.Equal(2, updated.GetProperty("version").GetInt32());

        var versions = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{w.Open.Id}/versions");
        Assert.Equal("via assistant", versions[0].GetProperty("changeComment").GetString());

        Assert.Contains("not both", Error(await Call(mcp, "update_page",
            new { pageId = w.Open.Id, content = "x", contentJson = Doc })));
    }

    [Fact]
    public async Task Get_page_gives_the_content_alone_as_its_first_text()
    {
        // T5-005: the text was one JSON envelope, and sending it back as the
        // content published the JSON as the page.
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);

        var result = await Call(mcp, "get_page", new { pageId = w.Open.Id });
        var content = result.GetProperty("content");
        Assert.Equal("pineapple notes\n", content[0].GetProperty("text").GetString());

        // The description is the second item, without the content in it...
        var about = JsonDocument.Parse(content[1].GetProperty("text").GetString()!).RootElement;
        Assert.Equal("Open plan", about.GetProperty("title").GetString());
        Assert.Equal(1, about.GetProperty("version").GetInt32());
        Assert.False(about.TryGetProperty("content", out var _));

        // ...and the whole record is structured content, for clients that read it.
        var structured = result.GetProperty("structuredContent");
        Assert.Equal("pineapple notes\n", structured.GetProperty("content").GetString());
        Assert.Equal("markdown", structured.GetProperty("format").GetString());

        // The naive round trip is now the right one.
        Ok(await Call(mcp, "update_page", new { pageId = w.Open.Id, content = content[0].GetProperty("text").GetString() }));
        var detail = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{w.Open.Id}");
        Assert.Contains("pineapple notes", detail.GetProperty("contentJson").GetString());
        Assert.DoesNotContain("\\u0022title\\u0022", detail.GetProperty("contentJson").GetString());
    }

    [Fact]
    public async Task Get_pages_whole_answer_sent_back_as_content_is_refused()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);

        var envelope = JsonSerializer.Serialize((await Call(mcp, "get_page", new { pageId = w.Open.Id }))
            .GetProperty("structuredContent"));
        Assert.Contains("only its `content`", Error(await Call(mcp, "update_page", new { pageId = w.Open.Id, content = envelope })));
        Assert.Contains("only its `content`", Error(await Call(mcp, "create_page",
            new { spaceKey = "TOOLS", title = "Copy", content = envelope })));

        var detail = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{w.Open.Id}");
        Assert.Equal(1, detail.GetProperty("currentVersionNumber").GetInt32());
    }

    [Fact]
    public async Task A_markdown_round_trip_leaves_the_rich_parts_of_a_page_alone()
    {
        // T5-003: two words changed through get_page and update_page rewrote
        // every block of a rich page (41 highlighted changes).
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);
        const string rich = """
        {"type":"doc","content":[
          {"type":"tableOfContents"},
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Status"}]},
          {"type":"paragraph","content":[{"type":"text","text":"Now "},{"type":"status","attrs":{"text":"On track","color":"green"}}]},
          {"type":"paragraph","content":[{"type":"text","text":"We launch on October 14, 2026."}]},
          {"type":"children"}
        ]}
        """;
        var page = (await (await w.Alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = w.Space.Id, Title = "Rich", ContentJson = rich.Replace("""{"type":"children"}""",
                """{"type":"dynamicBlock","attrs":{"kind":"children","params":{"depth":"1"}}}""") }))
            .Content.ReadFromJsonAsync<PageDetail>())!;
        await NewPage(w.Alice, w.Space.Id, "A child", page.Id);

        var markdown = (await Call(mcp, "get_page", new { pageId = page.Id })).GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("A child", markdown);
        Ok(await Call(mcp, "update_page", new { pageId = page.Id, content = markdown.Replace("October 14", "October 21") }));

        var before = JsonDocument.Parse((await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{page.Id}/versions/1"))
            .GetProperty("contentJson").GetString()!).RootElement.GetProperty("content").EnumerateArray().ToList();
        var after = JsonDocument.Parse((await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{page.Id}"))
            .GetProperty("contentJson").GetString()!).RootElement.GetProperty("content").EnumerateArray().ToList();

        Assert.Equal(before.Count, after.Count);
        var changed = Enumerable.Range(0, before.Count).Where(i => !JsonElement.DeepEquals(before[i], after[i])).ToList();
        Assert.Equal([3], changed);
        Assert.Contains("October 21", after[3].GetRawText());
        // The live list of children is still live, not a pasted list of links.
        Assert.Equal("dynamicBlock", after[4].GetProperty("type").GetString());
    }

    private const string Sectioned = """
    {"type":"doc","content":[
      {"type":"paragraph","content":[{"type":"text","text":"An introduction."}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Deployment"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Deploy on Tuesday."}]},
      {"type":"panel","attrs":{"panelType":"warning"},"content":[{"type":"paragraph","content":[{"type":"text","text":"Never on Friday."}]}]},
      {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Rollback"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Roll back with the script."}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Support"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Ask the team."}]}
    ]}
    """;

    private static async Task<Guid> SectionedPage(World w) =>
        (await (await w.Alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = w.Space.Id, Title = "Runbook", ContentJson = Sectioned }))
            .Content.ReadFromJsonAsync<PageDetail>())!.Id;

    private static async Task<List<JsonElement>> CurrentBlocks(World w, Guid pageId) =>
        JsonDocument.Parse((await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{pageId}"))
            .GetProperty("contentJson").GetString()!).RootElement.GetProperty("content").EnumerateArray()
            .Select(b => b.Clone()).ToList();

    [Fact]
    public async Task A_section_sent_back_with_its_section_replaces_only_that_section()
    {
        // An assistant that read one section and sent it back replaced the
        // whole page with that section. Named, only that section changes.
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);
        var pageId = await SectionedPage(w);

        var read = await Call(mcp, "get_page", new { pageId, section = "deployment" });
        var markdown = read.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.StartsWith("<!-- tesria-section: deployment.", markdown);
        Assert.Contains("update_page with section \"deployment\"", markdown);
        Assert.DoesNotContain("An introduction", markdown);

        Ok(await Call(mcp, "update_page",
            new { pageId, section = "deployment", content = markdown.Replace("Tuesday", "Wednesday") }));

        var before = JsonDocument.Parse(Sectioned).RootElement.GetProperty("content").EnumerateArray().ToList();
        var after = await CurrentBlocks(w, pageId);
        Assert.Equal(before.Count, after.Count);
        var changed = Enumerable.Range(0, before.Count).Where(i => !JsonElement.DeepEquals(before[i], after[i])).ToList();
        Assert.Equal([2], changed);
        Assert.Contains("Wednesday", after[2].GetRawText());
        // The panel it sent back unchanged is still a panel, and the note is not on the page.
        Assert.Equal("panel", after[3].GetProperty("type").GetString());
        Assert.DoesNotContain(after, b => b.GetRawText().Contains("tesria-section"));
    }

    [Fact]
    public async Task A_section_sent_back_as_the_whole_page_is_refused_and_says_how()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);
        var pageId = await SectionedPage(w);

        var markdown = (await Call(mcp, "get_page", new { pageId, section = "deployment" }))
            .GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("section: \"deployment\"", Error(await Call(mcp, "update_page", new { pageId, content = markdown })));

        var json = (await Call(mcp, "get_page", new { pageId, section = "deployment", format = "json" }))
            .GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Equal("deployment", JsonDocument.Parse(json).RootElement.GetProperty("section").GetString());
        Assert.Contains("section: \"deployment\"", Error(await Call(mcp, "update_page", new { pageId, contentJson = json })));

        // A section named that is not the one the content came from.
        Assert.Contains("section: \"deployment\"", Error(await Call(mcp, "update_page",
            new { pageId, section = "support", content = markdown })));
        // And a section the page does not have lists the ones it does.
        Assert.Contains("rollback", Error(await Call(mcp, "update_page",
            new { pageId, section = "nope", content = "## Nope\n" })));

        // t5-R07: the whole page sent with a section's name, which put the
        // rest of the page into it a second time; and a section without its
        // heading, which silently dropped the heading.
        var whole = (await Call(mcp, "get_page", new { pageId })).GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("must start with that section's heading", Error(await Call(mcp, "update_page",
            new { pageId, section = "deployment", content = whole })));
        var body = markdown[(markdown.IndexOf("## Deployment", StringComparison.Ordinal))..];
        Assert.Contains("goes past the section 'deployment'", Error(await Call(mcp, "update_page",
            new { pageId, section = "deployment", content = body + "\n## Support\n\nAnother section.\n" })));
        Assert.Contains("must start with that section's heading", Error(await Call(mcp, "update_page",
            new { pageId, section = "deployment", content = "Deploy on Wednesday.\n" })));

        var detail = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{pageId}");
        Assert.Equal(1, detail.GetProperty("currentVersionNumber").GetInt32());
    }

    [Fact]
    public async Task A_section_in_the_editors_format_replaces_only_that_section_too()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);
        var pageId = await SectionedPage(w);

        var json = (await Call(mcp, "get_page", new { pageId, section = "rollback", format = "json" }))
            .GetProperty("content")[0].GetProperty("text").GetString()!;
        Ok(await Call(mcp, "update_page",
            new { pageId, section = "rollback", contentJson = json.Replace("with the script", "by hand") }));

        var after = await CurrentBlocks(w, pageId);
        Assert.Equal(8, after.Count);
        Assert.Contains("by hand", after[5].GetRawText());
        Assert.Contains("An introduction.", after[0].GetRawText());
        Assert.Contains("Ask the team.", after[7].GetRawText());
    }

    [Fact]
    public async Task Whole_page_updates_and_new_pages_from_a_section_still_work()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);
        var pageId = await SectionedPage(w);

        // A section read is a fine start for a new page; its note is left behind.
        var markdown = (await Call(mcp, "get_page", new { pageId, section = "support" }))
            .GetProperty("content")[0].GetProperty("text").GetString()!;
        var created = Ok(await Call(mcp, "create_page", new { spaceKey = "TOOLS", title = "Support copy", content = markdown }));
        var copy = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{created.GetProperty("id").GetGuid()}");
        Assert.Contains("Ask the team.", copy.GetProperty("contentJson").GetString());
        Assert.DoesNotContain("tesria-section", copy.GetProperty("contentJson").GetString());

        // A whole page read and sent back is still the whole page.
        var whole = (await Call(mcp, "get_page", new { pageId })).GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.DoesNotContain("tesria-section", whole);
        Ok(await Call(mcp, "update_page", new { pageId, content = whole.Replace("Ask the team.", "Ask anyone.") }));
        var after = await CurrentBlocks(w, pageId);
        Assert.Equal(8, after.Count);
        Assert.Contains("Ask anyone.", after[7].GetRawText());
    }

    [Fact]
    public async Task A_read_only_token_cannot_use_any_write_tool_and_changes_nothing()
    {
        var w = await Build(); using var _ = w.F;
        var readOnly = await Mcp(w.F, w.Alice, readOnly: true);

        foreach (var (tool, args) in new (string, object)[]
        {
            ("create_page", new { spaceKey = "TOOLS", title = "No", content = "no" }),
            ("update_page", new { pageId = w.Open.Id, content = "no" }),
            ("add_page_label", new { pageId = w.Open.Id, label = "nope" }),
            ("remove_page_label", new { pageId = w.Open.Id, label = "shared" }),
        })
        {
            Assert.Contains("read-only", Error(await Call(readOnly, tool, args)));
        }

        // Refused before anything ran: still one version, still labeled.
        var detail = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{w.Open.Id}");
        Assert.Equal(1, detail.GetProperty("currentVersionNumber").GetInt32());
        var labels = await w.Alice.GetFromJsonAsync<JsonElement>($"/api/pages/{w.Open.Id}/labels");
        Assert.Equal(1, labels.GetArrayLength());

        // Reads still work with the same token.
        Ok(await Call(readOnly, "get_page", new { pageId = w.Open.Id }));
    }

    [Fact]
    public async Task Writing_to_a_page_the_caller_cannot_see_is_not_found_not_forbidden()
    {
        var w = await Build(); using var _ = w.F;
        var bob = await Mcp(w.F, w.Bob);

        // "Not found", never "you may not edit this": the latter confirms it exists.
        var message = Error(await Call(bob, "update_page", new { pageId = w.Secret.Id, content = "mine now" }));
        Assert.Contains("not found", message);
        Assert.DoesNotContain("Secret", message);

        Assert.Contains("not found", Error(await Call(bob, "add_page_label", new { pageId = w.Secret.Id, label = "x" })));
    }

    [Fact]
    public async Task Labels_can_be_added_and_removed_and_are_validated()
    {
        var w = await Build(); using var _ = w.F;
        var mcp = await Mcp(w.F, w.Alice);

        var added = Ok(await Call(mcp, "add_page_label", new { pageId = w.Open.Id, label = "roadmap" }));
        Assert.Equal(["roadmap", "shared"], added.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));

        var removed = Ok(await Call(mcp, "remove_page_label", new { pageId = w.Open.Id, label = "shared" }));
        Assert.Equal(["roadmap"], removed.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));

        // The same rule the REST endpoint enforces.
        Assert.Contains("1-50 characters", Error(await Call(mcp, "add_page_label", new { pageId = w.Open.Id, label = "Not A Label!" })));
    }
}

/// <summary>
/// The Markdown the write tools accept is exactly what the export emits, so
/// a page can be read, edited and written back without losing its shape.
/// </summary>
public class MarkdownToProseMirrorTests
{
    private static JsonElement Convert(string markdown) =>
        JsonDocument.Parse(MarkdownToProseMirror.Convert(markdown)).RootElement.Clone();

    private static string Json(string markdown) => MarkdownToProseMirror.Convert(markdown);

    [Fact]
    public void An_empty_document_is_still_a_valid_one()
    {
        var doc = Convert("");
        Assert.Equal("doc", doc.GetProperty("type").GetString());
        // ProseMirror's schema requires at least one block.
        Assert.Equal("paragraph", doc.GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public void Headings_paragraphs_and_inline_marks()
    {
        var json = Json("# Title\n\nSome **bold**, *italic*, ~~struck~~ and `code`.\n");
        Assert.Contains("\"heading\"", json);
        Assert.Contains("\"level\":1", json);
        foreach (var mark in new[] { "bold", "italic", "strike", "code" })
            Assert.Contains($"\"type\":\"{mark}\"", json);
    }

    [Fact]
    public void Links_and_images_keep_their_targets()
    {
        var json = Json("See [the docs](https://example.com/a) and ![a dot](/api/attachments/x/download).");
        Assert.Contains("\"href\":\"https://example.com/a\"", json);
        Assert.Contains("\"src\":\"/api/attachments/x/download\"", json);
        Assert.Contains("\"alt\":\"a dot\"", json);
    }

    [Fact]
    public void Lists_including_task_lists()
    {
        Assert.Contains("\"bulletList\"", Json("- one\n- two\n"));
        Assert.Contains("\"orderedList\"", Json("1. one\n2. two\n"));

        var tasks = Json("- [x] done\n- [ ] todo\n");
        Assert.Contains("\"taskList\"", tasks);
        Assert.Contains("\"checked\":true", tasks);
        Assert.Contains("\"checked\":false", tasks);
        // The checkbox marker itself is an attribute, not text.
        Assert.DoesNotContain("[x]", tasks);
    }

    [Fact]
    public void Code_blocks_keep_their_language_and_their_whitespace()
    {
        var json = Json("```python\ndef f():\n    return 1\n```\n");
        Assert.Contains("\"codeBlock\"", json);
        Assert.Contains("\"language\":\"python\"", json);
        Assert.Contains("def f():\\n    return 1", json);
        // A fence with no language still has one the editor understands.
        Assert.Contains("\"language\":\"plaintext\"", Json("```\nplain\n```\n"));
    }

    [Fact]
    public void Tables_quotes_and_rules()
    {
        var table = Json("| Name | Value |\n| --- | --- |\n| a | 1 |\n");
        Assert.Contains("\"table\"", table);
        Assert.Contains("\"tableHeader\"", table);
        Assert.Contains("\"tableCell\"", table);

        Assert.Contains("\"blockquote\"", Json("> quoted\n"));
        Assert.Contains("\"horizontalRule\"", Json("---\n"));
    }

    [Fact]
    public void A_br_in_a_table_cell_is_a_line_break_rather_than_lost()
    {
        // The export joins a cell's blocks with <br> (T5-001), so reading a
        // page and writing it back keeps the two lines apart.
        var table = Json("| Name | Value |\n| --- | --- |\n| a | first<br>second |\n");
        Assert.Contains("\"hardBreak\"", table);
        Assert.Contains("\"text\":\"first\"", table);
        Assert.Contains("\"text\":\"second\"", table);
    }

    [Fact]
    public void Raw_html_is_not_a_way_to_smuggle_markup_in()
    {
        // The contract is Markdown, not HTML: a script tag is not a node type,
        // so it cannot become one.
        var json = Json("<script>alert(1)</script>\n\nafter\n");
        Assert.DoesNotContain("script", json);
        Assert.Contains("after", json);
    }

    /// <summary>All the text of a converted document, block by block.</summary>
    private static List<string> BlockTexts(string markdown) =>
        Convert(markdown).GetProperty("content").EnumerateArray()
            .Select(b => string.Concat(Texts(b)))
            .ToList();

    private static IEnumerable<string> Texts(JsonElement node)
    {
        if (node.TryGetProperty("text", out var t)) yield return t.GetString()!;
        if (node.TryGetProperty("type", out var type) && type.GetString() == "hardBreak") yield return "\n";
        if (node.TryGetProperty("content", out var content))
            foreach (var child in content.EnumerateArray())
                foreach (var s in Texts(child)) yield return s;
    }

    [Fact]
    public void Html_keeps_its_words_as_plain_text()
    {
        // T5-030: an HTML block vanished, text and all, though the docs say
        // what Tesria does not recognize arrives as plain text.
        var blocks = BlockTexts(
            "<div class=\"custom\">raw html block</div>\n\n"
            + "<details><summary>Summary text</summary>\n\nThe body.\n\n</details>\n\n"
            + "Press <kbd>Ctrl</kbd>+<kbd>K</kbd> now.<br>Next line.\n\n"
            + "A <span style=\"color:red\">red word</span> here.\n\n"
            + "<!-- a note to self -->\n");

        Assert.Equal(["raw html block", "Summary text", "The body.", "Press Ctrl+K now.\nNext line.", "A red word here."], blocks);
        // The tags themselves are gone; only their words are left.
        Assert.DoesNotContain(blocks, b => b.Contains('<'));
    }

    [Fact]
    public void Html_entities_and_line_breaks_in_a_block_read_as_text()
    {
        Assert.Equal(["Fish & chips", "second line"], BlockTexts("<p>Fish &amp; chips<br/>second line</p>\n"));
    }

    [Fact]
    public void A_heading_anchor_does_not_come_back_as_an_empty_paragraph()
    {
        // get_page writes <a id> anchors above headings when the page links
        // to them; writing that Markdown back used to add a blank line each.
        var doc = Convert("<a id=\"plan\"></a>\n## Plan\n\nText.\n");
        Assert.Equal(["heading", "paragraph"],
            doc.GetProperty("content").EnumerateArray().Select(b => b.GetProperty("type").GetString()));
    }
}

/// <summary>
/// Writing an assistant's Markdown back onto a rich page keeps every block it
/// did not change exactly as it was (T5-003).
/// </summary>
public class MarkdownMergeTests
{
    private const string Rich = """
    {"type":"doc","content":[
      {"type":"tableOfContents"},
      {"type":"pageProperties","content":[{"type":"table","content":[
        {"type":"tableRow","content":[
          {"type":"tableCell","attrs":{"colwidth":[180]},"content":[{"type":"paragraph","content":[{"type":"text","text":"Status"}]}]},
          {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"status","attrs":{"text":"On track","color":"green"}}]}]}]},
        {"type":"tableRow","content":[
          {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Owner"}]}]},
          {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"mention","attrs":{"userId":"11111111-1111-1111-1111-111111111111","label":"Priya"}}]}]}]}
      ]}]},
      {"type":"excerpt","content":[{"type":"paragraph","content":[{"type":"text","text":"The launch in one line."}]}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Timeline"}]},
      {"type":"paragraph","content":[{"type":"text","text":"We launch on October 14, 2026."}]},
      {"type":"paragraph"},
      {"type":"taskList","content":[{"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[
        {"type":"text","text":"Brief "},{"type":"mention","attrs":{"userId":"22222222-2222-2222-2222-222222222222","label":"Sam"}}]}]}]},
      {"type":"panel","attrs":{"panelType":"info"},"content":[{"type":"paragraph","content":[{"type":"text","text":"Shared folders a whole team can use."}]}]}
    ]}
    """;

    private static List<JsonElement> Blocks(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("content").EnumerateArray().Select(b => b.Clone()).ToList();

    private static string Merge(string current, Func<string, string> edit)
    {
        var markdown = Tesria.Api.Features.Export.ProseMirrorRenderer.ToMarkdown(current);
        var blocks = Tesria.Api.Features.Export.ProseMirrorRenderer.ToMarkdownBlocks(current);
        return MarkdownMerge.Merge(current, blocks, edit(markdown));
    }

    [Fact]
    public void A_live_block_whose_results_changed_since_the_read_stays_live()
    {
        // t5-R02: the snapshot get_page wrote no longer matched the block's
        // results by the time the assistant saved, and the block was rebuilt
        // from it as a plain table. Between get_page's markers it is the
        // same live block, whatever the snapshot says.
        const string live = """
        {"type":"doc","content":[
          {"type":"paragraph","content":[{"type":"text","text":"Intro with alpha."}]},
          {"type":"dynamicBlock","attrs":{"kind":"recent","params":{"limit":5}}},
          {"type":"paragraph","content":[{"type":"text","text":"Closing."}]}
        ]}
        """;
        var markdown = Tesria.Api.Features.Export.ProseMirrorRenderer.ToMarkdown(live, liveMarkers: true);
        Assert.Contains(Tesria.Api.Features.Export.ProseMirrorRenderer.LiveStart, markdown);
        Assert.DoesNotContain("tesria-live", Tesria.Api.Features.Export.ProseMirrorRenderer.ToMarkdown(live));

        // What the assistant sends back: its edit, and a snapshot that now
        // reads differently.
        var start = markdown.IndexOf("<!-- tesria-live", StringComparison.Ordinal);
        var end = markdown.IndexOf("<!-- /tesria-live -->", StringComparison.Ordinal);
        var sent = markdown[..start].Replace("alpha", "gamma")
            + markdown[start..end].Replace("dynamic content", "| Page | Updated |\n| --- | --- |\n| Newer page | today |\n")
            + markdown[end..];
        var merged = MarkdownMerge.Merge(live, Tesria.Api.Features.Export.ProseMirrorRenderer.ToMarkdownBlocks(live), sent);

        var blocks = Blocks(merged);
        Assert.Equal(3, blocks.Count);
        Assert.Contains("gamma", blocks[0].GetRawText());
        Assert.Equal(Blocks(live)[1].GetRawText(), blocks[1].GetRawText());
        Assert.Equal(Blocks(live)[2].GetRawText(), blocks[2].GetRawText());

        // Without the markers the block goes, as any block the assistant leaves out.
        var dropped = MarkdownMerge.Merge(live, Tesria.Api.Features.Export.ProseMirrorRenderer.ToMarkdownBlocks(live),
            "Intro with alpha.\n\nClosing.\n");
        Assert.DoesNotContain(Blocks(dropped), b => b.GetRawText().Contains("dynamicBlock"));
    }

    [Fact]
    public void Two_edits_change_two_blocks_and_nothing_else()
    {
        var merged = Merge(Rich, md => md
            .Replace("October 14, 2026", "October 21, 2026")
            .Replace("a whole team", "an entire team"));

        var before = Blocks(Rich);
        var after = Blocks(merged);
        Assert.Equal(before.Count, after.Count);
        var changed = Enumerable.Range(0, before.Count).Where(i => !JsonElement.DeepEquals(before[i], after[i])).ToList();
        // The paragraph and the panel; the panel is Markdown's quote now,
        // since the assistant changed it, which is the documented cost.
        Assert.Equal([4, 7], changed);
        Assert.Contains("October 21, 2026", after[4].GetRawText());
        Assert.Contains("an entire team", after[7].GetRawText());
    }

    [Fact]
    public void Sent_back_unchanged_the_page_is_exactly_as_it_was()
    {
        var merged = Merge(Rich, md => md);
        Assert.True(JsonElement.DeepEquals(
            JsonDocument.Parse(Rich).RootElement, JsonDocument.Parse(merged).RootElement));
    }

    [Fact]
    public void Added_and_removed_blocks_land_where_the_markdown_puts_them()
    {
        var merged = Merge(Rich, md => md
            .Replace("We launch on October 14, 2026.\n\n", "")
            .Replace("## Timeline\n", "## Timeline\n\nA new paragraph.\n"));

        var types = Blocks(merged).Select(b => b.GetProperty("type").GetString()).ToList();
        // The rich blocks are still the rich blocks.
        Assert.Equal("tableOfContents", types[0]);
        Assert.Equal("pageProperties", types[1]);
        Assert.Equal("excerpt", types[2]);
        Assert.Contains("A new paragraph.", merged);
        Assert.DoesNotContain("October 14", merged);
        Assert.Equal("taskList", types[^2]);
        Assert.Equal("panel", types[^1]);
    }

    [Fact]
    public void A_completely_new_page_is_simply_the_markdown()
    {
        var merged = Merge(Rich, _ => "# Something else\n\nEntirely.\n");
        Assert.Equal(["heading", "paragraph"], Blocks(merged).Select(b => b.GetProperty("type").GetString()));
    }
}
