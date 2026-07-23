using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class AuditTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record AuditEntry(
        Guid Id, string Action, string TargetType, Guid? TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson, DateTimeOffset CreatedAt);

    private const string Doc = """{"type":"doc","content":[]}""";

    [Fact]
    public async Task Page_lifecycle_actions_are_recorded_with_the_actor()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();

        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Audited", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        await client.PutAsJsonAsync($"/api/pages/{page!.Id}",
            new { Title = "Audited v2", ContentJson = Doc, ChangeComment = (string?)null });
        await client.DeleteAsync($"/api/pages/{page.Id}");
        await client.PostAsync($"/api/pages/{page.Id}/restore", null);

        var entries = await client.GetFromJsonAsync<List<AuditEntry>>($"/api/audit?targetId={page.Id}");
        var actions = entries!.Select(e => e.Action).ToList();

        Assert.Contains("page.created", actions);
        Assert.Contains("page.updated", actions);
        Assert.Contains("page.trashed", actions);
        Assert.Contains("page.restored", actions);

        // Entries carry the acting user and useful context.
        Assert.All(entries!, e => Assert.Equal(userId, e.ActorId));
        Assert.All(entries!, e => Assert.NotNull(e.ActorName));
        Assert.Contains(entries!, e => e.Action == "page.created" && e.MetadataJson!.Contains("Audited"));
    }

    [Fact]
    public async Task Space_creation_and_archiving_are_recorded()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        await client.PostAsJsonAsync("/api/spaces", new { Key = "AUD", Name = "Audit Space" });
        await client.PostAsync("/api/spaces/AUD/archive", null);

        var entries = await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit?targetType=space");
        var actions = entries!.Select(e => e.Action).ToList();
        Assert.Contains("space.created", actions);
        Assert.Contains("space.archived", actions);
    }

    [Fact]
    public async Task Newest_entries_come_first_and_take_is_honoured()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/pages",
                new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = $"P{i}", ContentJson = Doc });
        }

        var all = await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit?targetType=page");
        Assert.True(all!.Count >= 3);
        Assert.True(all[0].CreatedAt >= all[^1].CreatedAt); // newest first

        var limited = await client.GetFromJsonAsync<List<AuditEntry>>("/api/audit?targetType=page&take=2");
        Assert.Equal(2, limited!.Count);
    }

    [Fact]
    public async Task Audit_requires_authentication()
    {
        using var factory = new TestAppFactory();
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/audit")).StatusCode);
    }
}
