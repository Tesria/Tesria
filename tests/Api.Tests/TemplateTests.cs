using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

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
    // Since 2026-09-22 anyone holding "Manage spaces" may remove one too (see
    // below); a member who is neither the author nor a manager still cannot.
    public async Task A_member_who_did_not_write_an_instance_wide_template_cannot_delete_it()
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

    // --- Managing templates (dev-plan 10.5 step 1): rename, delete, and who may.

    private record TemplateDto(Guid Id, Guid? SpaceId, string Name, string? Description,
        string? CreatedByName, bool CanManage);


    private static async Task<TemplateDto> CreateAsync(HttpClient client, Guid? spaceId, string name)
    {
        var res = await client.PostAsJsonAsync("/api/templates",
            new { SpaceId = spaceId, Name = name, Description = (string?)null, ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<TemplateDto>())!;
    }

    [Fact]
    public async Task The_author_can_rename_an_instance_wide_template_and_another_member_cannot()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var author = factory.CreateClient();
        await author.RegisterAndSignInAsync();
        var other = factory.CreateClient();
        await other.RegisterAndSignInAsync();

        var t = await CreateAsync(author, null, "Meeting notes");

        var renamed = await author.PutAsJsonAsync($"/api/templates/{t.Id}", new { Name = "  Weekly meeting  ", Description = "Agenda first" });
        renamed.EnsureSuccessStatusCode();
        var body = (await renamed.Content.ReadFromJsonAsync<TemplateDto>())!;
        Assert.Equal("Weekly meeting", body.Name);
        Assert.Equal("Agenda first", body.Description);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.PutAsJsonAsync($"/api/templates/{t.Id}", new { Name = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/templates/{t.Id}")).StatusCode);

        // And the list says so, per viewer, so the screen offers only what works.
        var seenByOther = await other.GetFromJsonAsync<List<TemplateDto>>("/api/templates");
        Assert.False(seenByOther!.Single(x => x.Id == t.Id).CanManage);
        var seenByAuthor = await author.GetFromJsonAsync<List<TemplateDto>>("/api/templates");
        Assert.True(seenByAuthor!.Single(x => x.Id == t.Id).CanManage);
        Assert.NotNull(seenByAuthor!.Single(x => x.Id == t.Id).CreatedByName);
    }

    [Fact]
    public async Task Someone_who_manages_spaces_can_remove_an_instance_wide_template_they_did_not_write()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var author = factory.CreateClient();
        await author.RegisterAndSignInAsync();

        var t = await CreateAsync(author, null, "Left behind");
        Assert.True((await owner.GetFromJsonAsync<List<TemplateDto>>("/api/templates"))!.Single(x => x.Id == t.Id).CanManage);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/templates/{t.Id}")).StatusCode);
        Assert.DoesNotContain(await owner.GetFromJsonAsync<List<TemplateDto>>("/api/templates") ?? [], x => x.Id == t.Id);
    }

    [Fact]
    public async Task A_spaces_templates_are_managed_by_anyone_who_can_edit_it()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var member = factory.CreateClient();
        await member.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync("TPL");

        var t = await CreateAsync(owner, spaceId, "Runbook");
        // An open space: every signed-in user may edit it, so may manage its templates.
        var res = await member.PutAsJsonAsync($"/api/templates/{t.Id}", new { Name = "Runbook v2" });
        res.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await member.PutAsJsonAsync($"/api/templates/{t.Id}", new { Name = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await member.PutAsJsonAsync($"/api/templates/{Guid.NewGuid()}", new { Name = "x" })).StatusCode);
    }
}
