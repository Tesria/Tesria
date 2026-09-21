using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tesria.Api.Features.Pages;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Collab;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Tracked changes from outside the editing session (dev-plan 8.6).
///
/// This file covers the server's half of step 1: a published version may
/// never carry <c>externalInsert</c> or <c>externalDelete</c>. Those marks
/// mean "an API or MCP write changed this and nobody has decided yet", and a
/// published version is a decision. The editor resolves them before
/// publishing, but the server does not depend on every client having
/// remembered to.
/// </summary>
public class ExternalMarkStripTests
{
    private static string Normalize(string input)
    {
        Assert.True(PageContent.TryNormalize(input, out var normalized));
        return normalized;
    }

    [Fact]
    public void A_tracked_change_mark_never_survives_into_a_stored_version()
    {
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","marks":[{"type":"externalInsert","attrs":{"source":"mcp","actor":"Bot"}}],"text":"added"},
          {"type":"text","marks":[{"type":"externalDelete","attrs":{"source":"mcp"}}],"text":"removed"}]}]}
        """;

        var stored = Normalize(doc);

        Assert.DoesNotContain("externalInsert", stored);
        Assert.DoesNotContain("externalDelete", stored);
        // The attributes go with the mark rather than lingering as orphans.
        Assert.DoesNotContain("mcp", stored);
    }

    [Fact]
    public void The_text_the_marks_were_on_is_kept_including_the_deletions()
    {
        // Deliberate: this is a safety net, not a merge. It cannot know
        // whether the human meant to accept, and quietly deleting their words
        // would be the worse guess of the two.
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","marks":[{"type":"externalDelete"}],"text":"the old sentence"}]}]}
        """;

        Assert.Contains("the old sentence", Normalize(doc));
    }

    [Fact]
    public void Other_marks_on_the_same_text_are_left_alone()
    {
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","marks":[{"type":"bold"},{"type":"externalInsert"},{"type":"link","attrs":{"href":"https://example.com"}}],"text":"hi"}]}]}
        """;

        var stored = Normalize(doc);

        Assert.DoesNotContain("externalInsert", stored);
        Assert.Contains("bold", stored);
        Assert.Contains("https://example.com", stored);
    }

    [Fact]
    public void A_node_left_with_no_marks_drops_the_property_rather_than_keeping_an_empty_array()
    {
        // What the editor itself produces for unmarked text, so a round trip
        // through the strip does not change the document's shape.
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","marks":[{"type":"externalInsert"}],"text":"hi"}]}]}
        """;

        var stored = Normalize(doc);

        Assert.DoesNotContain("marks", stored);
        Assert.Contains("\"text\":\"hi\"", stored);
    }

    [Fact]
    public void A_document_with_nothing_to_strip_is_returned_byte_for_byte()
    {
        // The common case by far. Rewriting every save would churn the JSON
        // for no reason and make version diffs noisy.
        const string doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";

        Assert.Same(doc, Normalize(doc));
    }

    [Fact]
    public void The_marks_are_stripped_wherever_they_are_nested()
    {
        // Inside a table cell inside a layout column: the walk is structural,
        // not a scan of the top level.
        var doc = """
        {"type":"doc","content":[{"type":"layoutSection","content":[{"type":"layoutColumn","content":[
          {"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[
            {"type":"paragraph","content":[
              {"type":"text","marks":[{"type":"externalInsert"}],"text":"deep"}]}]}]}]}]}]}]}
        """;

        var stored = Normalize(doc);

        Assert.DoesNotContain("externalInsert", stored);
        Assert.Contains("deep", stored);
    }

    [Fact]
    public void Malformed_marks_do_not_throw()
    {
        // Stored content is arbitrary JSON as far as the API is concerned, so
        // the strip must not be the thing that discovers it is odd.
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","marks":["externalInsert",{"noType":1},{"type":42}],"text":"hi"}]}]}
        """;

        var stored = Normalize(doc);

        Assert.Contains("hi", stored);
    }
}

/// <summary>The same guarantee, through the door a real caller uses.</summary>
public class ExternalMarkEndpointTests
{
    private record PageDetail(Guid Id, string Title, string ContentJson);

    private const string Draft = """
    {"type":"doc","content":[{"type":"paragraph","content":[
      {"type":"text","marks":[{"type":"externalInsert","attrs":{"source":"mcp"}}],"text":"from an assistant"}]}]}
    """;

    [Fact]
    public async Task A_page_saved_with_pending_tracked_changes_stores_none()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();

        var created = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Draft", ContentJson = Draft }))
            .Content.ReadFromJsonAsync<PageDetail>();

        Assert.DoesNotContain("externalInsert", created!.ContentJson);
        Assert.Contains("from an assistant", created.ContentJson);

        // And on update, which is the path an editor actually publishes through.
        var updated = await client.PutAsJsonAsync($"/api/pages/{created.Id}",
            new { Title = "Draft", ContentJson = Draft, ChangeComment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var reread = await client.GetFromJsonAsync<PageDetail>($"/api/pages/{created.Id}");
        Assert.DoesNotContain("externalInsert", reread!.ContentJson);
    }

    [Fact]
    public async Task The_stored_document_is_still_valid_json_after_stripping()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();

        var created = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Draft", ContentJson = Draft }))
            .Content.ReadFromJsonAsync<PageDetail>();

        using var parsed = JsonDocument.Parse(created!.ContentJson);
        Assert.Equal("doc", parsed.RootElement.GetProperty("type").GetString());
    }
}

/// <summary>
/// Which door a write came through (dev-plan 8.6). This decides what a
/// highlight says, and more importantly whether the sidecar shows the write
/// at all: a write from the editor is the editor's own document, so it is
/// recorded and not drawn.
/// </summary>
public class WriteSourceTests
{
    private static HttpContext Request(string path, string? scheme)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.User = scheme is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity([], scheme));
        return context;
    }

    private const string Token = ApiTokenAuthenticationDefaults.AuthenticationScheme;

    [Fact]
    public void A_browser_session_is_the_editor()
    {
        Assert.Equal(WriteSource.Editor, WriteSources.Of(Request("/api/pages/x", "Cookies")));
    }

    [Fact]
    public void An_api_token_on_a_rest_route_is_the_api()
    {
        Assert.Equal(WriteSource.Api, WriteSources.Of(Request("/api/pages/x", Token)));
    }

    [Fact]
    public void The_same_token_at_the_mcp_endpoint_is_an_assistant()
    {
        // MCP authenticates with the same API tokens REST does, so the
        // endpoint is what separates them, not the credential.
        Assert.Equal(WriteSource.Mcp, WriteSources.Of(Request("/mcp", Token)));
        Assert.Equal(WriteSource.Mcp, WriteSources.Of(Request("/mcp/messages", Token)));
    }

    [Fact]
    public void A_path_that_merely_begins_with_those_letters_is_not_mcp()
    {
        // StartsWithSegments, not StartsWith: /mcpanything is another route.
        Assert.Equal(WriteSource.Api, WriteSources.Of(Request("/mcpanything", Token)));
    }

    [Fact]
    public void An_unauthenticated_or_absent_request_falls_back_to_the_editor()
    {
        // The quiet option: "editor" records a version and draws nothing, so
        // an unexpected caller cannot put highlighting into someone's draft.
        Assert.Equal(WriteSource.Editor, WriteSources.Of(Request("/api/pages/x", scheme: null)));
        Assert.Equal(WriteSource.Editor, WriteSources.Of((HttpContext?)null));
    }
}
