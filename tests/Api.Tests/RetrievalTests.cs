using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Features.Pages;
using Tesria.Api.Features.Search;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Retrieval quality: what a search result says about *why* it matched, and
/// being able to fetch one section instead of a whole page. Both exist so an
/// assistant can judge relevance and spend its context deliberately.
/// </summary>
public class SearchSnippetTests
{
    private const string Page =
        "Getting started Install the thing and run it. " +
        "Webhooks let another system hear about changes as they happen, without polling. " +
        "Configure the endpoint under space settings.";

    [Fact]
    public void A_snippet_is_the_matching_passage_not_the_start_of_the_page()
    {
        var snippet = SearchSnippets.Window(Page, "webhooks");
        // The old behavior returned the page's opening line, which says
        // nothing about why the page matched.
        Assert.Contains("**Webhooks**", snippet);
        Assert.Contains("without polling", snippet);
    }

    [Fact]
    public void A_match_far_into_a_page_elides_what_comes_before_it()
    {
        var long_ = new string('x', 900) + " the pineapple clause applies here " + new string('y', 900);
        var snippet = SearchSnippets.Window(long_, "pineapple");
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("**pineapple**", snippet);
        // Bounded: a snippet is a snippet.
        Assert.True(snippet.Length < 300, $"snippet was {snippet.Length} characters");
    }

    [Fact]
    public void The_earliest_matching_word_wins_so_the_window_is_where_the_reader_expects()
    {
        var snippet = SearchSnippets.Window(Page, "polling install");
        Assert.Contains("**Install**", snippet);
    }

    [Fact]
    public void A_page_with_no_match_still_returns_something_readable()
    {
        var snippet = SearchSnippets.Window(Page, "pineapple");
        Assert.StartsWith("Getting started", snippet);
        Assert.DoesNotContain("**", snippet);
    }

    [Fact]
    public void Short_pages_are_returned_whole_rather_than_padded_with_ellipses()
    {
        Assert.Equal("", SearchSnippets.Window("", "anything"));
        Assert.Equal("A short page.", SearchSnippets.Window("A short page.", "nothing here"));
    }
}

public class PageSectionTests
{
    private const string Doc = """
    {"type":"doc","content":[
      {"type":"paragraph","content":[{"type":"text","text":"Intro text."}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Deployment"}]},
      {"type":"paragraph","content":[{"type":"text","text":"How to deploy."}]},
      {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Rollback"}]},
      {"type":"paragraph","content":[{"type":"text","text":"How to roll back."}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Support"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Who to ask."}]}
    ]}
    """;

    [Fact]
    public void The_outline_is_every_heading_in_document_order()
    {
        var outline = PageSections.Outline(Doc);
        Assert.Equal(["deployment", "rollback", "support"], outline.Select(h => h.Id));
        Assert.Equal([2, 3, 2], outline.Select(h => h.Level));
        Assert.Equal("Deployment", outline[0].Text);
    }

    [Fact]
    public void A_section_brings_its_sub_headings_but_stops_at_the_next_peer()
    {
        var slice = PageSections.Extract(Doc, "deployment")!;
        Assert.Contains("Deployment", slice);
        Assert.Contains("How to deploy.", slice);
        // A level-3 heading beneath it belongs to it...
        Assert.Contains("Rollback", slice);
        Assert.Contains("How to roll back.", slice);
        // ...but the next level-2 heading does not.
        Assert.DoesNotContain("Support", slice);
        // And nothing before it leaks in.
        Assert.DoesNotContain("Intro text.", slice);
    }

    [Fact]
    public void A_deeper_section_stops_at_the_next_heading_of_any_higher_level()
    {
        var slice = PageSections.Extract(Doc, "rollback")!;
        Assert.Contains("How to roll back.", slice);
        Assert.DoesNotContain("Support", slice);
        Assert.DoesNotContain("How to deploy.", slice);
    }

    [Fact]
    public void Duplicate_headings_keep_the_numbering_the_anchors_use()
    {
        var doc = """
        {"type":"doc","content":[
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Notes"}]},
          {"type":"paragraph","content":[{"type":"text","text":"First notes."}]},
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Notes"}]},
          {"type":"paragraph","content":[{"type":"text","text":"Second notes."}]}
        ]}
        """;
        Assert.Equal(["notes", "notes-2"], PageSections.Outline(doc).Select(h => h.Id));
        Assert.Contains("First notes.", PageSections.Extract(doc, "notes")!);
        Assert.Contains("Second notes.", PageSections.Extract(doc, "notes-2")!);
        Assert.DoesNotContain("Second notes.", PageSections.Extract(doc, "notes")!);
    }

