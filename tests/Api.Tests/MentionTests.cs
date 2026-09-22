using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure.Mentions;
using Xunit;

namespace Tesria.Api.Tests;

public class MentionExtractionTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string MentionNode(Guid id) =>
        "{\"type\":\"mention\",\"attrs\":{\"userId\":\"" + id + "\",\"label\":\"Someone\"}}";

    private static string DocMentioning(params Guid[] ids) =>
        "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":["
        + string.Join(",", ids.Select(MentionNode))
        + "]}]}";

    [Fact]
    public void Finds_mentions_at_any_depth_and_deduplicates()
    {
        // Nested two containers deep, and Alice twice: a mention inside a
        // layout column or a panel counts exactly like one in a paragraph.
        var doc =
            "{\"type\":\"doc\",\"content\":["
            + "{\"type\":\"paragraph\",\"content\":[" + MentionNode(Alice) + "]},"
            + "{\"type\":\"layoutSection\",\"content\":[{\"type\":\"layoutColumn\",\"content\":["
            + "{\"type\":\"panel\",\"content\":[{\"type\":\"paragraph\",\"content\":["
            + MentionNode(Bob) + "," + MentionNode(Alice) + "]}]}]}]}"
            + "]}";

        Assert.Equal([Alice, Bob], Mentions.UserIdsIn(doc).OrderBy(g => g.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"mention\",\"attrs\":{\"userId\":\"not-a-guid\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"mention\"}]}")]
    public void Malformed_input_yields_nothing_rather_than_throwing(string doc) =>
        Assert.Empty(Mentions.UserIdsIn(doc));

    [Fact]
    public void Only_newly_added_mentions_are_notified()
    {
        // Alice was already mentioned, so a later edit must not ping her again.
        var newly = Mentions.NewlyMentioned(DocMentioning(Alice), DocMentioning(Alice, Bob), authorId: Guid.NewGuid());
        Assert.Equal([Bob], newly);
    }

    [Fact]
    public void Mentioning_yourself_notifies_nobody() =>
        Assert.Empty(Mentions.NewlyMentioned(null, DocMentioning(Alice), authorId: Alice));
}

/// <summary>
/// The mention notification end to end, including the part that matters for
/// security: a mention on a page the recipient cannot see must not tell them
/// anything, because the notification carries the page's title.
/// </summary>
public class MentionNotificationTests
{
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record NotificationResponse(
        Guid Id, string Action, string TargetType, Guid TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson,
        DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

    private const int User = 0, View = 0;
    private const string Plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hello"}]}]}""";

    private static string Mentioning(Guid userId, string label = "Bob") =>
        "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":["
        + "{\"type\":\"mention\",\"attrs\":{\"userId\":\"" + userId + "\",\"label\":\"" + label + "\"}}]}]}";

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Plain }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    private static async Task<List<NotificationResponse>> NotificationsOf(HttpClient c) =>
        (await c.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications"))!;

    [Fact]
    public async Task Mentioning_someone_notifies_them_once()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        await alice.RegisterAndSignInAsync();
        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();

        var spaceId = await alice.CreateSpaceAsync();
        var page = await NewPage(alice, spaceId, "Rollout plan");

        await alice.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = Mentioning(bobId) });

        var mention = Assert.Single(await NotificationsOf(bob), n => n.Action == "user.mentioned");
        Assert.Equal("page", mention.TargetType);
        Assert.Equal(page.Id, mention.TargetId);
        Assert.Contains("Rollout plan", mention.MetadataJson);

        // Editing again without adding anyone must not ping Bob a second time.
        await alice.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { ContentJson = Mentioning(bobId), ChangeComment = "typo" });
        Assert.Single(await NotificationsOf(bob), n => n.Action == "user.mentioned");
    }

    [Fact]
    public async Task A_mention_on_a_page_the_recipient_cannot_see_tells_them_nothing()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = factory.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();

        var spaceId = await alice.CreateSpaceAsync();
        var page = await NewPage(alice, spaceId, "Compensation review");
        (await alice.PostAsJsonAsync($"/api/pages/{page.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View }))
            .EnsureSuccessStatusCode();

        await alice.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = Mentioning(bobId) });

        // Not "notified but 404 on click": the title itself is the leak.
        Assert.DoesNotContain(await NotificationsOf(bob), n => n.Action == "user.mentioned");
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{page.Id}")).StatusCode);
    }

    [Fact]
    public async Task Mentioning_yourself_does_not_notify_you()
    {
        using var factory = new TestAppFactory();
        var alice = factory.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var spaceId = await alice.CreateSpaceAsync();
        var page = await NewPage(alice, spaceId, "Notes");

        await alice.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = Mentioning(aliceId, "Alice") });

        Assert.DoesNotContain(await NotificationsOf(alice), n => n.Action == "user.mentioned");
    }
    [Fact]
    public async Task A_mention_of_an_id_that_is_not_a_user_does_not_fail_the_save()
    {
        // A document carries whatever id it says. One that names nobody used
        // to reach the notification insert and break the write on a foreign
        // key, turning bad content into a 500 (found by the 12.1 fixture).
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();

        var ghost = Guid.NewGuid();
        var doc = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":["
            + "{\"type\":\"mention\",\"attrs\":{\"userId\":\"" + ghost + "\",\"label\":\"Nobody\"}}]}]}";

        var res = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Ghost", ContentJson = doc });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

}
