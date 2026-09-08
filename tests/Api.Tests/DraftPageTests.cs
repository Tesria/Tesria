using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The draft/publish page lifecycle: a page created via POST /pages/draft is
/// invisible (tree, search, trash) until POST /pages/{id}/publish fires the
/// same "page created" side effects Create fires today — exactly once.
/// </summary>
public class DraftPageTests
{
    private record DraftResponse(Guid Id);
    private record PageDetail(
        Guid Id, Guid SpaceId, Guid? ParentPageId, string Title, int Position, int Status,
        int CurrentVersionNumber, string ContentJson, bool FullWidth, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private record TreeNode(Guid Id, string Title, int Position, List<TreeNode> Children);
    private record TrashedPage(Guid Id, string Title, DateTimeOffset DeletedAt, Guid? DeletedById);
    private record AttachmentResponse(Guid Id, Guid PageId, string Filename);
    private record SpaceDto(Guid Id, string Key, string Name);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";

    private static async Task<(TestAppFactory, HttpClient, Guid)> NewClientWithSpace()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        return (factory, client, spaceId);
    }

    [Fact]
    public async Task Draft_is_invisible_in_tree_and_trash_until_published()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;

        var draft = await (await client.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<DraftResponse>();

        var tree = await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}");
        Assert.Empty(tree!);

        var trash = await client.GetFromJsonAsync<List<TrashedPage>>($"/api/pages/trash?spaceId={spaceId}");
        Assert.Empty(trash!);

        // Not directly fetchable as a normal page either.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/pages/{draft!.Id}")).StatusCode);

        var published = await (await client.PostAsJsonAsync($"/api/pages/{draft.Id}/publish",
            new { Title = "Real Title", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.Equal("Real Title", published!.Title);
        Assert.Equal(1, published.CurrentVersionNumber); // mutated v1 in place, not a new v2

        var treeAfter = await client.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={spaceId}");
        Assert.Single(treeAfter!);
        Assert.Equal("Real Title", treeAfter![0].Title);
    }

    [Fact]
    public async Task Publish_fires_page_created_side_effects_exactly_once()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var spaces = await client.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;
        await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "page.created" });

        var draft = await (await client.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<DraftResponse>();

        var sender = factory.Services.GetRequiredService<RecordingWebhookSender>();
        Assert.Empty(sender.Deliveries); // creating the draft fired nothing

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.AuditLogs.Where(a => a.TargetId == draft!.Id).ToListAsync());
        }

        await client.PostAsJsonAsync($"/api/pages/{draft!.Id}/publish",
            new { Title = "Hooked", ContentJson = Doc });

        Assert.Single(sender.Deliveries, d => d.PayloadJson.Contains("page.created"));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entries = await db.AuditLogs.Where(a => a.TargetId == draft.Id && a.Action == "page.created").ToListAsync();
            Assert.Single(entries);
        }

        // A retried/double-clicked publish is a safe no-op, not a second event.
        var again = await client.PostAsJsonAsync($"/api/pages/{draft.Id}/publish",
            new { Title = "Hooked Again", ContentJson = Doc });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(sender.Deliveries, d => d.PayloadJson.Contains("page.created"));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entries = await db.AuditLogs.Where(a => a.TargetId == draft.Id && a.Action == "page.created").ToListAsync();
            Assert.Single(entries); // still exactly one
        }
    }

    [Fact]
    public async Task An_image_can_be_uploaded_against_a_draft_before_publish()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var draft = await (await client.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<DraftResponse>();

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(bytes, "file", "pic.png");

        var upload = await client.PostAsync($"/api/pages/{draft!.Id}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.Equal(draft.Id, attachment!.PageId);
    }

    [Fact]
    public async Task DeleteDraft_removes_an_unpublished_draft_but_refuses_a_published_page()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var draft = await (await client.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<DraftResponse>();

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/pages/{draft!.Id}/draft")).StatusCode);

        // Gone for good — not even a trash entry, since nothing was ever really saved.
        var trash = await client.GetFromJsonAsync<List<TrashedPage>>($"/api/pages/trash?spaceId={spaceId}");
        Assert.Empty(trash!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/pages/{draft.Id}/publish",
                new { Title = "Too Late", ContentJson = Doc })).StatusCode);

        // A published page is a real page, not a draft — delete-draft must refuse it.
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Real", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/pages/{page!.Id}/draft")).StatusCode);
    }

    [Fact]
    public async Task Publish_rejects_missing_title_and_invalid_content()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var draft = await (await client.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<DraftResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/pages/{draft!.Id}/publish",
            new { Title = "  ", ContentJson = Doc })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/pages/{draft.Id}/publish",
            new { Title = "OK", ContentJson = "{ not json" })).StatusCode);
    }

    [Fact]
    public async Task Full_width_can_be_toggled_on_a_draft_and_survives_publish()
    {
        var (factory, client, spaceId) = await NewClientWithSpace();
        using var _ = factory;
        var draft = await (await client.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<DraftResponse>();

        // The full-width toggle is available while composing a brand-new page,
        // before it has ever been published — so layout must reach a draft.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/pages/{draft!.Id}/layout", new { FullWidth = true })).StatusCode);

        var published = await (await client.PostAsJsonAsync($"/api/pages/{draft.Id}/publish",
            new { Title = "Wide From Birth", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.True(published!.FullWidth); // publish must not reset display metadata

        var fetched = await client.GetFromJsonAsync<PageDetail>($"/api/pages/{draft.Id}");
        Assert.True(fetched!.FullWidth);
    }
}
