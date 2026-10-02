using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

public class WebhookTests
{
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record CreatedWebhook(Guid Id, string Url, string Events, bool Enabled, string Secret);
    private record WebhookRow(Guid Id, string Url, string Events, bool Enabled, DateTimeOffset CreatedAt);
    private const int User = 0, Admin = 2;

    private static async Task<(Guid spaceId, string key)> NewSpace(HttpClient client)
    {
        var spaceId = await client.CreateSpaceAsync();
        var spaces = await client.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        return (spaceId, spaces!.Single(s => s.Id == spaceId).Key);
    }

    [Fact]
    public async Task Creating_a_webhook_returns_the_secret_once_and_never_again()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var (_, key) = await NewSpace(client);

        var created = await (await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "page.updated" }))
            .Content.ReadFromJsonAsync<CreatedWebhook>();
        Assert.False(string.IsNullOrWhiteSpace(created!.Secret));

        var listBody = await client.GetStringAsync($"/api/spaces/{key}/webhooks");
        Assert.DoesNotContain(created.Secret, listBody);
    }

    [Fact]
    public async Task Create_validates_url_and_events()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var (_, key) = await NewSpace(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "not-a-url", Events = "page.updated" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "page.update" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "page.created, comment.created" })).StatusCode);
    }

    [Fact]
    public async Task Managing_webhooks_requires_space_admin_rights()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var (_, key) = await NewSpace(alice);

        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        await alice.MakePrivateAsync(key);
        await alice.PostAsJsonAsync($"/api/spaces/{key}/permissions",
            new { PrincipalType = User, PrincipalId = bobId, Operation = 1 }); // Edit, not Admin

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "*" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/spaces/{key}/webhooks")).StatusCode);
    }

    [Fact]
    public async Task Matching_event_dispatches_to_an_enabled_webhook_with_a_signed_payload()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var (spaceId, key) = await NewSpace(client);
        await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "page.updated,comment.created" });

        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Hooked", ContentJson = (string?)null }))
            .Content.ReadFromJsonAsync<PageDetail>();

        var sender = factory.Services.GetRequiredService<RecordingWebhookSender>();
        // page.created is not in this webhook's event list, so creating the
        // page must not have dispatched anything yet.
        Assert.Empty(sender.Deliveries);

        await client.PutAsJsonAsync($"/api/pages/{page!.Id}",
            new { Title = "Hooked", ContentJson = "{\"type\":\"doc\",\"content\":[]}", ChangeComment = (string?)null });

        var delivery = Assert.Single(sender.Deliveries);
        Assert.Equal("https://example.com/hook", delivery.Url);
        Assert.Contains("page.updated", delivery.PayloadJson);
        Assert.Contains(page.Id.ToString(), delivery.PayloadJson);
    }

    [Fact]
    public async Task Disabled_or_unsubscribed_webhooks_do_not_receive_deliveries()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var (spaceId, key) = await NewSpace(client);
        // Subscribed only to comments, not page updates.
        await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/comments-only", Events = "comment.created" });

        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "P", ContentJson = (string?)null }))
            .Content.ReadFromJsonAsync<PageDetail>();

        var sender = factory.Services.GetRequiredService<RecordingWebhookSender>();
        await client.PutAsJsonAsync($"/api/pages/{page!.Id}",
            new { Title = "P", ContentJson = "{\"type\":\"doc\",\"content\":[]}", ChangeComment = (string?)null });

        Assert.Empty(sender.Deliveries);
    }

    [Fact]
    public async Task A_webhook_hears_nothing_about_a_page_its_creator_cannot_read()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var (spaceId, key) = await NewSpace(alice); // no permission rows: open to every member

        // Bob can manage webhooks on the open space and makes one for everything.
        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        (await bob.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/bob", Events = "*" })).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/alice", Events = "*" })).EnsureSuccessStatusCode();

        var sender = factory.Services.GetRequiredService<RecordingWebhookSender>();
        var page = await (await alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Plans", ContentJson = (string?)null }))
            .Content.ReadFromJsonAsync<PageDetail>();
        // Readable by both, so both hear about it.
        Assert.Contains(sender.Deliveries, d => d.Url == "https://example.com/bob");
        Assert.Contains(sender.Deliveries, d => d.Url == "https://example.com/alice");

        (await alice.PostAsJsonAsync($"/api/pages/{page!.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = 0 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{page.Id}")).StatusCode);
        var before = sender.Deliveries.Count(d => d.Url == "https://example.com/bob");

        (await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Plans, restricted", ContentJson = "{\"type\":\"doc\",\"content\":[]}", ChangeComment = (string?)null }))
            .EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/pages/{page.Id}/comments",
            new { Body = "private remark", ParentCommentId = (Guid?)null, AnchorJson = (string?)null })).EnsureSuccessStatusCode();
        var child = await (await alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = page.Id, Title = "Under it", ContentJson = (string?)null }))
            .Content.ReadFromJsonAsync<PageDetail>();
        Assert.NotNull(child);

        // Bob's webhook heard none of it; Alice's, whose creator can read the page, heard all three.
        Assert.Equal(before, sender.Deliveries.Count(d => d.Url == "https://example.com/bob"));
        Assert.DoesNotContain(sender.Deliveries, d => d.Url == "https://example.com/bob" && d.PayloadJson.Contains("restricted"));
        Assert.DoesNotContain(sender.Deliveries, d => d.Url == "https://example.com/bob" && d.PayloadJson.Contains("private remark"));
        var aliceHeard = sender.Deliveries.Where(d => d.Url == "https://example.com/alice").Select(d => d.PayloadJson).ToList();
        Assert.Contains(aliceHeard, p => p.Contains("page.updated"));
        Assert.Contains(aliceHeard, p => p.Contains("private remark"));
        Assert.Contains(aliceHeard, p => p.Contains(child!.Id.ToString()));
    }

    [Fact]
    public async Task Wildcard_subscription_receives_every_event_type()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var (spaceId, key) = await NewSpace(client);
        await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/everything", Events = "*" });

        var sender = factory.Services.GetRequiredService<RecordingWebhookSender>();

        await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Q", ContentJson = (string?)null });

        Assert.Single(sender.Deliveries, d => d.PayloadJson.Contains("page.created"));
    }

    [Fact]
    public async Task Deleting_a_webhook_stops_further_dispatch()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var (spaceId, key) = await NewSpace(client);
        var created = await (await client.PostAsJsonAsync($"/api/spaces/{key}/webhooks",
            new { Url = "https://example.com/hook", Events = "*" }))
            .Content.ReadFromJsonAsync<CreatedWebhook>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/spaces/{key}/webhooks/{created!.Id}")).StatusCode);

        var sender = factory.Services.GetRequiredService<RecordingWebhookSender>();
        await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "R", ContentJson = (string?)null });

        Assert.Empty(sender.Deliveries);
    }
}
