using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

public class SpaceTests
{
    private record SpaceResponse(
        Guid Id, string Key, string Name, string? Description,
        bool Archived, Guid? HomepageId, DateTimeOffset CreatedAt);

    [Fact]
    public async Task Create_requires_authentication()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/spaces",
            new { Key = "ENG", Name = "Engineering", Description = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_then_get_and_list()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var create = await client.PostAsJsonAsync("/api/spaces",
            new { Key = "eng", Name = "Engineering", Description = "Team space" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var space = await create.Content.ReadFromJsonAsync<SpaceResponse>();
        Assert.Equal("ENG", space!.Key); // upper-cased

        var fetched = await client.GetFromJsonAsync<SpaceResponse>("/api/spaces/eng");
        Assert.Equal(space.Id, fetched!.Id);

        var list = await client.GetFromJsonAsync<List<SpaceResponse>>("/api/spaces");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Create_rejects_invalid_and_duplicate_keys()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var bad = await client.PostAsJsonAsync("/api/spaces",
            new { Key = "1BAD", Name = "Bad", Description = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        await client.PostAsJsonAsync("/api/spaces",
            new { Key = "OPS", Name = "Ops", Description = (string?)null });
        var dup = await client.PostAsJsonAsync("/api/spaces",
            new { Key = "ops", Name = "Ops again", Description = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Update_and_archive_flow()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        await client.PostAsJsonAsync("/api/spaces",
            new { Key = "DOCS", Name = "Docs", Description = (string?)null });

        var updated = await client.PutAsJsonAsync("/api/spaces/DOCS",
            new { Name = "Documentation", Description = "All the docs" });
        var body = await updated.Content.ReadFromJsonAsync<SpaceResponse>();
        Assert.Equal("Documentation", body!.Name);

        // Archived spaces drop out of the default list but come back with the flag.
        await client.PostAsync("/api/spaces/DOCS/archive", null);
        Assert.Empty((await client.GetFromJsonAsync<List<SpaceResponse>>("/api/spaces"))!);
        Assert.Single((await client.GetFromJsonAsync<List<SpaceResponse>>("/api/spaces?includeArchived=true"))!);

        await client.PostAsync("/api/spaces/DOCS/unarchive", null);
        Assert.Single((await client.GetFromJsonAsync<List<SpaceResponse>>("/api/spaces"))!);
    }
}
