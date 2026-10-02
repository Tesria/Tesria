using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tesria.Api.Infrastructure.Collab;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Inline comments from agents (dev-plan 22.2): a comment with a quote has
/// its highlight placed in the page's shared draft by the live-editing
/// service, and is saved only once it is. The service is faked here; what it
/// does to the document is tested beside it (commentAnchor.test.ts). These
/// pin the app's half: who may ask, in which order the checks run (none of
/// the refusals reaches the service), what each refusal says, and that a
/// placed comment is a comment like any other.
/// </summary>
public class InlineCommentTests
{
    private record Created(Guid Id, string Token);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record CommentDto(Guid Id, Guid PageId, string? Body, string? AnchorJson, bool IsInline);
    private record NotificationRow(string Action, Guid TargetId, string? MetadataJson);
    private record DraftDto(Guid Id);

    private const int User = 0, View = 0;
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"We launch on Friday at noon."}]}]}""";

    /// <summary>Stands in for the live-editing service: records each request and answers as told.</summary>
    private sealed class FakeCollab : ICollabNotifier
    {
        public InlineCommentResult Answer = new(InlineCommentPlacement.Placed, Occurrence: 1);
        public readonly ConcurrentQueue<(Guid PageId, Guid CommentId, string Quote, int? Occurrence)> Placed = new();
        public readonly ConcurrentQueue<(Guid PageId, Guid CommentId)> Removed = new();

        public Task NotifyAsync(Guid pageId, string contentJson, WriteSource source, int version, CancellationToken ct = default) => Task.CompletedTask;
        public Task MaintenanceAsync(bool on, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeAsync(CollabRevocation revocation, CancellationToken ct = default) => Task.CompletedTask;

        public Task<InlineCommentResult> PlaceCommentAsync(Guid pageId, Guid commentId, string quote, int? occurrence, CancellationToken ct = default)
        {
            Placed.Enqueue((pageId, commentId, quote, occurrence));
            return Task.FromResult(Answer);
        }

        public Task RemoveCommentAsync(Guid pageId, Guid commentId, CancellationToken ct = default)
        {
            Removed.Enqueue((pageId, commentId));
            return Task.CompletedTask;
        }
    }

    private sealed record World(
        TestAppFactory Factory, WebApplicationFactory<Program> App, FakeCollab Collab,
        HttpClient Alice, HttpClient Bob, HttpClient Carol, Guid AliceId, PageDetail Page) : IDisposable
    {
        public void Dispose()
        {
            App.Dispose();
            Factory.Dispose();
        }
    }

    /// <summary>
    /// Alice's space: Bob may view it but not edit; Carol may not see it at
    /// all. One page, which Alice watches, and a webhook for comments.
    /// </summary>
    private static async Task<World> Build()
    {
        var collab = new FakeCollab();
        var factory = new TestAppFactory();
        var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ICollabNotifier>();
            s.AddSingleton<ICollabNotifier>(collab);
        }));
        var alice = app.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = app.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        var carol = app.CreateClient();
        await carol.RegisterAndSignInAsync();

        var space = (await (await alice.PostAsJsonAsync("/api/spaces",
            new { Key = "INL", Name = "Inline", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>())!;
        (await alice.PostAsJsonAsync("/api/spaces/INL/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = 2 })).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync("/api/spaces/INL/permissions",
            new { PrincipalType = User, PrincipalId = bobId, Operation = View })).EnsureSuccessStatusCode();
        var page = (await (await alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, ParentPageId = (Guid?)null, Title = "Launch", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;
        (await alice.PostAsJsonAsync("/api/spaces/INL/webhooks",
            new { Url = "https://example.com/hook", Events = "comment.created" })).EnsureSuccessStatusCode();
        return new World(factory, app, collab, alice, bob, carol, aliceId, page);
    }

    private static async Task<HttpClient> Token(World w, HttpClient session, bool readOnly = false, bool mcp = false)
    {
        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = "agent", ReadOnly = readOnly }))
            .Content.ReadFromJsonAsync<Created>();
        var client = w.App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", created!.Token);
        if (mcp) client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return client;
    }

    private static Task<HttpResponseMessage> PostInline(HttpClient c, Guid pageId, string quote, int? occurrence = null, string body = "Is this right?") =>
        c.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = body, ParentCommentId = (Guid?)null, AnchorJson = (string?)null, Quote = quote, Occurrence = occurrence });

    private static async Task<List<CommentDto>> Comments(HttpClient c, Guid pageId) =>
        (await c.GetFromJsonAsync<List<CommentDto>>($"/api/pages/{pageId}/comments"))!;

    private static async Task<JsonElement> Json(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> CallTool(HttpClient client, string name, object args)
    {
        var res = await client.PostAsJsonAsync("/mcp",
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments = args } });
        var body = await res.Content.ReadAsStringAsync();
        if (res.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = string.Join("\n", body.Split('\n')
                .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                .Select(l => l["data:".Length..].Trim()));
        return JsonDocument.Parse(body).RootElement.GetProperty("result").Clone();
    }

    private static string ToolError(JsonElement result)
    {
        Assert.True(result.TryGetProperty("isError", out var e) && e.GetBoolean(), "expected the tool to fail");
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private static JsonElement ToolOk(JsonElement result)
    {
        Assert.False(result.TryGetProperty("isError", out var e) && e.GetBoolean(),
            result.GetProperty("content")[0].GetProperty("text").GetString());
        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    // -- who may ask -----------------------------------------------------------

    [Fact]
    public async Task Refusals_over_rights_come_in_order_and_never_reach_the_live_editing_service()
    {
        using var w = await Build();

        // Cannot read: not found, exactly as for any comment.
        Assert.Equal(HttpStatusCode.NotFound, (await PostInline(w.Carol, w.Page.Id, "Friday")).StatusCode);

        // Can read, cannot edit: the highlight goes into the draft.
        var bob = await PostInline(w.Bob, w.Page.Id, "Friday");
        Assert.Equal(HttpStatusCode.Forbidden, bob.StatusCode);
        Assert.Contains("edit rights", (await Json(bob)).GetProperty("detail").GetString());

        // A read-only token: refused before anything runs.
        var readOnly = await PostInline(await Token(w, w.Alice, readOnly: true), w.Page.Id, "Friday");
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.Equal("read_only_token", (await Json(readOnly)).GetProperty("code").GetString());

        Assert.Empty(w.Collab.Placed);
        Assert.Empty(await Comments(w.Alice, w.Page.Id));

        // Bob may still comment on the whole page: that needs only read access.
        (await w.Bob.PostAsJsonAsync($"/api/pages/{w.Page.Id}/comments",
            new { Body = "A page comment.", ParentCommentId = (Guid?)null, AnchorJson = (string?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_bad_quote_or_occurrence_is_refused_before_the_service_is_asked()
    {
        using var w = await Build();

        Assert.Equal(HttpStatusCode.BadRequest, (await PostInline(w.Alice, w.Page.Id, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostInline(w.Alice, w.Page.Id, "Friday", occurrence: 0)).StatusCode);

        var tooLong = await PostInline(w.Alice, w.Page.Id, new string('a', 1001));
        Assert.Equal((HttpStatusCode)422, tooLong.StatusCode);
        Assert.Equal("quote_too_long", (await Json(tooLong)).GetProperty("code").GetString());

        // Whitespace collapses before the length is counted.
        w.Collab.Answer = new InlineCommentResult(InlineCommentPlacement.Placed, Occurrence: 1);
        Assert.Equal(HttpStatusCode.Created,
            (await PostInline(w.Alice, w.Page.Id, "Friday" + new string(' ', 1200) + "at noon")).StatusCode);
        Assert.Equal("Friday at noon", Assert.Single(w.Collab.Placed).Quote);
    }

    // -- what the service says ------------------------------------------------

    [Theory]
    [InlineData("quote_not_found", 0)]
    [InlineData("quote_ambiguous", 3)]
    [InlineData("quote_spans_blocks", null)]
    public async Task A_quote_the_service_cannot_place_once_is_a_422_with_its_code_and_count_and_no_comment(string code, int? count)
    {
        using var w = await Build();
        w.Collab.Answer = new InlineCommentResult(InlineCommentPlacement.Refused, code, $"Because: {code}.", count);

        var res = await PostInline(w.Alice, w.Page.Id, "Friday");

        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        var problem = await Json(res);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        if (count is { } n) Assert.Equal(n, problem.GetProperty("count").GetInt32());
        Assert.Equal($"Because: {code}.", problem.GetProperty("detail").GetString());
        Assert.Empty(await Comments(w.Alice, w.Page.Id));
    }

    [Theory]
    [InlineData(InlineCommentPlacement.Unavailable)]
    [InlineData(InlineCommentPlacement.NotConfigured)]
    public async Task Without_live_editing_the_request_is_refused_with_a_503_and_no_comment(InlineCommentPlacement status)
    {
        using var w = await Build();
        w.Collab.Answer = new InlineCommentResult(status);

        var res = await PostInline(w.Alice, w.Page.Id, "Friday");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("live_editing_unavailable", (await Json(res)).GetProperty("code").GetString());
        Assert.Empty(await Comments(w.Alice, w.Page.Id));
        // An answer lost on the way back may have left a highlight: it is taken off again.
        if (status == InlineCommentPlacement.Unavailable) Assert.Single(w.Collab.Removed);
    }

    // -- a placed comment ------------------------------------------------------

    [Fact]
    public async Task A_placed_comment_is_saved_with_its_passage_and_tells_watchers_and_webhooks()
    {
        using var w = await Build();
        (await w.Alice.PostAsync($"/api/pages/{w.Page.Id}/watch", null)).EnsureSuccessStatusCode();
        var sender = w.App.Services.GetRequiredService<RecordingWebhookSender>();
        w.Collab.Answer = new InlineCommentResult(InlineCommentPlacement.Placed, Occurrence: 2);

        // Bob may edit now; he comments, Alice watches.
        var bobId = (await w.Bob.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();
        (await w.Alice.PostAsJsonAsync("/api/spaces/INL/permissions",
            new { PrincipalType = User, PrincipalId = bobId, Operation = 1 })).EnsureSuccessStatusCode();
        var res = await PostInline(w.Bob, w.Page.Id, "  Friday\n at  noon ", occurrence: 2, body: "Noon is too early.");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var placed = Assert.Single(w.Collab.Placed);
        Assert.Equal("Friday at noon", placed.Quote);
        Assert.Equal(2, placed.Occurrence);

        // The highlight carries the comment's id, minted before the row was saved.
        var saved = Assert.Single(await Comments(w.Alice, w.Page.Id));
        Assert.Equal(placed.CommentId, saved.Id);
        Assert.True(saved.IsInline);
        var anchor = JsonDocument.Parse(saved.AnchorJson!).RootElement;
        Assert.Equal("text", anchor.GetProperty("type").GetString());
        Assert.Equal("Friday at noon", anchor.GetProperty("quote").GetString());
        Assert.Equal(2, anchor.GetProperty("occurrence").GetInt32());

        var note = Assert.Single((await w.Alice.GetFromJsonAsync<List<NotificationRow>>("/api/notifications"))!,
            n => n.Action == "comment.created");
        Assert.Contains("Friday at noon", note.MetadataJson);
        Assert.Matches("\"Inline\":\\s*true", note.MetadataJson);

        var delivery = Assert.Single(sender.Deliveries, d => d.PayloadJson.Contains("comment.created"));
        Assert.Contains("Friday at noon", delivery.PayloadJson);
    }

    [Fact]
    public async Task An_inline_comment_on_an_unpublished_draft_tells_no_watcher_and_fires_no_webhook()
    {
        using var w = await Build();
        var sender = w.App.Services.GetRequiredService<RecordingWebhookSender>();
        var draft = (await (await w.Alice.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = w.Page.SpaceId, ParentPageId = (Guid?)null })).Content.ReadFromJsonAsync<DraftDto>())!;
        (await w.Alice.PostAsync($"/api/pages/{draft.Id}/watch", null)).EnsureSuccessStatusCode();
        var before = sender.Deliveries.Count;

        Assert.Equal(HttpStatusCode.Created, (await PostInline(w.Alice, draft.Id, "anything")).StatusCode);

        Assert.Single(await Comments(w.Alice, draft.Id));
        Assert.Equal(before, sender.Deliveries.Count);
        Assert.DoesNotContain((await w.Alice.GetFromJsonAsync<List<NotificationRow>>("/api/notifications"))!,
            n => n.Action == "comment.created" && n.TargetId == draft.Id);
    }

    [Fact]
    public async Task A_quote_cannot_go_with_a_reply_or_an_anchor()
    {
        using var w = await Build();
        var first = (await (await w.Alice.PostAsJsonAsync($"/api/pages/{w.Page.Id}/comments",
            new { Body = "Hello.", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .Content.ReadFromJsonAsync<CommentDto>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Alice.PostAsJsonAsync($"/api/pages/{w.Page.Id}/comments",
            new { Body = "Re.", ParentCommentId = first.Id, AnchorJson = (string?)null, Quote = "Friday" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Alice.PostAsJsonAsync($"/api/pages/{w.Page.Id}/comments",
            new { Body = "Re.", ParentCommentId = (Guid?)null, AnchorJson = """{"type":"text"}""", Quote = "Friday" })).StatusCode);
        Assert.Empty(w.Collab.Placed);
    }

    // -- MCP --------------------------------------------------------------------

    [Fact]
    public async Task Mcp_add_comment_takes_a_quote_and_says_what_rest_says()
    {
        using var w = await Build();
        var alice = await Token(w, w.Alice, mcp: true);

        w.Collab.Answer = new InlineCommentResult(InlineCommentPlacement.Placed, Occurrence: 1);
        var added = ToolOk(await CallTool(alice, "add_comment", new { pageId = w.Page.Id, body = "Noon?", quote = "at noon" }));
        Assert.True(added.GetProperty("isInline").GetBoolean());
        Assert.Equal("at noon", added.GetProperty("quote").GetString());
        Assert.Equal(1, added.GetProperty("occurrence").GetInt32());

        var listed = ToolOk(await CallTool(alice, "list_comments", new { pageId = w.Page.Id }));
        var thread = Assert.Single(listed.GetProperty("threads").EnumerateArray());
        Assert.Equal("at noon", thread.GetProperty("quote").GetString());
        Assert.False(thread.GetProperty("isDeleted").GetBoolean());

        w.Collab.Answer = new InlineCommentResult(InlineCommentPlacement.Refused, "quote_ambiguous",
            "That passage appears 3 times on the page; say which one with occurrence (1 to 3), or quote more of it.", 3);
        Assert.Contains("3 times", ToolError(await CallTool(alice, "add_comment", new { pageId = w.Page.Id, body = "x", quote = "a" })));

        w.Collab.Answer = new InlineCommentResult(InlineCommentPlacement.Unavailable);
        Assert.Contains("Live editing is not reachable", ToolError(await CallTool(alice, "add_comment", new { pageId = w.Page.Id, body = "x", quote = "a" })));

        // An empty quote is a page comment, as some clients send "" for "none".
        var plain = ToolOk(await CallTool(alice, "add_comment", new { pageId = w.Page.Id, body = "Whole page.", quote = "" }));
        Assert.False(plain.GetProperty("isInline").GetBoolean());
    }

    [Fact]
    public async Task Mcp_refusals_over_rights_match_rest_and_never_reach_the_service()
    {
        using var w = await Build();

        var carol = ToolError(await CallTool(await Token(w, w.Carol, mcp: true), "add_comment",
            new { pageId = w.Page.Id, body = "x", quote = "Friday" }));
        Assert.Contains("not found", carol);
        Assert.DoesNotContain("Launch", carol);

        Assert.Contains("edit rights", ToolError(await CallTool(await Token(w, w.Bob, mcp: true), "add_comment",
            new { pageId = w.Page.Id, body = "x", quote = "Friday" })));
        Assert.Contains("read-only", ToolError(await CallTool(await Token(w, w.Alice, readOnly: true, mcp: true), "add_comment",
            new { pageId = w.Page.Id, body = "x", quote = "Friday" })));

        Assert.Empty(w.Collab.Placed);
        Assert.Contains("not found", ToolError(await CallTool(await Token(w, w.Carol, mcp: true), "list_comments",
            new { pageId = w.Page.Id })));
    }
}