    [Fact]
    public void A_heading_inside_a_container_is_listed_but_not_sliceable()
    {
        // Slicing mid-panel would produce something that is not a document,
        // so it is refused rather than returned malformed.
        var doc = """
        {"type":"doc","content":[
          {"type":"panel","attrs":{"panelType":"info"},"content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Inside"}]}]},
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Outside"}]},
          {"type":"paragraph","content":[{"type":"text","text":"Body."}]}
        ]}
        """;
        Assert.Equal(["inside", "outside"], PageSections.Outline(doc).Select(h => h.Id));
        Assert.Null(PageSections.Extract(doc, "inside"));
        // The top-level one still works, and is matched by id not by position.
        Assert.Contains("Body.", PageSections.Extract(doc, "outside")!);
    }

    [Fact]
    public void Unknown_anchors_and_malformed_documents_return_nothing_rather_than_throwing()
    {
        Assert.Null(PageSections.Extract(Doc, "no-such-heading"));
        Assert.Null(PageSections.Extract("not json", "anything"));
        Assert.Empty(PageSections.Outline("not json"));
    }
}

/// <summary>The same two improvements, through the tools an assistant actually calls.</summary>
public class McpRetrievalTests
{
    private record Created(Guid Id, string Token);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);

    private const string Doc = """
    {"type":"doc","content":[
      {"type":"paragraph","content":[{"type":"text","text":"An introduction that mentions nothing useful."}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Deployment"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Run the pipeline to deploy pineapple."}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Support"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Ask the team."}]}
    ]}
    """;

    private static async Task<(TestAppFactory f, HttpClient mcp, Guid pageId)> World()
    {
        var f = new TestAppFactory();
        var session = f.CreateClient();
        await session.RegisterAndSignInAsync();
        var space = (await (await session.PostAsJsonAsync("/api/spaces",
            new { Key = "RET", Name = "Retrieval", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>())!;
        var page = (await (await session.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, ParentPageId = (Guid?)null, Title = "Runbook", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = "ret", ReadOnly = true }))
            .Content.ReadFromJsonAsync<Created>();
        var mcp = f.CreateClient();
        mcp.DefaultRequestHeaders.Authorization = new("Bearer", created!.Token);
        mcp.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return (f, mcp, page.Id);
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
        var result = JsonDocument.Parse(body).RootElement.GetProperty("result").Clone();
        if (result.TryGetProperty("isError", out var e) && e.GetBoolean())
            throw new Xunit.Sdk.XunitException(result.GetProperty("content")[0].GetProperty("text").GetString());
        return result.TryGetProperty("structuredContent", out var sc)
            ? sc
            : JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    [Fact]
    public async Task Get_page_returns_an_outline_and_can_return_one_section()
    {
        var (f, mcp, pageId) = await World();
        using var _ = f;

        var whole = await Call(mcp, "get_page", new { pageId });
        Assert.Equal(["deployment", "support"],
            whole.GetProperty("outline").EnumerateArray().Select(h => h.GetProperty("id").GetString()));
        // Null fields are omitted from the tool result, so "no section asked
        // for" shows up as the property being absent.
        Assert.False(whole.TryGetProperty("section", out var none) && none.ValueKind is not JsonValueKind.Null,
            "a whole-page read should not claim a section");
        Assert.Contains("An introduction", whole.GetProperty("content").GetString());

        var section = await Call(mcp, "get_page", new { pageId, section = "deployment" });
        var text = section.GetProperty("content").GetString()!;
        Assert.Equal("deployment", section.GetProperty("section").GetString());
        Assert.Contains("Run the pipeline", text);
        // The point of the feature: the rest of the page is not spent.
        Assert.DoesNotContain("An introduction", text);
        Assert.DoesNotContain("Ask the team", text);
        // The outline still comes back, so a follow-up can pick another one.
        Assert.Equal(2, section.GetProperty("outline").GetArrayLength());
    }

    [Fact]
    public async Task Asking_for_a_section_that_is_not_there_says_which_ones_are()
    {
        var (f, mcp, pageId) = await World();
        using var _ = f;

        var error = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(
            () => Call(mcp, "get_page", new { pageId, section = "nope" }));
        Assert.Contains("deployment", error.Message);
        Assert.Contains("support", error.Message);
    }

    [Fact]
    public async Task A_search_hit_shows_the_passage_that_matched()
    {
        var (f, mcp, pageId) = await World();
        using var _ = f;

        var results = await Call(mcp, "search_pages", new { query = "pineapple" });
        var hits = results.ValueKind == JsonValueKind.Array ? results : results.GetProperty("result");
        var hit = hits.EnumerateArray().Single();

        Assert.Equal(pageId, hit.GetProperty("id").GetGuid());
        var snippet = hit.GetProperty("snippet").GetString()!;
        Assert.Contains("pineapple", snippet, StringComparison.OrdinalIgnoreCase);
        // Not the page's opening line, which is what it used to return.
        Assert.DoesNotContain("An introduction that mentions", snippet);
        // Postgres ranks and reports a score; the test provider cannot, and a
        // null is omitted rather than sent as a fake 0: "unranked" is not a
        // score of zero. A client must treat it as optional.
        Assert.True(!hit.TryGetProperty("score", out var score) || score.ValueKind == JsonValueKind.Number);
    }
}
