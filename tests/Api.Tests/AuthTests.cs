using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class AuthTests
{
    private record RegisterRequest(string Email, string DisplayName, string Password);
    private record LoginRequest(string Email, string Password);
    private record UserResponse(Guid Id, string Email, string DisplayName);

    [Fact]
    public async Task Register_creates_account_and_signs_in()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Ada@Example.com", "Ada Lovelace", "supersecret"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var user = await res.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Equal("ada@example.com", user!.Email); // normalized lower-case

        // Cookie from registration authenticates the follow-up request.
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(user.Id, me!.Id);
    }

    [Fact]
    public async Task Register_rejects_duplicate_email()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var req = new RegisterRequest("dup@example.com", "First", "supersecret");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", req)).StatusCode);
        var second = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("dup@example.com", "Second", "supersecret"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_short_password()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("weak@example.com", "Weak", "short"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_succeeds_with_correct_password_and_fails_otherwise()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("grace@example.com", "Grace", "supersecret"));
        // Drop the registration cookie so we exercise a clean login.
        client = factory.CreateClient();

        var bad = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("grace@example.com", "wrongpass"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var good = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("Grace@example.com", "supersecret"));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Me_is_unauthorized_when_not_signed_in()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("bye@example.com", "Bye", "supersecret"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
