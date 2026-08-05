using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

public class SearchTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record SearchResult(Guid PageId, Guid SpaceId, string SpaceKey, string Title, string Snippet);

    private static string Doc(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    private static Task<PageDetail?> CreatePage(HttpClient client, Guid spaceId, string title, string body) =>
        client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc(body) })
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<PageDetail>()).Unwrap();

    [Fact]
    public async Task Finds_pages_by_title_and_content()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();

        await CreatePage(client, spaceId, "Getting Started", "onboarding and laptop setup");
        await CreatePage(client, spaceId, "Deployment Guide", "kubernetes and docker rollout");

        // Match on content...
        var byContent = await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q=laptop");
        Assert.Single(byContent!);
        Assert.Equal("Getting Started", byContent![0].Title);

        // ...and on title.
        var byTitle = await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q=Deployment");
        Assert.Single(byTitle!);
        Assert.Equal("Deployment Guide", byTitle![0].Title);

        // A term in neither page returns nothing.
        Assert.Empty((await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q=zzzznomatch"))!);
    }

    [Fact]
    public async Task Empty_query_returns_no_results()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        await CreatePage(client, spaceId, "Something", "content here");

        Assert.Empty((await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q="))!);
    }

    [Fact]
    public async Task Trashed_pages_are_excluded_from_search()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await CreatePage(client, spaceId, "Secret Plans", "confidential widget roadmap");

        Assert.Single((await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q=widget"))!);

        await client.DeleteAsync($"/api/pages/{page!.Id}");
        Assert.Empty((await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q=widget"))!);
    }

    [Fact]
    public async Task Slash_joined_words_are_indexed_as_separate_terms()
    {
        // Postgres's tsvector parser treats "word/word" (e.g. "Hocuspocus/Yjs")
        // as one compound lexeme rather than splitting it, so a search for
        // just "Hocuspocus" would otherwise find nothing. SearchText must have
        // the slash replaced with a space so both halves tokenize normally —
        // this can only be verified against the stored text directly since the
        // SQLite test provider falls back to a plain LIKE match, which can't
        // reproduce the tsvector-specific bug.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await CreatePage(client, spaceId, "Collab", "A Node + Hocuspocus/Yjs sidecar");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var searchText = await db.Pages.Where(p => p.Id == page!.Id).Select(p => p.SearchText).SingleAsync();
        Assert.DoesNotContain('/', searchText);
        Assert.Contains("Hocuspocus Yjs", searchText);
    }

    [Fact]
    public async Task Search_can_be_scoped_to_a_space()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceA = await client.CreateSpaceAsync();
        var spaceB = await client.CreateSpaceAsync();
        await CreatePage(client, spaceA, "Alpha", "shared keyword apple");
        await CreatePage(client, spaceB, "Beta", "shared keyword apple");

        Assert.Equal(2, (await client.GetFromJsonAsync<List<SearchResult>>("/api/search?q=apple"))!.Count);
        Assert.Single((await client.GetFromJsonAsync<List<SearchResult>>($"/api/search?q=apple&spaceId={spaceA}"))!);
    }
}
