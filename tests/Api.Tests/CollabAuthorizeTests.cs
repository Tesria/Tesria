using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The collaboration service asking the app whether a connection may stand
/// (dev-plan 14.4, the review's SEC-02). Before this a token, once issued,
/// admitted its holder until it expired, whatever happened to their access.
/// </summary>
public class CollabAuthorizeTests
{
    private const string Secret = "test-collab-secret-0123456789";
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";

    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record TokenResponse(string Token, string DocumentName, bool Enabled);
    private record Created(Guid Id, string Token);
    private record Answer(Dictionary<string, bool> Allowed);

    /// <summary>The claims the service reads out of a verified token.</summary>
    private record Claims(string UserId, string PageId, string Sk, string Sid, string St);

    private static TestAppFactory Factory() => new(new Dictionary<string, string?> { ["Collab:Secret"] = Secret });

    private static async Task<PageDetail> PageAsync(HttpClient client)
    {
        var spaceId = await client.CreateSpaceAsync();
        return (await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Live", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>())!;
    }

    private static async Task<Claims> ClaimsAsync(HttpClient client, Guid pageId)
    {
        var res = await client.GetFromJsonAsync<TokenResponse>($"/api/pages/{pageId}/collab-token");
        Assert.True(res!.Enabled);
        var part = res.Token.Split('.')[0].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        var payload = JsonDocument.Parse(Convert.FromBase64String(part)).RootElement;
        return new Claims(
            payload.GetProperty("userId").GetString()!, payload.GetProperty("pageId").GetString()!,
            payload.GetProperty("sk").GetString()!, payload.GetProperty("sid").GetString()!,
            payload.GetProperty("st").GetString()!);
    }

    private static async Task<HttpResponseMessage> AskAsync(TestAppFactory f, string? secret, params Claims[] connections)
    {
        var service = f.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/collab/authorize")
        {
            Content = JsonContent.Create(new
            {
                connections = connections.Select((c, i) => new
                {
                    key = i.ToString(), c.UserId, c.PageId, c.Sk, c.Sid, c.St,
                }),
            }),
        };
        if (secret is not null) request.Headers.Add("X-Collab-Secret", secret);
        return await service.SendAsync(request);
    }

    private static async Task<bool[]> AllowedAsync(TestAppFactory f, params Claims[] connections)
    {
        var res = await AskAsync(f, Secret, connections);
        res.EnsureSuccessStatusCode();
        var answer = (await res.Content.ReadFromJsonAsync<Answer>())!;
        return [.. connections.Select((_, i) => answer.Allowed[i.ToString()])];
    }

    [Fact]
    public async Task Only_the_collaboration_service_may_ask()
    {
        using var f = Factory();
        var owner = f.CreateClient();
        await owner.RegisterAndSignInAsync();
        var claims = await ClaimsAsync(owner, (await PageAsync(owner)).Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(f, null, claims)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AskAsync(f, "not-the-secret-0123456789abc", claims)).StatusCode);
        // Signed in makes no difference: this is not a user's route.
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/collab/authorize")
        {
            Content = JsonContent.Create(new { connections = Array.Empty<object>() }),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task A_sessions_connection_stands_until_the_session_ends()
    {
        using var f = Factory();
        var owner = f.CreateClient();
        await owner.RegisterAndSignInAsync();
        var claims = await ClaimsAsync(owner, (await PageAsync(owner)).Id);

        Assert.Equal(new[] { true }, await AllowedAsync(f, claims));

        (await owner.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(new[] { false }, await AllowedAsync(f, claims));
    }

    [Fact]
    public async Task A_connection_ends_when_its_page_is_restricted_away_from_its_user()
    {
        using var f = Factory();
        var owner = f.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        var page = await PageAsync(owner);

        // Another member, editing the page in an open space.
        var other = f.CreateClient();
        await other.RegisterAndSignInAsync();
        var claims = await ClaimsAsync(other, page.Id);
        Assert.Equal(new[] { true }, await AllowedAsync(f, claims));

        // The owner restricts the page to themselves. The token the other
        // member holds is still valid for minutes; the answer is not.
        (await owner.PostAsJsonAsync($"/api/pages/{page.Id}/restrictions",
            new { PrincipalType = 0, PrincipalId = ownerId, Operation = 0 })).EnsureSuccessStatusCode();
        Assert.Equal(new[] { false }, await AllowedAsync(f, claims));
    }

    [Fact]
    public async Task A_changed_security_stamp_ends_the_connection()
    {
        using var f = Factory();
        var owner = f.CreateClient();
        await owner.RegisterAndSignInAsync();
        var claims = await ClaimsAsync(owner, (await PageAsync(owner)).Id);

        // What a password change or "sign out everywhere" does to the stamp.
        Assert.Equal(new[] { false }, await AllowedAsync(f, claims with { St = "0000000000000000" }));
        Assert.Equal(new[] { false }, await AllowedAsync(f, claims with { Sk = "x" }));
    }

    [Fact]
    public async Task An_api_tokens_connection_ends_when_the_token_is_revoked()
    {
        using var f = Factory();
        var owner = f.CreateClient();
        await owner.RegisterAndSignInAsync();
        var page = await PageAsync(owner);
        var created = (await (await owner.PostAsJsonAsync("/api/api-tokens", new { Name = "live", ReadOnly = false }))
            .Content.ReadFromJsonAsync<Created>())!;
        var script = f.CreateClient();
        script.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var claims = await ClaimsAsync(script, page.Id);
        Assert.Equal("t", claims.Sk);

        Assert.Equal(new[] { true }, await AllowedAsync(f, claims));

        (await owner.DeleteAsync($"/api/api-tokens/{created.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(new[] { false }, await AllowedAsync(f, claims));
    }
}
