using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The MCP endpoint (dev-plan 8.4), driven as a client would drive it:
/// JSON-RPC over Streamable HTTP, token-authenticated, stateless.
/// </summary>
public class McpTests
{
    private record Created(Guid Id, string Token);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDetail(Guid Id, Guid SpaceId, string Title);

    private const int User = 0, View = 0;
    private const string Doc = """
    {"type":"doc","content":[
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Plan"}]},
      {"type":"paragraph","content":[{"type":"text","text":"Ship "},{"type":"text","marks":[{"type":"bold"}],"text":"soon"},{"type":"text","text":"."}]},
      {"type":"dynamicBlock","attrs":{"kind":"children","params":{"depth":"1"}}}
    ]}
    """;

    private static async Task<HttpClient> TokenClient(TestAppFactory f, HttpClient session, bool readOnly = false)
    {
        var created = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = "mcp", ReadOnly = readOnly }))
            .Content.ReadFromJsonAsync<Created>();
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", created!.Token);
        // Streamable HTTP: a client must accept both JSON and an event stream.
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return client;
    }

    /// <summary>One JSON-RPC call; unwraps the SSE framing if the server chose to stream.</summary>
    private static async Task<JsonElement> Rpc(HttpClient client, string method, object? @params = null, int id = 1)
    {
        var res = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id, method, @params });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        if (res.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            body = string.Join("\n", body.Split('\n')
                .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                .Select(l => l["data:".Length..].Trim()));
        }
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> CallTool(HttpClient client, string name, object args)
    {
        var result = await Rpc(client, "tools/call", new { name, arguments = args }, id: 2);
        return result.GetProperty("result");
    }

    /// <summary>The tool's structured result, which the SDK also mirrors as text content.</summary>
    private static JsonElement Structured(JsonElement callResult) =>
        callResult.TryGetProperty("structuredContent", out var sc)
            ? sc
            : JsonDocument.Parse(callResult.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();

    [Fact]
    public async Task Only_an_api_token_is_accepted_never_a_cookie_session()
    {
        using var f = new TestAppFactory();
        var session = f.CreateClient();
        await session.RegisterAndSignInAsync();

        var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" })).StatusCode);

        // A signed-in browser session is not a credential here (8.4, decision 2).
        var asBrowser = await session.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, asBrowser.StatusCode);
    }

    [Fact]
    public async Task Initialize_and_list_tools()
    {
        using var f = new TestAppFactory();
        var session = f.CreateClient();
        await session.RegisterAndSignInAsync();
        var mcp = await TokenClient(f, session);

        var init = await Rpc(mcp, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "0" },
        });
        Assert.Equal("tesria", init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Contains("Markdown", init.GetProperty("result").GetProperty("instructions").GetString());

        var tools = (await Rpc(mcp, "tools/list")).GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("list_spaces", tools);
        Assert.Contains("get_page", tools);
    }

    [Fact]
    public async Task Get_page_returns_markdown_with_live_blocks_resolved_as_the_caller()
    {
        using var f = new TestAppFactory();
        var session = f.CreateClient();
        await session.RegisterAndSignInAsync();
        var space = await (await session.PostAsJsonAsync("/api/spaces", new { Key = "MCP", Name = "MCP", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>();
        var parent = await (await session.PostAsJsonAsync("/api/pages",
            new { SpaceId = space!.Id, ParentPageId = (Guid?)null, Title = "Parent", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        await session.PostAsJsonAsync("/api/pages",
            new { SpaceId = space.Id, ParentPageId = parent!.Id, Title = "Child page", ContentJson = Doc });
        await session.PostAsJsonAsync($"/api/pages/{parent.Id}/labels", new { Name = "roadmap" });

        var mcp = await TokenClient(f, session);
        var page = Structured(await CallTool(mcp, "get_page", new { pageId = parent.Id }));

        Assert.Equal("Parent", page.GetProperty("title").GetString());
        Assert.Equal("MCP", page.GetProperty("spaceKey").GetString());
        Assert.Equal("markdown", page.GetProperty("format").GetString());
        Assert.Equal(["roadmap"], page.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
        var md = page.GetProperty("content").GetString()!;
        Assert.Contains("## Plan", md);
        Assert.Contains("Ship **soon**.", md);
        // The children block is resolved, not left as a placeholder.
        Assert.Contains("[Child page](", md);

        var json = Structured(await CallTool(mcp, "get_page", new { pageId = parent.Id, format = "json" }));
        Assert.Equal("json", json.GetProperty("format").GetString());
        Assert.Contains("\"dynamicBlock\"", json.GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_page_the_tokens_owner_cannot_see_is_not_found_and_absent_from_listings()
    {
        using var f = new TestAppFactory();
        var alice = f.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var bob = f.CreateClient();
        await bob.RegisterAndSignInAsync();

        var space = await (await alice.PostAsJsonAsync("/api/spaces", new { Key = "SEC", Name = "Secret space", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>();
        var page = await (await alice.PostAsJsonAsync("/api/pages",
            new { SpaceId = space!.Id, ParentPageId = (Guid?)null, Title = "Secret", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        (await alice.PostAsJsonAsync($"/api/pages/{page!.Id}/restrictions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View })).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = User, PrincipalId = aliceId, Operation = View })).EnsureSuccessStatusCode();

        var bobMcp = await TokenClient(f, bob);
        var call = await CallTool(bobMcp, "get_page", new { pageId = page.Id });
        Assert.True(call.GetProperty("isError").GetBoolean());
        var text = call.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("not found", text);
        Assert.DoesNotContain("Secret", text);

        var spaces = Structured(await CallTool(bobMcp, "list_spaces", new { }));
        Assert.DoesNotContain(spaces.EnumerateArray(), s => s.GetProperty("key").GetString() == "SEC");
    }
}
