using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class NotificationTests
{
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record WatchStatus(bool Watching);
    private record NotificationRow(
        Guid Id, string Action, string TargetType, Guid TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson,
        DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
    private record UnreadCount(int Count);

    private const string Doc = """{"type":"doc","content":[]}""";
    private const int User = 0, Admin = 2;

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    [Fact]
    public async Task Watching_a_page_notifies_on_update_but_not_the_editors_own_edit()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var page = await NewPage(alice, spaceId, "Watched");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await bob.PostAsync($"/api/pages/{page.Id}/watch", null)).StatusCode);
        Assert.True((await bob.GetFromJsonAsync<WatchStatus>($"/api/pages/{page.Id}/watch"))!.Watching);

        // Alice edits — Bob (a watcher) is notified, Alice (the editor) is not.
        await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Watched", ContentJson = Doc, ChangeComment = (string?)null });

        var bobNotifications = await bob.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.Contains(bobNotifications!, n => n.Action == "page.updated" && n.TargetId == page.Id);

        var aliceNotifications = await alice.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.DoesNotContain(aliceNotifications!, n => n.Action == "page.updated");
    }

    [Fact]
    public async Task Watching_a_space_notifies_on_new_pages_and_comments()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var spaces = await alice.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        await bob.PostAsync($"/api/spaces/{key}/watch", null);
        Assert.True((await bob.GetFromJsonAsync<WatchStatus>($"/api/spaces/{key}/watch"))!.Watching);

        var page = await NewPage(alice, spaceId, "New In Watched Space");
        await alice.PostAsJsonAsync($"/api/pages/{page.Id}/comments",
            new { Body = "first comment", ParentCommentId = (Guid?)null, AnchorJson = (string?)null });

        var notes = await bob.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.Contains(notes!, n => n.Action == "page.created" && n.TargetId == page.Id);
        Assert.Contains(notes!, n => n.Action == "comment.created" && n.TargetId == page.Id);
    }

    [Fact]
    public async Task Unwatching_stops_further_notifications()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var page = await NewPage(alice, spaceId, "Toggle");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        await bob.PostAsync($"/api/pages/{page.Id}/watch", null);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync($"/api/pages/{page.Id}/watch")).StatusCode);
        Assert.False((await bob.GetFromJsonAsync<WatchStatus>($"/api/pages/{page.Id}/watch"))!.Watching);

        await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Toggle", ContentJson = Doc, ChangeComment = (string?)null });

        var notes = await bob.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.DoesNotContain(notes!, n => n.Action == "page.updated" && n.TargetId == page.Id);
    }

    [Fact]
    public async Task Mark_read_and_mark_all_read_update_unread_count()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var page = await NewPage(alice, spaceId, "Counting");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        await bob.PostAsync($"/api/pages/{page.Id}/watch", null);

        await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Counting", ContentJson = Doc, ChangeComment = (string?)null });
        await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Counting v2", ContentJson = Doc, ChangeComment = (string?)null });

        var before = await bob.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count");
        Assert.Equal(2, before!.Count);

        var notes = await bob.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        await bob.PostAsync($"/api/notifications/{notes![0].Id}/read", null);
        var afterOne = await bob.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count");
        Assert.Equal(1, afterOne!.Count);

        await bob.PostAsync("/api/notifications/read-all", null);
        var afterAll = await bob.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count");
        Assert.Equal(0, afterAll!.Count);
    }

    [Fact]
    public async Task Notifications_hide_once_access_to_the_target_is_revoked()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var spaces = await alice.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;
        var page = await NewPage(alice, spaceId, "Locking Down");

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        await bob.PostAsync($"/api/pages/{page.Id}/watch", null);
        await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Locking Down", ContentJson = Doc, ChangeComment = (string?)null });

        var before = await bob.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.Contains(before!, n => n.TargetId == page.Id);

        // Lock the space to Alice only — Bob's existing notification for this
        // page must disappear rather than keep exposing its title/metadata.
        await alice.PostAsJsonAsync($"/api/spaces/{key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });

        var after = await bob.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.DoesNotContain(after!, n => n.TargetId == page.Id);
    }

    [Fact]
    public async Task Watch_requires_view_access_to_the_target()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var spaces = await alice.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;
        var page = await NewPage(alice, spaceId, "Hidden From Bob");

        await alice.PostAsJsonAsync($"/api/spaces/{key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = Admin });

        var bob = factory.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/pages/{page.Id}/watch", null)).StatusCode);
    }
}
