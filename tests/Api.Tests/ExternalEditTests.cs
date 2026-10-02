using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

/// <summary>
/// Publishing when the page has moved on (dev-plan 8.6, step 5).
///
/// The editor sends the version its draft was last reconciled to. If the page
/// is past that, something wrote to it that this draft has not seen, and
/// publishing would overwrite it. The refusal carries the page as it stands
/// so the editor can show the difference rather than just saying no.
/// </summary>
public class PublishConflictTests
{
    private record PageDetail(Guid Id, string Title, string ContentJson, int CurrentVersionNumber);

    private static string Doc(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    private static async Task<(TestAppFactory, HttpClient, PageDetail)> NewPage()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Page", ContentJson = Doc("first") }))
            .Content.ReadFromJsonAsync<PageDetail>();
        return (factory, client, page!);
    }

    [Fact]
    public async Task Publishing_from_the_version_you_have_succeeds()
    {
        var (factory, client, page) = await NewPage();
        using var _ = factory;

        var res = await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("second"), BaseVersion = page.CurrentVersionNumber });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Publishing_from_a_version_the_page_has_passed_is_refused()
    {
        var (factory, client, page) = await NewPage();
        using var _ = factory;

        // Somebody else writes. An API caller sends no version, so this one
        // wins as it always has.
        (await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("written by an assistant") })).EnsureSuccessStatusCode();

        // Now the editor publishes a draft based on what it loaded first.
        var res = await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("my stale draft"), BaseVersion = page.CurrentVersionNumber });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task The_refusal_carries_the_page_as_it_now_stands()
    {
        // Not just a status code: the editor reconciles against this body to
        // show the difference, and cannot do that from a 409 alone.
        var (factory, client, page) = await NewPage();
        using var _ = factory;
        (await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("what the page says now") })).EnsureSuccessStatusCode();

        var res = await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("my stale draft"), BaseVersion = page.CurrentVersionNumber });
        var body = await res.Content.ReadFromJsonAsync<PageDetail>();

        Assert.Contains("what the page says now", body!.ContentJson);
        Assert.True(body.CurrentVersionNumber > page.CurrentVersionNumber);
    }

    [Fact]
    public async Task A_refused_publish_changes_nothing()
    {
        var (factory, client, page) = await NewPage();
        using var _ = factory;
        (await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("the good version") })).EnsureSuccessStatusCode();

        await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("would have clobbered it"), BaseVersion = page.CurrentVersionNumber });

        var after = await client.GetFromJsonAsync<PageDetail>($"/api/pages/{page.Id}");
        Assert.Contains("the good version", after!.ContentJson);
        Assert.DoesNotContain("would have clobbered", after.ContentJson);
    }

    [Fact]
    public async Task Omitting_the_version_keeps_last_write_wins()
    {
        // What every API and MCP caller does. They hold no draft that could
        // be stale, and requiring a version would break existing scripts.
        var (factory, client, page) = await NewPage();
        using var _ = factory;
        (await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("second") })).EnsureSuccessStatusCode();

        var res = await client.PutAsJsonAsync($"/api/pages/{page.Id}",
            new { Title = "Page", ContentJson = Doc("third, no version sent") });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}

