using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class TemplateTests
{
    private record TemplateResponse(
        Guid Id, Guid? SpaceId, string Name, string? Description, string ContentJson,
        Guid CreatedById, DateTimeOffset CreatedAt);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"template body"}]}]}""";

    [Fact]
    public async Task Create_and_list_instance_wide_template()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var created = await (await client.PostAsJsonAsync("/api/templates",
            new { SpaceId = (Guid?)null, Name = "Meeting Notes", Description = "1:1 template", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<TemplateResponse>();
        Assert.Null(created!.SpaceId);

        var list = await client.GetFromJsonAsync<List<TemplateResponse>>("/api/templates");
        Assert.Contains(list!, t => t.Id == created.Id);
    }

    [Fact]
    public async Task Space_scoped_template_only_appears_for_that_space()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceA = await client.CreateSpaceAsync();
        var spaceB = await client.CreateSpaceAsync();

        var created = await (await client.PostAsJsonAsync("/api/templates",
            new { SpaceId = spaceA, Name = "Space Template", Description = (string?)null, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<TemplateResponse>();
        Assert.Equal(spaceA, created!.SpaceId);

        var forA = await client.GetFromJsonAsync<List<TemplateResponse>>($"/api/templates?spaceId={spaceA}");
        Assert.Contains(forA!, t => t.Id == created.Id);

        var forB = await client.GetFromJsonAsync<List<TemplateResponse>>($"/api/templates?spaceId={spaceB}");
        Assert.DoesNotContain(forB!, t => t.Id == created.Id);

        // Without a spaceId filter, only instance-wide templates are listed.
        var instanceWide = await client.GetFromJsonAsync<List<TemplateResponse>>("/api/templates");
        Assert.DoesNotContain(instanceWide!, t => t.Id == created.Id);
    }

    [Fact]
    public async Task Create_rejects_invalid_input()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/templates",
            new { SpaceId = (Guid?)null, Name = "  ", Description = (string?)null, ContentJson = Doc })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/templates",
            new { SpaceId = (Guid?)null, Name = "X", Description = (string?)null, ContentJson = "{ not json" })).StatusCode);
    }

    [Fact]
    public async Task Space_scoped_template_requires_edit_rights_on_the_space()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();

        // Lock the space so Bob only has View.
        var spaces = await alice.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var space = spaces!.Single(s => s.Id == spaceId);

        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = 0, PrincipalId = aliceId, Operation = 2 });
        await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = 0, PrincipalId = bobId, Operation = 0 });

        var res = await bob.PostAsJsonAsync("/api/templates",
            new { SpaceId = spaceId, Name = "Blocked", Description = (string?)null, ContentJson = Doc });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Only_the_author_can_delete_an_instance_wide_template()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var created = await (await alice.PostAsJsonAsync("/api/templates",
            new { SpaceId = (Guid?)null, Name = "Mine", Description = (string?)null, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<TemplateResponse>();

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/templates/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/templates/{created.Id}")).StatusCode);
    }

    private record SpaceDto(Guid Id, string Key, string Name);
}
