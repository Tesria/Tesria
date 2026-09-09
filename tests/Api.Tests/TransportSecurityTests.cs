using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Proxy trust and response headers (dev-plan 3.0). Everything in the rest of
/// Phase 3 that keys on a client address assumes these hold.
/// </summary>
public class TransportSecurityTests
{
    private static async Task RegisterAsync(HttpClient client, string email) =>
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .EnsureSuccessStatusCode();

    private static async Task<string?> LastLoginIpAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "user.login").ToListAsync();
        var last = rows.OrderByDescending(a => a.CreatedAt).First();
        using var doc = System.Text.Json.JsonDocument.Parse(last.MetadataJson!);
        return doc.RootElement.GetProperty("Ip").GetString();
    }

    [Fact]
    public async Task The_proxy_forwarded_address_becomes_the_client_address()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();

        Assert.Equal("203.0.113.9", await LastLoginIpAsync(factory));
    }

    [Fact]
    public async Task A_forwarded_address_from_outside_the_trusted_networks_is_ignored()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");

        // A caller on a public address claiming to be someone else: the header
        // must not be believed, or a rate limit is dodged by editing a header.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.Header, "198.51.100.7");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();

        Assert.Equal("198.51.100.7", await LastLoginIpAsync(factory));
    }

    [Fact]
    public async Task Only_the_nearest_proxy_hop_is_believed()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");

        // Caddy appends the real client last; anything the client put in the
        // header itself comes earlier. ForwardLimit=1 takes only the last.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "1.1.1.1, 203.0.113.9");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();

        Assert.Equal("203.0.113.9", await LastLoginIpAsync(factory));
    }

    [Fact]
    public async Task The_session_cookie_is_secure_when_the_proxy_says_https()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "a@example.com");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "a@example.com", Password = "supersecret" });
        res.EnsureSuccessStatusCode();

        // Outside production the policy is "same as request", so this proves
        // the forwarded scheme reached the cookie handler. Production forces
        // Secure regardless.
        var setCookie = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("tesria.auth="));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        using var factory = new TestAppFactory();
        var res = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", res.Headers.GetValues("Referrer-Policy").Single());

        var csp = res.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-inline'", csp.Split(';').Single(d => d.Trim().StartsWith("script-src")));
        Assert.Contains("connect-src 'self' ws://localhost", csp);
    }

    [Fact]
    public async Task An_unauthenticated_request_also_gets_the_headers()
    {
        using var factory = new TestAppFactory();
        var res = await factory.CreateClient().GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.True(res.Headers.Contains("Content-Security-Policy"));
    }
}
