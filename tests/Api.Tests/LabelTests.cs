using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class LabelTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record LabelResponse(Guid Id, string Name);
    private record LabelUsage(Guid Id, string Name, int PageCount);
    private record LabelledPage(Guid PageId, Guid SpaceId, string SpaceKey, string Title);

    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<(TestAppFactory, HttpClient, Guid spaceId)> NewClientWithSpace()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        return (factory, client, await client.CreateSpaceAsync());
    }

    private static async Task<PageDetail> NewPage(HttpClient client, Guid spaceId, string title)
    {
        var res = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc });
        return (await res.Content.ReadFromJsonAsync<PageDetail>())!;
    }

    [Fact]
    public async Task Add_list_and_remove_labels_on_a_page()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await NewPage(client, spaceId, "Runbook");

        // Names are normalized to lower case.
        var added = await (await client.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = "OnCall" }))
            .Content.ReadFromJsonAsync<LabelResponse>();
        Assert.Equal("oncall", added!.Name);

        await client.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = "runbook" });

        var labels = await client.GetFromJsonAsync<List<LabelResponse>>($"/api/pages/{page.Id}/labels");
        Assert.Equal(2, labels!.Count);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/pages/{page.Id}/labels/oncall")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<LabelResponse>>($"/api/pages/{page.Id}/labels"))!);
    }

    [Fact]
    public async Task Adding_the_same_label_twice_is_idempotent()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await NewPage(client, spaceId, "P");

        await client.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = "docs" });
        await client.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = "docs" });

        Assert.Single((await client.GetFromJsonAsync<List<LabelResponse>>($"/api/pages/{page.Id}/labels"))!);
    }

    [Fact]
    public async Task Invalid_label_names_are_rejected()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await NewPage(client, spaceId, "P");

        foreach (var bad in new[] { "", "  ", "has space", "-leading", new string('x', 51) })
        {
            var res = await client.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = bad });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task Browse_pages_by_label_and_see_usage_counts()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var a = await NewPage(client, spaceId, "Alpha");
        var b = await NewPage(client, spaceId, "Beta");
        await client.PostAsJsonAsync($"/api/pages/{a.Id}/labels", new { Name = "shared" });
        await client.PostAsJsonAsync($"/api/pages/{b.Id}/labels", new { Name = "shared" });

        var pages = await client.GetFromJsonAsync<List<LabelledPage>>("/api/labels/shared/pages");
        Assert.Equal(2, pages!.Count);

        var usage = await client.GetFromJsonAsync<List<LabelUsage>>("/api/labels");
        Assert.Equal(2, usage!.Single(u => u.Name == "shared").PageCount);
    }

    [Fact]
    public async Task Trashed_pages_drop_out_of_label_listings()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var page = await NewPage(client, spaceId, "Doomed");
        await client.PostAsJsonAsync($"/api/pages/{page.Id}/labels", new { Name = "temp" });
        Assert.Single((await client.GetFromJsonAsync<List<LabelledPage>>("/api/labels/temp/pages"))!);

        await client.DeleteAsync($"/api/pages/{page.Id}");

        Assert.Empty((await client.GetFromJsonAsync<List<LabelledPage>>("/api/labels/temp/pages"))!);
        var usage = await client.GetFromJsonAsync<List<LabelUsage>>("/api/labels");
        Assert.DoesNotContain(usage!, u => u.Name == "temp");
    }
}
