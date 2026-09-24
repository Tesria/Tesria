using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

public class CommentTests
{
    private record PageDetail(Guid Id, Guid SpaceId, Guid? ParentPageId, string Title);
    private record CommentResponse(
        Guid Id, Guid PageId, Guid? ParentCommentId, string? Body, string? AnchorJson,
        Guid AuthorId, bool IsInline, bool IsDeleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<(TestAppFactory, HttpClient, Guid pageId)> NewClientWithPage()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "P", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        return (factory, client, page!.Id);
    }

    [Fact]
    public async Task Footer_and_inline_comments_and_replies()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;

        var footer = await (await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "Nice page", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .Content.ReadFromJsonAsync<CommentResponse>();
        Assert.False(footer!.IsInline);

        var inline = await (await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "typo here", ParentCommentId = (Guid?)null, AnchorJson = """{"from":1,"to":5}""" }))
            .Content.ReadFromJsonAsync<CommentResponse>();
        Assert.True(inline!.IsInline);

        var reply = await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "agreed", ParentCommentId = footer.Id, AnchorJson = (string?)null });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);

        var all = await client.GetFromJsonAsync<List<CommentResponse>>($"/api/pages/{pageId}/comments");
        Assert.Equal(3, all!.Count);
        Assert.Contains(all, c => c.ParentCommentId == footer.Id);
    }

    [Fact]
    public async Task Create_rejects_empty_body_and_bad_anchor()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "  ", ParentCommentId = (Guid?)null, AnchorJson = (string?)null })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "hi", ParentCommentId = (Guid?)null, AnchorJson = "{bad" })).StatusCode);
    }

    [Fact]
    public async Task Edit_and_soft_delete_preserve_thread()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;
        var c = await (await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "first", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .Content.ReadFromJsonAsync<CommentResponse>();

        var edited = await (await client.PutAsJsonAsync($"/api/comments/{c!.Id}", new { Body = "edited" }))
            .Content.ReadFromJsonAsync<CommentResponse>();
        Assert.Equal("edited", edited!.Body);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/comments/{c.Id}")).StatusCode);

        // Soft-deleted: still listed (thread preserved) but body hidden + flagged.
        var all = await client.GetFromJsonAsync<List<CommentResponse>>($"/api/pages/{pageId}/comments");
        var deleted = Assert.Single(all!);
        Assert.True(deleted.IsDeleted);
        Assert.Null(deleted.Body);
    }

    [Fact]
    public async Task Only_the_author_can_edit_or_delete()
    {
        var (factory, author, pageId) = await NewClientWithPage();
        using var _ = factory;
        var c = await (await author.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "mine", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .Content.ReadFromJsonAsync<CommentResponse>();

        // A different signed-in user cannot modify someone else's comment.
        var other = factory.CreateClient();
        await other.RegisterAndSignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.PutAsJsonAsync($"/api/comments/{c!.Id}", new { Body = "hijack" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.DeleteAsync($"/api/comments/{c.Id}")).StatusCode);
    }

    private record Resolved(Guid Id, DateTimeOffset? ResolvedAt, string? ResolvedByName);
    private record NotificationRow(Guid Id, string Action, string TargetType, Guid TargetId, string? MetadataJson = null);

    [Fact]
    public async Task A_thread_is_resolved_and_reopened_by_those_who_may()
    {
        var (factory, author, pageId) = await NewClientWithPage();
        using var _ = factory;
        var root = (await (await author.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "Is this final?", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .Content.ReadFromJsonAsync<CommentResponse>())!;
        var reply = (await (await author.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "Yes.", ParentCommentId = root.Id, AnchorJson = (string?)null }))
            .Content.ReadFromJsonAsync<CommentResponse>())!;

        var done = await (await author.PostAsync($"/api/comments/{root.Id}/resolve", null)).Content.ReadFromJsonAsync<Resolved>();
        Assert.NotNull(done!.ResolvedAt);
        Assert.NotNull(done.ResolvedByName);
        var listed = await author.GetFromJsonAsync<List<Resolved>>($"/api/pages/{pageId}/comments");
        Assert.NotNull(listed!.Single(c => c.Id == root.Id).ResolvedAt);

        // A reply goes with its thread; it is not resolved on its own.
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/comments/{reply.Id}/resolve", null)).StatusCode);

        // Someone who may edit the page may reopen it (the space is open).
        var editor = factory.CreateClient();
        await editor.RegisterAndSignInAsync();
        var reopened = await (await editor.PostAsync($"/api/comments/{root.Id}/reopen", null)).Content.ReadFromJsonAsync<Resolved>();
        Assert.Null(reopened!.ResolvedAt);
    }

    [Fact]
    public async Task Mentioning_someone_in_a_comment_tells_them()
    {
        var (factory, author, pageId) = await NewClientWithPage();
        using var _ = factory;
        var sam = factory.CreateClient();
        var samId = await sam.RegisterAndSignInAsync();

        (await author.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = $"Over to you, @[Sam](user:{samId}).", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .EnsureSuccessStatusCode();

        var notes = await sam.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        Assert.Contains(notes!, n => n.Action == "user.mentioned" && n.TargetId == pageId);
    }

    [Fact]
    public async Task A_comment_notification_shows_a_mention_as_the_name()
    {
        // The bell and its email read "@Sam", not the token that carries the id.
        var (factory, author, pageId) = await NewClientWithPage();
        using var _ = factory;
        (await author.PostAsync($"/api/pages/{pageId}/watch", null)).EnsureSuccessStatusCode();
        var sam = factory.CreateClient();
        var samId = await sam.RegisterAndSignInAsync();

        (await sam.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = $"Thanks, @[Sam](user:{samId})!", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .EnsureSuccessStatusCode();

        var notes = await author.GetFromJsonAsync<List<NotificationRow>>("/api/notifications");
        var note = notes!.Single(n => n.Action == "comment.created");
        Assert.Contains("Thanks, @Sam!", note.MetadataJson);
        Assert.DoesNotContain("user:", note.MetadataJson);
    }
}
