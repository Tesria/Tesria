using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tesria.Api.Features.Mcp;
using Tesria.Api.Infrastructure;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The MCP comment tools (dev-plan 22.1): list_comments, add_comment and
/// reply_to_comment. They share the REST path, so a comment an assistant
/// writes notifies, mentions and fires webhooks as one from the browser does,
/// and the same three things are owed as for every tool: the result, the
/// leak test, and a read-only token refused before anything changes.
/// </summary>
public class McpCommentToolTests
{
    private record Created(Guid Id, string Token);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record CommentDto(Guid Id, Guid PageId, Guid? ParentCommentId, string? Body, Guid AuthorId, string AuthorName, bool IsDeleted);
    private record NotificationRow(string Action, Guid TargetId, string? MetadataJson);

    private const int User = 0, View = 0;
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"pineapple notes"}]}]}""";

    private sealed record World(
        TestAppFactory F, HttpClient Alice, HttpClient Bob, Guid AliceId, Guid BobId,
        SpaceDto Space, PageDetail Open, PageDetail Secret);

    /// <summary>Alice's space, shared with Bob, holding one open page and one restricted to Alice.</summary>
    private static async Task<World> Build()
    {
        var f = new TestAppFactory();
        var alice = f.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = f.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();

        var space = (await (await alice.PostAsJsonAsync("/api/spaces",
            new { Key = "TALK", Name = "Talk", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>())!;
        var open = await NewPage(alice, space.Id, "Open plan");
        var secret = await NewPage(alice, space.Id, "Secret pineapple");
        (await alice.PostAsJsonAsync($"/api/pages/{secret.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View })).EnsureSuccessStatusCode();
        return new World(f, alice, bob, aliceId, bobId, space, open, secret);
    }

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string title) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    private static async Task<CommentDto> Comment(HttpClient c, Guid pageId, string body, Guid? parent = null, string? anchor = null)
    {
        var res = await c.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = body, ParentCommentId = parent, AnchorJson = anchor });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CommentDto>())!;
    }

    private static async Task<List<CommentDto>> RestList(HttpClient c, Guid pageId) =>
        (await c.GetFromJsonAsync<List<CommentDto>>($"/api/pages/{pageId}/comments"))!;

    private static async Task<HttpClient> Mcp(TestAppFactory f, HttpClient session, bool readOnly = false)
    {
        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = "talker", ReadOnly = readOnly }))
            .Content.ReadFromJsonAsync<Created>();
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", created!.Token);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return client;
    }

    private static async Task<JsonElement> Call(HttpClient client, string name, object args)
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

    private static JsonElement Ok(JsonElement result)
    {
        Assert.False(result.TryGetProperty("isError", out var e) && e.GetBoolean(),
            result.GetProperty("content")[0].GetProperty("text").GetString());
        return result.TryGetProperty("structuredContent", out var sc)
            ? sc
            : JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private static string Error(JsonElement result)
    {
        Assert.True(result.GetProperty("isError").GetBoolean(), "expected the tool to fail");
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    /// <summary>A string property, or null when it is null or left out (the SDK leaves nulls out).</summary>
    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [Fact]
    public async Task List_nests_replies_in_order_and_quotes_an_inline_comments_passage()
    {
        var w = await Build(); using var _ = w.F;

        var first = await Comment(w.Alice, w.Open.Id, $"Can you check this, @[Bob](user:{w.BobId})?");
        var inline = await Comment(w.Alice, w.Open.Id, "Is Friday right?", anchor: """{"type":"text"}""");
        var image = await Comment(w.Alice, w.Open.Id, "Blurry.", anchor: """{"type":"image","src":"/x.png"}""");
        var reply = await Comment(w.Bob, w.Open.Id, "Looks fine to me.", parent: first.Id);
        var deeper = await Comment(w.Alice, w.Open.Id, "Thanks.", parent: reply.Id);
        (await w.Alice.PostAsync($"/api/comments/{first.Id}/resolve", null)).EnsureSuccessStatusCode();

        // The editor marks the passage with the comment's id; the row holds no copy of it.
        var mark = "{\"type\":\"comment\",\"attrs\":{\"commentId\":\"" + inline.Id + "\"}}";
        var marked = "{\"type\":\"doc\",\"content\":["
            + "{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"We ship \"},"
            + "{\"type\":\"text\",\"marks\":[" + mark + "],\"text\":\"on Friday\"},"
            + "{\"type\":\"text\",\"marks\":[" + mark + ",{\"type\":\"bold\"}],\"text\":\" at noon\"}]},"
            + "{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"marks\":[" + mark + "],\"text\":\"sharp.\"}]}]}";
        (await w.Alice.PutAsJsonAsync($"/api/pages/{w.Open.Id}", new { ContentJson = marked })).EnsureSuccessStatusCode();

        var listed = Ok(await Call(await Mcp(w.F, w.Bob, readOnly: true), "list_comments", new { pageId = w.Open.Id }));
        Assert.Equal("Open plan", listed.GetProperty("title").GetString());
        var threads = listed.GetProperty("threads").EnumerateArray().ToList();
        Assert.Equal([first.Id, inline.Id, image.Id], threads.Select(t => t.GetProperty("id").GetGuid()));

        var top = threads[0];
        Assert.Equal("Can you check this, @Bob?", top.GetProperty("body").GetString());
        Assert.Equal(w.BobId, Assert.Single(top.GetProperty("mentions").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.True(top.GetProperty("resolved").GetBoolean());
        Assert.False(top.GetProperty("isInline").GetBoolean());
        Assert.Equal("User", top.GetProperty("author").GetString()![..4]);
        Assert.Equal(w.AliceId, top.GetProperty("authorId").GetGuid());

        // Replies nest under what they answer, a reply to a reply included.
        var r = Assert.Single(top.GetProperty("replies").EnumerateArray());
        Assert.Equal(reply.Id, r.GetProperty("id").GetGuid());
        Assert.Equal(deeper.Id, Assert.Single(r.GetProperty("replies").EnumerateArray()).GetProperty("id").GetGuid());

        Assert.True(threads[1].GetProperty("isInline").GetBoolean());
        Assert.Equal("on Friday at noon sharp.", Str(threads[1], "quote"));
        // An image has no passage to quote.
        Assert.True(threads[2].GetProperty("isInline").GetBoolean());
        Assert.Null(Str(threads[2], "quote"));
    }

    [Fact]
    public async Task A_deleted_comment_keeps_its_place_without_its_text()
    {
        var w = await Build(); using var _ = w.F;
        var first = await Comment(w.Alice, w.Open.Id, "Secret plan: pineapples.");
        await Comment(w.Bob, w.Open.Id, "Agreed.", parent: first.Id);
        (await w.Alice.DeleteAsync($"/api/comments/{first.Id}")).EnsureSuccessStatusCode();

        var listed = Ok(await Call(await Mcp(w.F, w.Alice), "list_comments", new { pageId = w.Open.Id }));
        var thread = Assert.Single(listed.GetProperty("threads").EnumerateArray());
        Assert.True(thread.GetProperty("isDeleted").GetBoolean());
        Assert.Null(Str(thread, "body"));
        Assert.DoesNotContain("pineapples", listed.GetRawText());
        Assert.Equal("Agreed.", Assert.Single(thread.GetProperty("replies").EnumerateArray()).GetProperty("body").GetString());
    }

    [Fact]
    public async Task Add_and_reply_write_real_comments_as_the_tokens_owner_and_tell_the_same_people()
    {
        var w = await Build(); using var _ = w.F;
        (await w.Alice.PostAsJsonAsync("/api/spaces/TALK/webhooks",
            new { Url = "https://example.com/hook", Events = "comment.created" })).EnsureSuccessStatusCode();
        (await w.Alice.PostAsync($"/api/pages/{w.Open.Id}/watch", null)).EnsureSuccessStatusCode();
        var sender = w.F.Services.GetRequiredService<RecordingWebhookSender>();
        var bobMcp = await Mcp(w.F, w.Bob);

        var added = Ok(await Call(bobMcp, "add_comment",
            new { pageId = w.Open.Id, body = $"  Over to you, @[Alice](user:{w.AliceId}).  " }));
        Assert.Equal("Over to you, @Alice.", added.GetProperty("body").GetString());
        Assert.Equal(w.AliceId, Assert.Single(added.GetProperty("mentions").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.EndsWith($"/spaces/TALK/pages/{w.Open.Id}", added.GetProperty("url").GetString());
        var addedId = added.GetProperty("id").GetGuid();

        var replied = Ok(await Call(bobMcp, "reply_to_comment", new { commentId = addedId, body = "And one more thing." }));
        Assert.Equal(addedId, replied.GetProperty("parentCommentId").GetGuid());
        Assert.Equal(w.Open.Id, replied.GetProperty("pageId").GetGuid());

        // Real comments, by the token's owner, exactly where the browser shows them.
        var rows = await RestList(w.Alice, w.Open.Id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, c => Assert.Equal(w.BobId, c.AuthorId));
        Assert.Equal(addedId, rows.Single(c => c.Body == "And one more thing.").ParentCommentId);
        Assert.Null(rows.Single(c => c.Id == addedId).ParentCommentId);

        // Alice, the page's creator, watches it and is mentioned: the bell says both.
        var notes = (await w.Alice.GetFromJsonAsync<List<NotificationRow>>("/api/notifications"))!;
        Assert.Contains(notes, n => n.Action == "user.mentioned" && n.TargetId == w.Open.Id);
        Assert.Contains(notes, n => n.Action == "comment.created" && n.MetadataJson!.Contains("Over to you, @Alice."));

        // Each one fired the webhook, as a REST comment does.
        Assert.Equal(2, sender.Deliveries.Count(d => d.PayloadJson.Contains("comment.created")));

        // The admin tool log puts both on the page, the reply included.
        using var scope = w.F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var calls = await db.McpToolCalls.AsNoTracking().Where(c => c.Tool == "add_comment" || c.Tool == "reply_to_comment").ToListAsync();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => { Assert.True(c.Write); Assert.True(c.Ok); Assert.Equal(w.Open.Id, c.PageId); });
    }

    [Fact]
    public async Task A_read_only_token_may_list_but_not_comment_and_changes_nothing()
    {
        var w = await Build(); using var _ = w.F;
        var first = await Comment(w.Alice, w.Open.Id, "Hello.");
        var readOnly = await Mcp(w.F, w.Alice, readOnly: true);

        Assert.Contains("read-only", Error(await Call(readOnly, "add_comment", new { pageId = w.Open.Id, body = "no" })));
        Assert.Contains("read-only", Error(await Call(readOnly, "reply_to_comment", new { commentId = first.Id, body = "no" })));

        Assert.Single(await RestList(w.Alice, w.Open.Id));
        Ok(await Call(readOnly, "list_comments", new { pageId = w.Open.Id }));
    }

    [Fact]
    public async Task A_page_the_caller_cannot_read_is_not_found_for_every_comment_tool()
    {
        var w = await Build(); using var _ = w.F;
        var hidden = await Comment(w.Alice, w.Secret.Id, "Only for me.");
        var bob = await Mcp(w.F, w.Bob);

        foreach (var (tool, args) in new (string, object)[]
        {
            ("list_comments", new { pageId = w.Secret.Id }),
            ("add_comment", new { pageId = w.Secret.Id, body = "hi" }),
            ("reply_to_comment", new { commentId = hidden.Id, body = "hi" }),
            ("reply_to_comment", new { commentId = Guid.NewGuid(), body = "hi" }),
        })
        {
            // "Not found", never "you may not": the latter confirms it exists.
            var message = Error(await Call(bob, tool, args));
            Assert.Contains("not found", message);
            Assert.DoesNotContain("Secret", message);
        }
        Assert.Single(await RestList(w.Alice, w.Secret.Id));
    }

    [Fact]
    public async Task An_empty_body_is_refused_as_rest_refuses_it()
    {
        var w = await Build(); using var _ = w.F;
        var first = await Comment(w.Alice, w.Open.Id, "Hello.");
        var mcp = await Mcp(w.F, w.Alice);

        Assert.Contains("Comment body is required", Error(await Call(mcp, "add_comment", new { pageId = w.Open.Id, body = "   " })));
        Assert.Contains("Comment body is required", Error(await Call(mcp, "reply_to_comment", new { commentId = first.Id, body = "" })));
        Assert.Single(await RestList(w.Alice, w.Open.Id));
    }

    [Fact]
    public void Quotes_come_from_the_comment_marks_and_bad_content_yields_none()
    {
        var id = Guid.NewGuid();
        var doc = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":["
            + "{\"type\":\"text\",\"marks\":[{\"type\":\"comment\",\"attrs\":{\"commentId\":\"" + id + "\"}}],\"text\":\"" + new string('a', 1500) + "\"}]}]}";
        var quote = TesriaTools.InlineQuotes(doc)[id];
        Assert.Equal(1001, quote.Length);
        Assert.EndsWith("…", quote);

        Assert.Empty(TesriaTools.InlineQuotes(null));
        Assert.Empty(TesriaTools.InlineQuotes("not json"));
        Assert.Empty(TesriaTools.InlineQuotes("""{"type":"doc","content":[{"type":"text","text":"x","marks":[{"type":"comment","attrs":{"commentId":"nope"}}]}]}"""));
    }
}
