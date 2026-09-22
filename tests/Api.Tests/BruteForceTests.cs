using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Brute-force protection (dev-plan 3.2): the per-address limiter, the
/// per-account lockout, the anonymous global limiter and token minting.
/// Addresses are spoofed through the trusted-proxy path, so these also prove
/// 3.0's forwarded headers reach the limiter.
/// </summary>
public class BruteForceTests
{
    private record RegisteredDto(Guid Id, List<string> RecoveryCodes);
    private record LimitsDto(int LoginRateLimitPerMinute, int LockoutThreshold, List<LockoutRow> ActiveLockouts);
    private record LockoutRow(Guid UserId, string Email, int FailedLoginCount, DateTimeOffset LockedUntil);
    private record TokenDto(Guid Id, string Token);

    private static async Task<RegisteredDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<RegisteredDto>())!;

    private static HttpClient From(TestAppFactory factory, string ip)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });

    /// <summary>Registers the admin on loopback and applies the given limits.</summary>
    private static async Task<HttpClient> AdminWithLimitsAsync(TestAppFactory factory, object limits)
    {
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings", limits)).EnsureSuccessStatusCode();
        return admin;
    }

    [Fact]
    public async Task Sign_in_attempts_from_one_address_are_limited()
    {
        using var factory = new TestAppFactory();
        await AdminWithLimitsAsync(factory, new { LoginRateLimitPerMinute = 3 });

        var attacker = From(factory, "203.0.113.1");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(attacker, "admin@example.com", "nope")).StatusCode);

        var limited = await LoginAsync(attacker, "admin@example.com", "nope");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task Two_addresses_do_not_share_a_budget()
    {
        using var factory = new TestAppFactory();
        await AdminWithLimitsAsync(factory, new { LoginRateLimitPerMinute = 2 });

        var first = From(factory, "203.0.113.1");
        await LoginAsync(first, "admin@example.com", "nope");
        await LoginAsync(first, "admin@example.com", "nope");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(first, "admin@example.com", "nope")).StatusCode);

        // Before 3.0 every caller shared Caddy's address; this is the proof
        // that they no longer do: a legitimate user next door still gets in.
        var second = From(factory, "203.0.113.2");
        (await LoginAsync(second, "admin@example.com", "supersecret")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_for_the_right_password()
    {
        using var factory = new TestAppFactory();
        await AdminWithLimitsAsync(factory,
            new { LoginRateLimitPerMinute = 100, LockoutThreshold = 3, LockoutBaseSeconds = 3600, LockoutMaxSeconds = 3600 });
        await RegisterAsync(factory.CreateClient(), "victim@example.com");

        // Three failures from three addresses: the per-address limiter never
        // trips, so this is the per-account counter doing the work.
        for (var i = 1; i <= 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await LoginAsync(From(factory, $"203.0.113.{i}"), "victim@example.com", "nope")).StatusCode);

        // Locked: the correct password is refused with the same 401 as a wrong
        // one, so the lock cannot be used to confirm a guess.
        var right = await LoginAsync(From(factory, "203.0.113.9"), "victim@example.com", "supersecret");
        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode);
        Assert.Empty(await right.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_lockout_is_always_temporary()
    {
        using var factory = new TestAppFactory();
        await AdminWithLimitsAsync(factory,
            new { LoginRateLimitPerMinute = 100, LockoutThreshold = 2, LockoutBaseSeconds = 1, LockoutMaxSeconds = 1 });
        await RegisterAsync(factory.CreateClient(), "victim@example.com");

        await LoginAsync(From(factory, "203.0.113.1"), "victim@example.com", "nope");
        await LoginAsync(From(factory, "203.0.113.2"), "victim@example.com", "nope");

        // A permanent lock would let anyone lock anyone out by guessing at
        // their address. Poll rather than sleep-and-assert: under a loaded
        // test run the exact second the lock lifts is not something to bet on.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        HttpStatusCode last;
        do
        {
            last = (await LoginAsync(From(factory, "203.0.113.9"), "victim@example.com", "supersecret")).StatusCode;
            if (last == HttpStatusCode.OK) break;
            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);
        Assert.Equal(HttpStatusCode.OK, last);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failure_count()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminWithLimitsAsync(factory, new { LoginRateLimitPerMinute = 100, LockoutThreshold = 3 });
        await RegisterAsync(factory.CreateClient(), "victim@example.com");

        await LoginAsync(From(factory, "203.0.113.1"), "victim@example.com", "nope");
        await LoginAsync(From(factory, "203.0.113.2"), "victim@example.com", "nope");
        (await LoginAsync(From(factory, "203.0.113.3"), "victim@example.com", "supersecret")).EnsureSuccessStatusCode();

        // Two more failures would have locked it had the count carried over.
        await LoginAsync(From(factory, "203.0.113.4"), "victim@example.com", "nope");
        await LoginAsync(From(factory, "203.0.113.5"), "victim@example.com", "nope");
        (await LoginAsync(From(factory, "203.0.113.6"), "victim@example.com", "supersecret")).EnsureSuccessStatusCode();

        var limits = await admin.GetFromJsonAsync<LimitsDto>("/api/admin/security/limits");
        Assert.Empty(limits!.ActiveLockouts);
    }

    [Fact]
    public async Task Recovering_the_password_ends_the_lockout()
    {
        using var factory = new TestAppFactory();
        await AdminWithLimitsAsync(factory, new { LoginRateLimitPerMinute = 100, LockoutThreshold = 2, LockoutMaxSeconds = 3600 });
        var victim = await RegisterAsync(factory.CreateClient(), "victim@example.com");

        await LoginAsync(From(factory, "203.0.113.1"), "victim@example.com", "nope");
        await LoginAsync(From(factory, "203.0.113.2"), "victim@example.com", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await LoginAsync(From(factory, "203.0.113.3"), "victim@example.com", "supersecret")).StatusCode);

        // The owner proves it is them with a recovery code; the lock that was
        // protecting them from the attacker should not now keep them out.
        (await From(factory, "203.0.113.4").PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "victim@example.com", Code = victim.RecoveryCodes[0], NewPassword = "a-new-secret-1" }))
            .EnsureSuccessStatusCode();
        (await LoginAsync(From(factory, "203.0.113.5"), "victim@example.com", "a-new-secret-1")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Administrators_see_lockouts_and_can_clear_them()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminWithLimitsAsync(factory, new { LoginRateLimitPerMinute = 100, LockoutThreshold = 2, LockoutMaxSeconds = 3600 });
        var victim = await RegisterAsync(factory.CreateClient(), "victim@example.com");

        await LoginAsync(From(factory, "203.0.113.1"), "victim@example.com", "nope");
        await LoginAsync(From(factory, "203.0.113.2"), "victim@example.com", "nope");

        var limits = await admin.GetFromJsonAsync<LimitsDto>("/api/admin/security/limits");
        var row = Assert.Single(limits!.ActiveLockouts);
        Assert.Equal(victim.Id, row.UserId);
        Assert.Equal(2, row.FailedLoginCount);

        (await admin.PostAsync($"/api/admin/users/{victim.Id}/unlock", null)).EnsureSuccessStatusCode();
        (await LoginAsync(From(factory, "203.0.113.3"), "victim@example.com", "supersecret")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Anonymous_callers_are_limited_globally_but_sessions_are_not()
    {
        using var factory = new TestAppFactory();
        await AdminWithLimitsAsync(factory, new { AnonymousRateLimitPerMinute = 3 });

        // One address: a signed-in session and a stranger.
        var session = From(factory, "203.0.113.1");
        (await LoginAsync(session, "admin@example.com", "supersecret")).EnsureSuccessStatusCode(); // anonymous request #1

        var stranger = From(factory, "203.0.113.1");
        (await stranger.GetAsync("/api/health")).EnsureSuccessStatusCode(); // #2
        (await stranger.GetAsync("/api/health")).EnsureSuccessStatusCode(); // #3
        Assert.Equal(HttpStatusCode.TooManyRequests, (await stranger.GetAsync("/api/health")).StatusCode);

        // Same address, but a session: accountability replaces the limit.
        for (var i = 0; i < 5; i++)
            (await session.GetAsync("/api/spaces")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Token_minting_is_limited_per_account()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminWithLimitsAsync(factory, new { TokenMintLimitPerHour = 2 });

        (await admin.PostAsJsonAsync("/api/api-tokens", new { Name = "one" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/api-tokens", new { Name = "two" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await admin.PostAsJsonAsync("/api/api-tokens", new { Name = "three" })).StatusCode);
    }

    [Fact]
    public async Task Limits_must_be_positive()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/settings", new { LockoutThreshold = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/settings", new { LoginRateLimitPerMinute = -1 })).StatusCode);
    }
}