/// <summary>
/// Discarding a published page's shared draft, and telling the live-editing
/// service which writes are outside changes (0.8.2, after QA cal-001 and
/// T5-032).
/// </summary>
public class DraftDiscardTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title, string ContentJson, int CurrentVersionNumber);

    private static string Doc(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    /// <summary>Remembers what the live-editing service was told, and answers discards as set.</summary>
    private sealed class RecordingCollab : ICollabNotifier
    {
        public DraftResetResult Answer = DraftResetResult.Done;
        public ConcurrentQueue<(Guid PageId, WriteSource Source, int Version)> Writes = new();
        public ConcurrentQueue<(Guid PageId, string Content, int Version)> Resets = new();

        public Task NotifyAsync(Guid pageId, string contentJson, WriteSource source, int version, CancellationToken ct = default)
        {
            Writes.Enqueue((pageId, source, version));
            return Task.CompletedTask;
        }

        public Task MaintenanceAsync(bool on, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeAsync(CollabRevocation revocation, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DraftResetResult> ResetDraftAsync(Guid pageId, string contentJson, int version, CancellationToken ct = default)
        {
            Resets.Enqueue((pageId, contentJson, version));
            return Task.FromResult(Answer);
        }
    }

    private sealed record Setup(TestAppFactory Factory, WebApplicationFactory<Program> App, HttpClient Client, PageDetail Page) : IDisposable
    {
        public void Dispose()
        {
            App.Dispose();
            Factory.Dispose();
        }
    }

    private static async Task<Setup> NewPage(RecordingCollab collab)
    {
        var factory = new TestAppFactory();
        var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ICollabNotifier>();
            s.AddSingleton<ICollabNotifier>(collab);
        }));
        var client = app.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Page", ContentJson = Doc("first") }))
            .Content.ReadFromJsonAsync<PageDetail>();
        return new Setup(factory, app, client, page!);
    }

    [Fact]
    public async Task Discarding_resets_the_draft_to_what_is_published()
    {
        var collab = new RecordingCollab();
        using var s = await NewPage(collab);
        (await s.Client.PutAsJsonAsync($"/api/pages/{s.Page.Id}",
            new { Title = "Page", ContentJson = Doc("second"), BaseVersion = s.Page.CurrentVersionNumber })).EnsureSuccessStatusCode();

        var res = await s.Client.DeleteAsync($"/api/pages/{s.Page.Id}/draft");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var reset = Assert.Single(collab.Resets);
        Assert.Equal(s.Page.Id, reset.PageId);
        Assert.Equal(2, reset.Version);
        Assert.Contains("second", reset.Content);
        // The page itself is untouched: only the unpublished draft goes.
        var after = await s.Client.GetFromJsonAsync<PageDetail>($"/api/pages/{s.Page.Id}");
        Assert.Equal(2, after!.CurrentVersionNumber);
    }

    [Fact]
    public async Task A_discard_the_live_editing_service_could_not_take_is_reported_not_swallowed()
    {
        var collab = new RecordingCollab { Answer = DraftResetResult.Unavailable };
        using var s = await NewPage(collab);

        var res = await s.Client.DeleteAsync($"/api/pages/{s.Page.Id}/draft");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Someone_who_may_only_read_the_page_cannot_discard_its_draft()
    {
        var collab = new RecordingCollab();
        using var s = await NewPage(collab);
        var alice = s.Client;
        var aliceId = (await alice.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();
        var spaceKey = (await alice.GetFromJsonAsync<List<JsonElement>>("/api/spaces"))!
            .First(x => x.GetProperty("id").GetGuid() == s.Page.SpaceId)
            .GetProperty("key").GetString();

        var bob = s.App.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        // Alice takes admin; Bob gets view only. (User = 0; View = 0, Admin = 2.)
        await alice.MakePrivateAsync(spaceKey!);
        await alice.PostAsJsonAsync($"/api/spaces/{spaceKey}/permissions", new { PrincipalType = 0, PrincipalId = bobId, Operation = 0 });
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/pages/{s.Page.Id}")).StatusCode);

        var res = await bob.DeleteAsync($"/api/pages/{s.Page.Id}/draft");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Empty(collab.Resets);
    }

    [Fact]
    public async Task The_editors_own_update_is_the_editors_but_a_restore_from_history_is_an_outside_change()
    {
        var collab = new RecordingCollab();
        using var s = await NewPage(collab);

        // The editor's Update names the version its draft is based on.
        (await s.Client.PutAsJsonAsync($"/api/pages/{s.Page.Id}",
            new { Title = "Page", ContentJson = Doc("second"), BaseVersion = 1 })).EnsureSuccessStatusCode();
        // Restoring version 1 from History does not: it did not come from the draft.
        (await s.Client.PostAsync($"/api/pages/{s.Page.Id}/versions/1/restore", null)).EnsureSuccessStatusCode();

        var writes = collab.Writes.Where(w => w.PageId == s.Page.Id).ToList();
        Assert.Contains(writes, w => w.Version == 2 && w.Source == WriteSource.Editor);
        Assert.Contains(writes, w => w.Version == 3 && w.Source == WriteSource.Page);
    }
}
