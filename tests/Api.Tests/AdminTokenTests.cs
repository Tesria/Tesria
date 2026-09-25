using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tesria.Api.Infrastructure;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The administrators' API tokens tab (2026-09-24): who holds
/// tokens, how much each is used through the API and through MCP, what
/// assistants did, and revoking one token.
/// </summary>
public class AdminTokenTests
{
    private const int Admin = 1;
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hello"}]}]}""";

    private record Created(Guid Id, string Token);
    private record UserDto(Guid Id);
    private record Counts(int Reads, int Writes, int McpReads, int McpWrites);
    private record Owner(Guid Id, string DisplayName, string Email);
    private record TokenRow(Guid Id, string Name, long UseCount, Owner Owner, Counts Last7Days);
    private record Summary(int Tokens, int ActiveLast7Days, int NeverUsed, Counts Last7Days);
    private record ActivityPage(Guid Id, string? Title, bool Hidden);
    private record ActivityRow(string Tool, bool Write, bool Ok, string TokenName, string? UserName, ActivityPage? Page);
    private record PageDto(Guid Id);

    private static async Task<(HttpClient Client, Guid Id)> RegisterAsync(TestAppFactory f, string email)
    {
        var c = f.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/register", new { Email = email, DisplayName = email.Split('@')[0], Password = "supersecret" });
        res.EnsureSuccessStatusCode();
        return (c, (await res.Content.ReadFromJsonAsync<UserDto>())!.Id);
    }

    private static async Task<HttpClient> TokenClientAsync(TestAppFactory f, HttpClient session, string name, bool mcp = false)
    {
        var made = await (await session.PostAsJsonAsync("/api/api-tokens", new { Name = name })).Content.ReadFromJsonAsync<Created>();
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", made!.Token);
        if (mcp) client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return client;
    }

    private static async Task CallToolAsync(HttpClient client, string name, object args)
    {
        var res = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments = args } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task An_administrator_sees_every_token_whose_it_is_and_how_much_it_is_used()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        var (member, memberId) = await RegisterAsync(f, "member@example.com");
        var script = await TokenClientAsync(f, member, "nightly");
        await TokenClientAsync(f, member, "unused");

        (await script.GetAsync("/api/spaces")).EnsureSuccessStatusCode();
        (await script.GetAsync("/api/spaces")).EnsureSuccessStatusCode();
        (await script.PostAsJsonAsync("/api/spaces", new { Key = "SCRIPT", Name = "Script" })).EnsureSuccessStatusCode();

        var rows = await owner.GetFromJsonAsync<List<TokenRow>>("/api/admin/api-tokens");
        var nightly = rows!.Single(r => r.Name == "nightly");
        Assert.Equal(memberId, nightly.Owner.Id);
        Assert.Equal("member@example.com", nightly.Owner.Email);
        Assert.Equal(3, nightly.UseCount);
        Assert.Equal(new Counts(2, 1, 0, 0), nightly.Last7Days);
        Assert.Equal(0, rows!.Single(r => r.Name == "unused").UseCount);

        var summary = await owner.GetFromJsonAsync<Summary>("/api/admin/api-tokens/summary");
        Assert.Equal((2, 1, 1), (summary!.Tokens, summary.ActiveLast7Days, summary.NeverUsed));

        // Someone without the right to see users sees none of it.
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/api-tokens")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/api-tokens/activity")).StatusCode);
    }

    [Fact]
    public async Task Revoking_one_token_leaves_the_others_and_tells_its_owner()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        var (member, memberId) = await RegisterAsync(f, "member@example.com");
        var rogue = await TokenClientAsync(f, member, "rogue");
        var fine = await TokenClientAsync(f, member, "fine");

        (await rogue.GetAsync("/api/spaces")).EnsureSuccessStatusCode();
        var rows = await owner.GetFromJsonAsync<List<TokenRow>>("/api/admin/api-tokens");
        (await owner.DeleteAsync($"/api/admin/api-tokens/{rows!.Single(r => r.Name == "rogue").Id}")).EnsureSuccessStatusCode();

        // What it did stays counted after it is gone (2026-09-24).
        var summary = await owner.GetFromJsonAsync<Summary>("/api/admin/api-tokens/summary");
        Assert.Equal(1, summary!.Last7Days.Reads);
        Assert.Equal(1, summary.Tokens);

        Assert.Equal(HttpStatusCode.Unauthorized, (await rogue.GetAsync("/api/notifications")).StatusCode);
        (await fine.GetAsync("/api/notifications")).EnsureSuccessStatusCode();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "token.revoked_by_admin" && a.TargetId == memberId));
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == memberId && n.Action == "token.revoked"));
    }

    [Fact]
    public async Task An_administrator_cannot_revoke_the_owners_token()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        var (admin, adminId) = await RegisterAsync(f, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new { Role = Admin })).EnsureSuccessStatusCode();
        await TokenClientAsync(f, owner, "owners");

        var rows = await admin.GetFromJsonAsync<List<TokenRow>>("/api/admin/api-tokens");
        var id = rows!.Single(r => r.Name == "owners").Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/admin/api-tokens/{id}")).StatusCode);
    }

    [Fact]
    public async Task What_an_assistant_did_is_logged_and_a_private_page_stays_unnamed()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        var (member, memberId) = await RegisterAsync(f, "member@example.com");
        var (admin, adminId) = await RegisterAsync(f, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new { Role = Admin })).EnsureSuccessStatusCode();

        var spaceId = await member.CreateSpaceAsync("PRIV");
        // Private to the member: view for the member only.
        (await member.PostAsJsonAsync("/api/spaces/PRIV/permissions", new { PrincipalType = 0, PrincipalId = memberId, Operation = 0 }))
            .EnsureSuccessStatusCode();
        var page = await (await member.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, Title = "Salary review", ContentJson = Doc })).Content.ReadFromJsonAsync<PageDto>();

        var assistant = await TokenClientAsync(f, member, "claude", mcp: true);
        await CallToolAsync(assistant, "get_page", new { pageId = page!.Id });
        await CallToolAsync(assistant, "add_page_label", new { pageId = page.Id, label = "reviewed" });

        var activity = await admin.GetFromJsonAsync<List<ActivityRow>>("/api/admin/api-tokens/activity");
        Assert.Equal(["add_page_label", "get_page"], activity!.Select(a => a.Tool));
        Assert.True(activity![0].Write && activity[0].Ok);
        Assert.Equal(("claude", "member"), (activity[0].TokenName, activity[0].UserName));
        // The administrator may not read that space, so the page has no title here.
        Assert.All(activity, a => Assert.True(a.Page!.Hidden && a.Page.Title is null));

        var rows = await admin.GetFromJsonAsync<List<TokenRow>>("/api/admin/api-tokens");
        Assert.Equal(new Counts(0, 0, 1, 1), rows!.Single(r => r.Name == "claude").Last7Days);
    }

    [Fact]
    public async Task A_refused_change_is_a_request_but_not_a_change()
    {
        using var f = new TestAppFactory();
        var (owner, _) = await RegisterAsync(f, "owner@example.com");
        var made = await (await owner.PostAsJsonAsync("/api/api-tokens", new { Name = "reader", ReadOnly = true })).Content.ReadFromJsonAsync<Created>();
        var reader = f.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new("Bearer", made!.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/spaces", new { Key = "NOPE", Name = "Nope" })).StatusCode);

        var rows = await owner.GetFromJsonAsync<List<TokenRow>>("/api/admin/api-tokens");
        Assert.Equal(new Counts(1, 0, 0, 0), rows!.Single(r => r.Name == "reader").Last7Days);
    }
}
