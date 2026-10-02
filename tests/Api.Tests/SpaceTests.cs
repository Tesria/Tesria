using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tesria.Api.Infrastructure;
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

    [Fact]
    public async Task A_name_over_the_limit_is_a_400_naming_the_limit()
    {
        // QA T3-005: the database refused it, and the answer was a bare 500.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var tooLong = await client.PostAsJsonAsync("/api/spaces", new { Key = "LONG", Name = new string('n', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("at most 200 characters", await tooLong.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/spaces", new { Key = "EXACT", Name = new string('n', 200) })).StatusCode);
        var rename = await client.PutAsJsonAsync("/api/spaces/EXACT", new { Name = new string('m', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Contains("at most 200 characters", await rename.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_description_over_the_limit_is_a_400_naming_the_limit()
    {
        // QA t3-R05: a 100,000-character description was saved and shown in full.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var tooLong = await client.PostAsJsonAsync("/api/spaces",
            new { Key = "LONG", Name = "Long", Description = new string('d', 2001) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("at most 2,000 characters", await tooLong.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/spaces",
            new { Key = "EXACT", Name = "Exact", Description = new string('d', 2000) })).StatusCode);
        var edit = await client.PutAsJsonAsync("/api/spaces/EXACT",
            new { Name = "Exact", Description = new string('e', 100_000) });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Contains("at most 2,000 characters", await edit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_description_from_before_the_limit_still_loads_and_may_be_sent_back_unchanged()
    {
        // The Details form sends the description with every rename: a space
        // given a long one before the limit must stay renamable.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        await client.PostAsJsonAsync("/api/spaces", new { Key = "OLD", Name = "Old" });
        var legacy = new string('x', 100_000);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var space = await db.Spaces.SingleAsync(s => s.Key == "OLD");
            space.Description = legacy;
            await db.SaveChangesAsync();
        }

        Assert.Equal(legacy, (await client.GetFromJsonAsync<SpaceResponse>("/api/spaces/OLD"))!.Description);
        var rename = await client.PutAsJsonAsync("/api/spaces/OLD", new { Name = "Renamed", Description = legacy });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("Renamed", (await rename.Content.ReadFromJsonAsync<SpaceResponse>())!.Name);
        // Changing it holds it to the limit.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/spaces/OLD",
            new { Name = "Renamed", Description = legacy + "y" })).StatusCode);
    }

    private record RightsResponse(string Key, bool? CanEdit, bool? CanAdmin);

    [Fact]
    public async Task Reading_a_space_says_what_the_caller_may_do_in_it()
    {
        // QA T3-012: a viewer was offered New Page, reordering and the
        // space's settings, each refused only after use.
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        await owner.PostAsJsonAsync("/api/spaces", new { Key = "RIGHTS", Name = "Rights" });
        var viewer = factory.CreateClient();
        var viewerId = await viewer.RegisterAndSignInAsync();
        var editor = factory.CreateClient();
        var editorId = await editor.RegisterAndSignInAsync();
        // User = 0 | View = 0, Edit = 1, Admin = 2
        await owner.MakePrivateAsync("RIGHTS");
        await owner.PostAsJsonAsync("/api/spaces/RIGHTS/permissions", new { PrincipalType = 0, PrincipalId = viewerId, Operation = 0 });
        await owner.PostAsJsonAsync("/api/spaces/RIGHTS/permissions", new { PrincipalType = 0, PrincipalId = editorId, Operation = 1 });

        var asOwner = await owner.GetFromJsonAsync<RightsResponse>("/api/spaces/RIGHTS");
        Assert.True(asOwner!.CanEdit);
        Assert.True(asOwner.CanAdmin);
        var asEditor = await editor.GetFromJsonAsync<RightsResponse>("/api/spaces/RIGHTS");
        Assert.True(asEditor!.CanEdit);
        Assert.False(asEditor.CanAdmin);
        var asViewer = await viewer.GetFromJsonAsync<RightsResponse>("/api/spaces/RIGHTS");
        Assert.False(asViewer!.CanEdit);
        Assert.False(asViewer.CanAdmin);
    }
}
