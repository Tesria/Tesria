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
        (await w.Alice.PostAsJsonAsync("/api/spaces/TOOLS/permissions",
            new { PrincipalType = User, PrincipalId = w.AliceId, Operation = View })).EnsureSuccessStatusCode();

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

        // Refused before anything ran: still one version, still labelled.
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
    public void Raw_html_is_not_a_way_to_smuggle_markup_in()
    {
        // The contract is Markdown, not HTML: a script tag is not a node type,
        // so it cannot become one.
        var json = Json("<script>alert(1)</script>\n\nafter\n");
        Assert.DoesNotContain("script", json);
        Assert.Contains("after", json);
    }
}
