using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Features.Search;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Search on real PostgreSQL (dev-plan 23.1): BM25 order over the full-text
/// index, accents, snippets, and the search text rebuilt on upgrade. SQLite
/// has no full-text search, so <see cref="SearchTests"/> covers only the
/// fallback; these are skipped unless <c>TESRIA_TEST_POSTGRES</c> is set. The
/// app runs as its least-privilege role here, so they also show search needs
/// no grant it lacks.
/// </summary>
[Collection("Postgres")]
public class SearchPostgresTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record SearchResult(Guid PageId, Guid SpaceId, string SpaceKey, string Title, string Snippet);

    private static string Doc(params string[] paragraphs) =>
        $$"""{"type":"doc","content":[{{string.Join(",", paragraphs.Select(p =>
            $$"""{"type":"paragraph","content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(p)}}}]}"""))}}]}""";

    private static async Task<PageDetail> CreatePage(HttpClient client, Guid spaceId, string title, params string[] body)
    {
        var res = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc(body) });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PageDetail>())!;
    }

    private static async Task<List<SearchResult>> Search(HttpClient client, string q) =>
        (await client.GetFromJsonAsync<List<SearchResult>>($"/api/search?q={Uri.EscapeDataString(q)}"))!;

    private static string Filler(int words) => string.Join(" ", Enumerable.Range(0, words).Select(i => $"filler{i % 40}"));

    [PostgresFact]
    public async Task A_page_titled_with_the_word_ranks_above_pages_that_only_mention_it()
    {
        // t6-019: "Working offline" was 6th for "offline", below pages that mention it in passing.
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var space = await client.CreateSpaceAsync();

        await CreatePage(client, space, "Launch plan", "Ships with offline editing.", "Offline editing that merges. Offline first.", Filler(60));
        await CreatePage(client, space, "Architecture overview", "Offline edits are replayed in order.", Filler(80));
        await CreatePage(client, space, "Working offline", "Keep editing; changes are sent when you reconnect.");
        await CreatePage(client, space, "Unrelated", "Nothing to see.");

        var results = await Search(client, "offline");
        Assert.Equal(3, results.Count);
        Assert.Equal("Working offline", results[0].Title);
        // Among the pages that only mention it, more mentions in a shorter page first.
        Assert.Equal(["Launch plan", "Architecture overview"], results.Skip(1).Select(r => r.Title));
    }

    [PostgresFact]
    public async Task A_rare_word_counts_for_more_and_a_short_page_beats_a_long_one()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var space = await client.CreateSpaceAsync();

        // "widget" is on every page; "quokka" on one.
        for (var i = 0; i < 6; i++) await CreatePage(client, space, $"Note {i}", "A widget, another widget, a third widget.", Filler(20));
        await CreatePage(client, space, "Animals", "One widget and a quokka.", Filler(20));

        // Either word will do: the page with the rare one comes first, even
        // though the others mention the common one three times as often.
        var either = await Search(client, "widget or quokka");
        Assert.Equal(7, either.Count);
        Assert.Equal("Animals", either[0].Title);

        // Same mentions, different lengths: the focused page first.
        var other = await client.CreateSpaceAsync();
        await CreatePage(client, other, "Long", "zebra", Filler(400));
        await CreatePage(client, other, "Short", "zebra", Filler(10));
        Assert.Equal(["Short", "Long"], (await Search(client, "zebra")).Select(r => r.Title));
    }

    [PostgresFact]
    public async Task Accents_do_not_matter_and_the_snippet_keeps_them()
    {
        // t6-R02: "naive" and "cafe" found nothing; only "naïve" did.
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var space = await client.CreateSpaceAsync();
        await CreatePage(client, space, "Ünïcödé naïve café", "Meet at the café on Rue Dvořák.");

        foreach (var q in new[] { "naive", "cafe", "café", "CAFE", "unicode", "dvorak", "Dvořák" })
            Assert.Equal("Ünïcödé naïve café", Assert.Single(await Search(client, q)).Title);

        var snippet = Assert.Single(await Search(client, "cafe")).Snippet;
        Assert.Contains("**café**", snippet);
        Assert.Contains("Dvořák", snippet);
    }

    [PostgresFact]
    public async Task Slash_joined_words_are_found_apart_and_shown_together()
    {
        // Postgres's tsvector parser treats "word/word" (e.g. "Hocuspocus/Yjs")
        // as one compound lexeme rather than splitting it, so a search for
        // just "Hocuspocus" found nothing. The index reads a slash as a space
        // (SearchIndex.Fold); since 0.9 the snippet still shows the slash.
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var space = await client.CreateSpaceAsync();
        await CreatePage(client, space, "Collab/Sync", "A Node + Hocuspocus/Yjs sidecar");

        foreach (var q in new[] { "Hocuspocus", "Yjs", "Hocuspocus/Yjs", "collab", "sync" })
            Assert.Single(await Search(client, q));
        Assert.Contains("**Hocuspocus**/Yjs", Assert.Single(await Search(client, "hocuspocus")).Snippet);
    }

    [PostgresFact]
    public async Task Snippets_separate_blocks_and_show_chips_and_filters_still_apply()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await alice.CreateSpaceAsync();
        var content = """
            {"type":"doc","content":[
              {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Attendees"}]},
              {"type":"paragraph","content":[{"type":"mention","attrs":{"id":"x","label":"Priya Natarajan"}},{"type":"text","text":" on "},{"type":"date","attrs":{"date":"2026-09-02"}}]}
            ]}
            """;
        (await alice.PostAsJsonAsync("/api/pages", new { SpaceId = space, Title = "Kickoff", ContentJson = content })).EnsureSuccessStatusCode();

        // T5-026: a mentioned person's name finds the page, and the snippet shows it.
        var hit = Assert.Single(await Search(alice, "priya"));
        Assert.StartsWith("Attendees · @**Priya** Natarajan", hit.Snippet);
        Assert.Single(await Search(alice, "kickoff"));

        // Space scoping and permissions are as before: another space, and a
        // space Bob may not see, contribute nothing.
        var elsewhere = await alice.CreateSpaceAsync();
        await CreatePage(alice, elsewhere, "Priya's notes", "Priya again.");
        Assert.Equal(2, (await Search(alice, "priya")).Count);
        Assert.Single((await alice.GetFromJsonAsync<List<SearchResult>>($"/api/search?q=priya&spaceId={space}"))!);

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(2, (await Search(bob, "priya")).Count);
        (await alice.PostAsJsonAsync($"/api/spaces/{await KeyOf(alice, space)}/permissions",
            new { PrincipalType = 0, PrincipalId = aliceId, Operation = 0 })).EnsureSuccessStatusCode();
        Assert.Equal("Priya's notes", Assert.Single(await Search(bob, "priya")).Title);

        // And a page restricted to Alice in a space Bob can see is still hers alone.
        var notes = (await Search(alice, "notes")).Single();
        (await alice.PostAsJsonAsync($"/api/pages/{notes.PageId}/restrictions",
            new { PrincipalType = 0, PrincipalId = aliceId, Operation = 0 })).EnsureSuccessStatusCode();
        Assert.Empty(await Search(bob, "priya"));
        Assert.Equal(2, (await Search(alice, "priya")).Count);
    }

    private static async Task<string> KeyOf(HttpClient client, Guid spaceId) =>
        (await client.GetFromJsonAsync<List<TestHelpers.SpaceDto>>("/api/spaces"))!.Single(s => s.Id == spaceId).Key;

    [PostgresFact]
    public async Task An_upgrade_rebuilds_the_search_text_of_every_page_once()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var space = await client.CreateSpaceAsync();
        var page = await CreatePage(client, space, "Old page", "First block", "Second block");

        // As 0.8 left it: the title in front, blocks run together.
        await using (var owner = pg.Owner())
        {
            await owner.Pages.Where(p => p.Id == page.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SearchText, "Old page First block Second block "));
            await owner.SiteSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.SearchTextVersion, 0));

            Assert.Equal(1, await SearchTextBackfill.RunAsync(owner));
            Assert.Equal("First block\nSecond block", await owner.Pages.Where(p => p.Id == page.Id).Select(p => p.SearchText).SingleAsync());
            Assert.Equal(SearchableText.Version, (await owner.SiteSettings.SingleAsync()).SearchTextVersion);

            // Once only: a second pass finds nothing to do.
            Assert.Equal(0, await SearchTextBackfill.RunAsync(owner));
        }

        // The title is still found, from its own column.
        Assert.Equal("Old page", Assert.Single(await Search(client, "old")).Title);
    }

    [PostgresFact]
    public async Task The_mcp_search_ranks_the_same_way_and_scores_by_bm25()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var space = await client.CreateSpaceAsync();
        await CreatePage(client, space, "Mentions", "glossary glossary glossary", Filler(50));
        await CreatePage(client, space, "Glossary", "Words and what they mean.");

        Assert.Equal(["Glossary", "Mentions"], (await Search(client, "glossary")).Select(r => r.Title));

        var token = await (await client.PostAsJsonAsync("/api/api-tokens", new { Name = "tools", ReadOnly = true }))
            .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var mcp = factory.CreateClient();
        mcp.DefaultRequestHeaders.Authorization = new("Bearer", token.GetProperty("token").GetString());
        mcp.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        var res = await mcp.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "search_pages", arguments = new { query = "glossary" } },
        });
        var body = await res.Content.ReadAsStringAsync();
        if (res.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = string.Join("\n", body.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()));
        var root = System.Text.Json.JsonDocument.Parse(body).RootElement;
        Assert.True(root.TryGetProperty("result", out var called), body);
        Assert.False(called.TryGetProperty("isError", out var isError) && isError.GetBoolean(), body);
        var result = called.TryGetProperty("structuredContent", out var structured)
            ? structured
            : System.Text.Json.JsonDocument.Parse(called.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
        var hits = (result.ValueKind == System.Text.Json.JsonValueKind.Array ? result : result.GetProperty("result")).EnumerateArray().ToList();
        Assert.Equal(["Glossary", "Mentions"], hits.Select(h => h.GetProperty("title").GetString()));
        var scores = hits.Select(h => h.GetProperty("score").GetDouble()).ToList();
        Assert.True(scores[0] > scores[1] && scores[1] > 0);
    }
}
