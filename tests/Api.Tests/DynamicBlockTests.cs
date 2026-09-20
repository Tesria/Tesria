using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Features.Blocks;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The dynamic-block mechanism (dev-plan Phase 7 Wave D), proven against
/// the reference kind. The leak test is the one that matters: a page the
/// caller cannot view must not influence a block's result at all.
/// </summary>
public class DynamicBlockTests
{
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record Item(string Title, string? Href, string? Subtitle, List<Item>? Children);
    private record Result(string Kind, string Shape, List<Item> Items, string? Empty, DateTimeOffset GeneratedAt);

    private const int User = 0, View = 0;
    private const string Plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";

    private static string WithChildrenBlock(string depth = "2") =>
        "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Sections:\"}]},"
        + "{\"type\":\"dynamicBlock\",\"attrs\":{\"kind\":\"children\",\"params\":{\"depth\":\"" + depth + "\"}}}]}";

    private static async Task<SpaceDto> NewSpace(HttpClient c)
    {
        var res = await c.PostAsJsonAsync("/api/spaces", new { Key = $"B{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}", Name = "Blocks", Description = (string?)null });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SpaceDto>())!;
    }

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title, Guid? parent = null, string? content = null) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = content ?? Plain }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    /// <summary>Alice's space: Home → [Open (→ Deep), Secret (→ Hidden child)]; Secret is restricted to Alice.</summary>
    private static async Task<(TestAppFactory f, HttpClient alice, HttpClient bob, SpaceDto space, PageDetail home, PageDetail open, PageDetail deep, PageDetail secret)> Fixture()
    {
        var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();

        var space = await NewSpace(alice);
        var home = await NewPage(alice, space.Id, "Home", content: WithChildrenBlock());
        var open = await NewPage(alice, space.Id, "Open", home.Id);
        var deep = await NewPage(alice, space.Id, "Deep", open.Id);
        var secret = await NewPage(alice, space.Id, "Secret", home.Id);
        await NewPage(alice, space.Id, "Hidden child", secret.Id);
        (await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View })).EnsureSuccessStatusCode();
        return (factory, alice, bob, space, home, open, deep, secret);
    }

    [Fact]
    public async Task Children_lists_the_visible_subtree_to_the_requested_depth()
    {
        var (f, alice, _, space, home, open, deep, secret) = await Fixture();
        using var _ = f;

        var result = await alice.GetFromJsonAsync<Result>($"/api/pages/{home.Id}/blocks/children?depth=2");
        Assert.Equal("list", result!.Shape);
        Assert.Equal(["Open", "Secret"], result.Items.Select(i => i.Title));
        Assert.Equal($"/spaces/{space.Key}/pages/{open.Id}", result.Items[0].Href);
        Assert.Equal(["Deep"], result.Items[0].Children!.Select(i => i.Title));
        Assert.Equal($"/spaces/{space.Key}/pages/{deep.Id}", result.Items[0].Children![0].Href);
        Assert.Equal(["Hidden child"], result.Items[1].Children!.Select(i => i.Title));

        // depth=1 stops at the children themselves.
        var shallow = await alice.GetFromJsonAsync<Result>($"/api/pages/{home.Id}/blocks/children?depth=1");
        Assert.All(shallow!.Items, i => Assert.Null(i.Children));
        GC.KeepAlive(secret);
    }

    [Fact]
    public async Task A_page_the_caller_cannot_view_is_absent_from_the_result_along_with_its_subtree()
    {
        var (f, _, bob, _, home, _, _, _) = await Fixture();
        using var _ = f;

        var result = await bob.GetFromJsonAsync<Result>($"/api/pages/{home.Id}/blocks/children?depth=3");
        Assert.Equal(["Open"], result!.Items.Select(i => i.Title));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Secret", json);
        Assert.DoesNotContain("Hidden child", json);
    }

    [Fact]
    public async Task An_unviewable_host_is_not_found_and_an_anonymous_caller_gets_the_same_answer()
    {
        var (f, alice, bob, space, home, _, _, secret) = await Fixture();
        using var _ = f;

        // Bob may not view Secret, so he may not ask for its children either.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{secret.Id}/blocks/children")).StatusCode);

        // A private space is invisible to the world.
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/pages/{home.Id}/blocks/children")).StatusCode);
        GC.KeepAlive((alice, space));
    }

    [Fact]
    public async Task Bad_kinds_and_parameters_are_400s_naming_the_field()
    {
        var (f, alice, _, _, home, _, _, _) = await Fixture();
        using var _ = f;

        var unknown = await alice.GetAsync($"/api/pages/{home.Id}/blocks/no-such-kind");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("kind", await unknown.Content.ReadAsStringAsync());

        var badSort = await alice.GetAsync($"/api/pages/{home.Id}/blocks/children?sort=sideways");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        Assert.Contains("sort", await badSort.Content.ReadAsStringAsync());

        // Out-of-range numbers are clamped, not rejected: a document written
        // against a looser server should still render.
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/pages/{home.Id}/blocks/children?depth=99")).StatusCode);
    }

    [Fact]
    public async Task Export_snapshots_the_block_as_the_exporting_user()
    {
        var (f, alice, bob, space, home, open, _, _) = await Fixture();
        using var _ = f;

        // Asserted through Markdown since 12.1: HTML and PDF are captured in
        // a browser, where the block is fetched by the page itself under the
        // render token. Markdown is still rendered here, and it is the same
        // snapshot, taken with the same permissions.
        var alicesMd = await (await alice.GetAsync($"/api/pages/{home.Id}/export?format=markdown")).Content.ReadAsStringAsync();
        Assert.Contains($"- [Open](https://localhost/spaces/{space.Key}/pages/{open.Id})", alicesMd);
        Assert.Contains("Secret", alicesMd);
        Assert.Contains("Snapshot taken", alicesMd);

        var bobsMd = await (await bob.GetAsync($"/api/pages/{home.Id}/export?format=markdown")).Content.ReadAsStringAsync();
        Assert.Contains($"- [Open](https://localhost/spaces/{space.Key}/pages/{open.Id})", bobsMd);
        Assert.DoesNotContain("Secret", bobsMd);
    }

    [Fact]
    public void Collect_finds_blocks_in_document_order_and_tolerates_junk()
    {
        var doc = """
        {"type":"doc","content":[
          {"type":"dynamicBlock","attrs":{"kind":"children","params":{"depth":2,"sort":"title"}}},
          {"type":"panel","content":[{"type":"dynamicBlock","attrs":{"kind":"labels","params":{"nested":{"x":1}}}}]},
          {"type":"dynamicBlock"}
        ]}
        """;
        var found = DynamicBlocks.Collect(doc);
        Assert.Equal(["children", "labels", ""], found.Select(p => p.Kind));
        Assert.Equal("2", found[0].Params["depth"]);
        Assert.Equal("title", found[0].Params["sort"]);
        Assert.Empty(found[1].Params); // a non-scalar value is dropped, not serialised
        Assert.Empty(DynamicBlocks.Collect("not json"));
    }

    [Fact]
    public void Renderer_draws_a_placeholder_when_no_snapshot_was_supplied()
    {
        var html = ProseMirrorRenderer.ToHtml(WithChildrenBlock());
        Assert.Contains("[children: dynamic content, shown on the page]", html);
        var md = ProseMirrorRenderer.ToMarkdown(WithChildrenBlock());
        Assert.Contains("_[children: dynamic content, shown on the page]_", md);
    }

    [Fact]
    public void Renderer_draws_the_neutral_shapes_and_does_not_recurse_into_included_documents()
    {
        var table = BlockResult.Table("t", [new("who", "Who"), new("when", "When"), new("done", "Done")],
            [new BlockItem("row", Cells: new Dictionary<string, BlockCell>
            {
                ["who"] = BlockCell.By(new BlockUser(Guid.NewGuid(), "Ana <b>", null, null)),
                ["when"] = BlockCell.On(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero)),
                ["done"] = BlockCell.Done(true),
            })]);
        var inner = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"included\"}]},"
                  + "{\"type\":\"dynamicBlock\",\"attrs\":{\"kind\":\"children\",\"params\":{}}}]}";
        var document = BlockResult.DocumentOf("include-page", inner);
        var doc = "{\"type\":\"doc\",\"content\":["
                + "{\"type\":\"dynamicBlock\",\"attrs\":{\"kind\":\"t\",\"params\":{}}},"
                + "{\"type\":\"dynamicBlock\",\"attrs\":{\"kind\":\"include-page\",\"params\":{}}}]}";

        var html = ProseMirrorRenderer.ToHtml(doc, [table, document], "https://wiki.example");
        Assert.Contains("<th>Who</th><th>When</th><th>Done</th>", html);
        Assert.Contains("<td>Ana &lt;b&gt;</td><td>10 Sep 2026</td><td>☑</td>", html);
        Assert.Contains("<p>included</p>", html);
        // The included document's own block is a placeholder — depth 1.
        Assert.Contains("[children: dynamic content, shown on the page]", html);

        var md = ProseMirrorRenderer.ToMarkdown(doc, [table, document]);
        Assert.Contains("| Who | When | Done |", md);
        Assert.Contains("| Ana <b> | 10 Sep 2026 | [x] |", md);
        Assert.Contains("included", md);
    }
}
