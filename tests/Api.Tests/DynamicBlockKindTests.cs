using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The eleven kinds added against the Wave D contract. Every kind owes the
/// same three: a normal result, a **leak test** (a page the caller cannot
/// view influences nothing — not a row, not a count, not a column), and an
/// export snapshot.
/// </summary>
public class DynamicBlockKindTests
{
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record Column(string Key, string Label);
    private record Cell(string? Text, string? Href, DateTimeOffset? Date, JsonElement? User, bool? Checked);
    private record Item(string Title, string? Href, string? Subtitle, Dictionary<string, Cell>? Cells, List<Item>? Children);
    private record Result(string Kind, string Shape, List<Item> Items, string? Empty, List<Column>? Columns, string? Document);

    private const int User = 0, View = 0;
    private const string Plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"body"}]}]}""";

    private static async Task<SpaceDto> NewSpace(HttpClient c)
    {
        var res = await c.PostAsJsonAsync("/api/spaces",
            new { Key = $"K{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}", Name = "Kinds", Description = (string?)null });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SpaceDto>())!;
    }

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title, Guid? parent = null, string? content = null) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = content ?? Plain }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    private static Task<Result?> Block(HttpClient c, Guid host, string kind, string query = "") =>
        c.GetFromJsonAsync<Result>($"/api/pages/{host}/blocks/{kind}{(query.Length > 0 ? "?" + query : "")}");

    private static async Task Label(HttpClient c, Guid pageId, string name) =>
        (await c.PostAsJsonAsync($"/api/pages/{pageId}/labels", new { Name = name })).EnsureSuccessStatusCode();

    /// <summary>Home → [Open, Secret]; Secret is restricted to Alice and labelled like Open.</summary>
    private sealed record World(TestAppFactory F, HttpClient Alice, HttpClient Bob, Guid AliceId, SpaceDto Space, PageDetail Home, PageDetail Open, PageDetail Secret);

    private static async Task<World> Build(string? homeContent = null)
    {
        var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();

        var space = await NewSpace(alice);
        var home = await NewPage(alice, space.Id, "Home", content: homeContent);
        var open = await NewPage(alice, space.Id, "Open", home.Id);
        var secret = await NewPage(alice, space.Id, "Secret", home.Id);
        await Label(alice, open.Id, "shared");
        await Label(alice, secret.Id, "shared");
        (await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View })).EnsureSuccessStatusCode();
        return new World(factory, alice, bob, aliceId, space, home, open, secret);
    }

    private static string Json(Result? r) => JsonSerializer.Serialize(r);

    // -- recently-updated ------------------------------------------------------

    [Fact]
    public async Task Recently_updated_lists_pages_with_their_last_author()
    {
        var w = await Build(); using var _ = w.F;
        var r = await Block(w.Alice, w.Home.Id, "recently-updated", "limit=50");
        Assert.Equal("table", r!.Shape);
        Assert.Equal(["title", "who", "when"], r.Columns!.Select(c => c.Key));
        Assert.Contains(r.Items, i => i.Title == "Secret");
        var open = r.Items.Single(i => i.Title == "Open");
        Assert.NotNull(open.Cells!["when"].Date);
        Assert.NotNull(open.Cells["who"].User);
    }

    [Fact]
    public async Task Recently_updated_omits_a_restricted_page_and_still_fills_the_limit()
    {
        var w = await Build(); using var _ = w.F;
        // Three more visible pages, so a limit of 3 could be filled entirely by
        // pages sitting *below* Secret in the ordering — it must look further
        // down rather than return a short list.
        for (var i = 0; i < 3; i++) await NewPage(w.Alice, w.Space.Id, $"Extra {i}", w.Home.Id);
        var r = await Block(w.Bob, w.Home.Id, "recently-updated", "limit=3");
        Assert.Equal(3, r!.Items.Count);
        Assert.DoesNotContain("Secret", Json(r));
    }

    // -- content-by-label ------------------------------------------------------

    [Fact]
    public async Task Content_by_label_matches_any_or_all_labels()
    {
        var w = await Build(); using var _ = w.F;
        await Label(w.Alice, w.Open.Id, "release");

        var any = await Block(w.Alice, w.Home.Id, "content-by-label", "labels=shared,release&match=any");
        Assert.Equal(["Open", "Secret"], any!.Items.Select(i => i.Title).Order());

        var all = await Block(w.Alice, w.Home.Id, "content-by-label", "labels=shared,release&match=all");
        Assert.Equal(["Open"], all!.Items.Select(i => i.Title));

        var none = await Block(w.Alice, w.Home.Id, "content-by-label", "labels=nothing-here");
        Assert.Empty(none!.Items);
        Assert.Contains("nothing-here", none.Empty);
    }

    [Fact]
    public async Task Content_by_label_hides_a_restricted_page_that_carries_the_label()
    {
        var w = await Build(); using var _ = w.F;
        var r = await Block(w.Bob, w.Home.Id, "content-by-label", "labels=shared");
        Assert.Equal(["Open"], r!.Items.Select(i => i.Title));
        Assert.DoesNotContain("Secret", Json(r));
    }

    [Fact]
    public async Task Content_by_label_requires_a_label()
    {
        var w = await Build(); using var _ = w.F;
        var res = await w.Alice.GetAsync($"/api/pages/{w.Home.Id}/blocks/content-by-label");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("labels", await res.Content.ReadAsStringAsync());
    }

    // -- attachments / change-history ------------------------------------------

    [Fact]
    public async Task Attachments_lists_the_hosts_files_with_a_download_link()
    {
        var w = await Build(); using var _ = w.F;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "notes.txt");
        (await w.Alice.PostAsync($"/api/pages/{w.Home.Id}/attachments", form)).EnsureSuccessStatusCode();

        var r = await Block(w.Alice, w.Home.Id, "attachments");
        var item = Assert.Single(r!.Items);
        Assert.Equal("notes.txt", item.Cells!["name"].Text);
        Assert.StartsWith("/api/attachments/", item.Cells["name"].Href);
        Assert.Equal("5 B", item.Cells["size"].Text);

        // Host-anchored: Bob cannot see Secret, so he cannot list its files.
        Assert.Equal(HttpStatusCode.NotFound, (await w.Bob.GetAsync($"/api/pages/{w.Secret.Id}/blocks/attachments")).StatusCode);
    }

    [Fact]
    public async Task Change_history_lists_versions_newest_first()
    {
        var w = await Build(); using var _ = w.F;
        await w.Alice.PutAsJsonAsync($"/api/pages/{w.Home.Id}", new { ContentJson = Plain, ChangeComment = "second pass" });

        var r = await Block(w.Alice, w.Home.Id, "change-history");
        Assert.Equal(["v2", "v1"], r!.Items.Select(i => i.Title));
        Assert.Equal("second pass", r.Items[0].Cells!["comment"].Text);
        Assert.NotNull(r.Items[0].Cells!["who"].User);
    }

    // -- contributors ----------------------------------------------------------

    [Fact]
    public async Task Contributors_counts_edits_and_ignores_pages_the_caller_cannot_see()
    {
        var w = await Build(); using var _ = w.F;
        // Alice edits Secret twice; Bob must see neither the edits nor the count.
        await w.Alice.PutAsJsonAsync($"/api/pages/{w.Secret.Id}", new { ContentJson = Plain });
        await w.Alice.PutAsJsonAsync($"/api/pages/{w.Secret.Id}", new { ContentJson = Plain });

        var mine = await Block(w.Alice, w.Home.Id, "contributors", "scope=tree");
        Assert.Equal("5 edits", Assert.Single(mine!.Items).Subtitle); // home + open + secret v1..v3

        var theirs = await Block(w.Bob, w.Home.Id, "contributors", "scope=tree");
        Assert.Equal("2 edits", Assert.Single(theirs!.Items).Subtitle); // home + open only
    }

    // -- include-page / excerpt-include ---------------------------------------

    [Fact]
    public async Task Include_page_inlines_the_content_and_refuses_to_recurse_or_self_include()
    {
        var w = await Build(); using var _ = w.F;
        var source = await NewPage(w.Alice, w.Space.Id, "Source",
            content: """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"borrowed text"}]}]}""");

        var r = await Block(w.Alice, w.Home.Id, "include-page", $"page={source.Id}");
        Assert.Equal("document", r!.Shape);
        Assert.Contains("borrowed text", r.Document);

        var self = await Block(w.Alice, w.Home.Id, "include-page", $"page={w.Home.Id}");
        Assert.Null(self!.Document);
        Assert.Contains("cannot include itself", self.Empty);
    }

    [Fact]
    public async Task Including_a_page_you_cannot_see_says_nothing_about_it()
    {
        var w = await Build(); using var _ = w.F;
        var r = await Block(w.Bob, w.Home.Id, "include-page", $"page={w.Secret.Id}");
        Assert.Null(r!.Document);
        // Not an error naming the page: that would confirm it exists.
        Assert.DoesNotContain("Secret", Json(r));
        Assert.Contains("cannot see it", r.Empty);
    }

    [Fact]
    public async Task Excerpt_include_takes_only_the_marked_excerpt()
    {
        var w = await Build(); using var _ = w.F;
        var source = await NewPage(w.Alice, w.Space.Id, "Source", content: """
        {"type":"doc","content":[
          {"type":"paragraph","content":[{"type":"text","text":"not the excerpt"}]},
          {"type":"excerpt","content":[{"type":"paragraph","content":[{"type":"text","text":"the excerpt"}]}]}
        ]}
        """);

        var r = await Block(w.Alice, w.Home.Id, "excerpt-include", $"page={source.Id}");
        Assert.Contains("the excerpt", r!.Document);
        Assert.DoesNotContain("not the excerpt", r.Document);

        var none = await Block(w.Alice, w.Home.Id, "excerpt-include", $"page={w.Open.Id}");
        Assert.Null(none!.Document);
        Assert.Contains("no excerpt", none.Empty);
    }

    // -- page-properties-report ------------------------------------------------

    private static string PropertiesDoc(string owner, string status) => $$"""
    {"type":"doc","content":[{"type":"pageProperties","content":[{"type":"table","content":[
      {"type":"tableRow","content":[
        {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Owner"}]}]},
        {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"{{owner}}"}]}]}]},
      {"type":"tableRow","content":[
        {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Status"}]}]},
        {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"{{status}}"}]}]}]}
    ]}]}]}
    """;

    [Fact]
    public async Task Page_properties_report_builds_its_columns_from_the_pages_themselves()
    {
        var w = await Build(); using var _ = w.F;
        var a = await NewPage(w.Alice, w.Space.Id, "Alpha", content: PropertiesDoc("Ana", "Live"));
        var b = await NewPage(w.Alice, w.Space.Id, "Beta", content: PropertiesDoc("Ben", "Draft"));
        await Label(w.Alice, a.Id, "project");
        await Label(w.Alice, b.Id, "project");

        var r = await Block(w.Alice, w.Home.Id, "page-properties-report", "labels=project");
        Assert.Equal(["page", "Owner", "Status"], r!.Columns!.Select(c => c.Key));
        Assert.Equal(["Alpha", "Beta"], r.Items.Select(i => i.Title));
        Assert.Equal("Ana", r.Items[0].Cells!["Owner"].Text);
        Assert.Equal("Draft", r.Items[1].Cells!["Status"].Text);
    }

    [Fact]
    public async Task Page_properties_report_leaks_neither_a_restricted_pages_row_nor_its_columns()
    {
        var w = await Build(); using var _ = w.F;
        var open = await NewPage(w.Alice, w.Space.Id, "Alpha", content: PropertiesDoc("Ana", "Live"));
        await Label(w.Alice, open.Id, "project");
        var hidden = await NewPage(w.Alice, w.Space.Id, "Confidential",
            content: """
            {"type":"doc","content":[{"type":"pageProperties","content":[{"type":"table","content":[
              {"type":"tableRow","content":[
                {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Salary band"}]}]},
                {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"P7"}]}]}]}
            ]}]}]}
            """);
        await Label(w.Alice, hidden.Id, "project");
        (await w.Alice.PostAsJsonAsync($"/api/pages/{hidden.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = w.AliceId, Operation = View })).EnsureSuccessStatusCode();

        var r = await Block(w.Bob, w.Home.Id, "page-properties-report", "labels=project");
        Assert.Equal(["Alpha"], r!.Items.Select(i => i.Title));
        // The column names come from the pages, so a hidden page's key would
        // be a leak even with no row behind it.
        Assert.DoesNotContain("Salary band", Json(r));
        Assert.DoesNotContain("Confidential", Json(r));
    }

    // -- labels ----------------------------------------------------------------

    [Fact]
    public async Task Labels_counts_only_visible_pages()
    {
        var w = await Build(); using var _ = w.F;
        await Label(w.Alice, w.Home.Id, "shared");

        var mine = await Block(w.Alice, w.Home.Id, "labels", "mode=popular");
        Assert.Equal("3 pages", Assert.Single(mine!.Items, i => i.Title == "shared").Subtitle);

        // Bob cannot see Secret, so for him the label is on two pages.
        var theirs = await Block(w.Bob, w.Home.Id, "labels", "mode=popular");
        Assert.Equal("2 pages", Assert.Single(theirs!.Items, i => i.Title == "shared").Subtitle);
    }

    [Fact]
    public async Task Labels_page_mode_lists_the_hosts_own_labels()
    {
        var w = await Build(); using var _ = w.F;
        await Label(w.Alice, w.Home.Id, "handbook");
        var r = await Block(w.Alice, w.Home.Id, "labels");
        Assert.Equal(["handbook"], r!.Items.Select(i => i.Title));
        Assert.Equal("/labels/handbook", r.Items[0].Href);
    }

    // -- task-report -----------------------------------------------------------

    private static string TasksDoc(Guid? assignee, string name, bool done) =>
        "{\"type\":\"doc\",\"content\":[{\"type\":\"taskList\",\"content\":[{\"type\":\"taskItem\",\"attrs\":{\"checked\":"
        + (done ? "true" : "false")
        + (assignee is null ? "" : ",\"assigneeId\":\"" + assignee + "\",\"assigneeName\":\"" + name + "\"")
        + "},\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Do the thing\"}]}]}]}]}";

    [Fact]
    public async Task Task_report_filters_by_state_and_assignee()
    {
        var w = await Build(); using var _ = w.F;
        await w.Alice.PutAsJsonAsync($"/api/pages/{w.Open.Id}", new { ContentJson = TasksDoc(w.AliceId, "Alice", false) });
        var done = await NewPage(w.Alice, w.Space.Id, "Done page", w.Home.Id, TasksDoc(w.AliceId, "Alice", true));

        var open = await Block(w.Alice, w.Home.Id, "task-report", "scope=tree&status=open");
        Assert.Equal(["Open"], open!.Items.Select(i => i.Cells!["page"].Text));
        Assert.Equal(false, Assert.Single(open.Items).Cells!["done"].Checked);
        Assert.Equal("Alice", open.Items[0].Cells!["who"].Text);

        var all = await Block(w.Alice, w.Home.Id, "task-report", "scope=tree&status=all");
        Assert.Equal(2, all!.Items.Count);

        var mine = await Block(w.Alice, w.Home.Id, "task-report", "scope=tree&status=all&assignee=me");
        Assert.Equal(2, mine!.Items.Count);

        var theirs = await Block(w.Alice, w.Home.Id, "task-report", $"scope=tree&status=all&assignee={Guid.NewGuid()}");
        Assert.Empty(theirs!.Items);
        GC.KeepAlive(done);
    }

    [Fact]
    public async Task Task_report_never_reads_a_restricted_pages_tasks()
    {
        var w = await Build(); using var _ = w.F;
        await w.Alice.PutAsJsonAsync($"/api/pages/{w.Secret.Id}",
            new { ContentJson = TasksDoc(w.AliceId, "Alice", false).Replace("Do the thing", "Fire someone") });

        var r = await Block(w.Bob, w.Home.Id, "task-report", "scope=tree&status=all");
        Assert.Empty(r!.Items);
        Assert.DoesNotContain("Fire someone", Json(r));
    }

    // -- page-tree -------------------------------------------------------------

    [Fact]
    public async Task Page_tree_roots_at_the_host_or_the_space_and_hides_restricted_branches()
    {
        var w = await Build(); using var _ = w.F;
        await NewPage(w.Alice, w.Space.Id, "Below secret", w.Secret.Id);

        var fromHost = await Block(w.Alice, w.Home.Id, "page-tree", "root=host&depth=3");
        Assert.Equal(["Open", "Secret"], fromHost!.Items.Select(i => i.Title).Order());

        var fromSpace = await Block(w.Alice, w.Home.Id, "page-tree", "root=space");
        Assert.Equal(["Home"], fromSpace!.Items.Select(i => i.Title));

        var theirs = await Block(w.Bob, w.Home.Id, "page-tree", "root=host&depth=3");
        Assert.Equal(["Open"], theirs!.Items.Select(i => i.Title));
        Assert.DoesNotContain("Below secret", Json(theirs));
    }

    // -- export ----------------------------------------------------------------

    [Fact]
    public async Task Every_shape_survives_an_export_and_is_filtered_for_the_exporting_user()
    {
        var doc = """
        {"type":"doc","content":[
          {"type":"dynamicBlock","attrs":{"kind":"page-tree","params":{"root":"host","depth":"2"}}},
          {"type":"dynamicBlock","attrs":{"kind":"recently-updated","params":{"limit":"20"}}},
          {"type":"dynamicBlock","attrs":{"kind":"contributors","params":{"scope":"tree"}}}
        ]}
        """;
        var w = await Build(doc); using var _ = w.F;

        // Through Markdown since 12.1: HTML is captured in a browser now, so
        // the shape of every block is checked in the format that is still
        // rendered here. What is being tested is the snapshot and its
        // filtering, not the markup.
        var aliceMd = await (await w.Alice.GetAsync($"/api/pages/{w.Home.Id}/export?format=markdown")).Content.ReadAsStringAsync();
        Assert.Contains("Secret", aliceMd);
        Assert.Contains("Updated by", aliceMd);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(aliceMd, "Snapshot taken").Count);

        var bobMd = await (await w.Bob.GetAsync($"/api/pages/{w.Home.Id}/export?format=markdown")).Content.ReadAsStringAsync();
        Assert.Contains("Open", bobMd);
        Assert.DoesNotContain("Secret", bobMd);
    }
}
