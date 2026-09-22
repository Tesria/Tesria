using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Usage telemetry (dev-plan 0.3). Recorded now, ahead of the dashboard that
/// consumes it (2.5), so that dashboard ships with real history rather than an
/// empty chart.
/// </summary>
public class TelemetryTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role);
    private record PageDto(Guid Id, string Title);
    private record TokenDto(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, string Token);

    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<Guid> CreatePageAsync(HttpClient client, Guid spaceId)
    {
        var res = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Read me", ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PageDto>())!.Id;
    }

    [Fact]
    public async Task A_successful_login_is_audited_with_the_actor_and_a_failure_is_not_attributed()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var user = await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "who@example.com", DisplayName = "Who", Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>();

        await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "who@example.com", Password = "supersecret" });
        await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "who@example.com", Password = "wrong" });
        await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "nobody@example.com", Password = "wrong" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ok = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "user.login");
        Assert.Equal(user!.Id, ok.ActorId);

        var failures = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "user.login_failed").ToListAsync();
        Assert.Equal(2, failures.Count);

        // A failure is never attributed to an account, and never records the
        // address that was tried: the log must not become a list of addresses
        // somebody guessed, nor confirm which of them exist.
        Assert.All(failures, f =>
        {
            Assert.Null(f.ActorId);
            Assert.Null(f.TargetId);
            Assert.DoesNotContain("who@example.com", f.MetadataJson);
            Assert.DoesNotContain("nobody@example.com", f.MetadataJson);
        });
    }

    [Fact]
    public async Task Reading_a_page_in_a_browser_session_records_a_view()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var pageId = await CreatePageAsync(client, spaceId);

        (await client.GetAsync($"/api/pages/{pageId}")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/pages/{pageId}")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var views = await db.PageViews.AsNoTracking().Where(v => v.PageId == pageId).ToListAsync();

        Assert.Equal(2, views.Count);
        Assert.All(views, v => Assert.Equal(userId, v.UserId));
    }

    [Fact]
    public async Task An_api_token_read_is_not_counted_as_a_view()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var pageId = await CreatePageAsync(client, spaceId);

        var token = await (await client.PostAsJsonAsync("/api/api-tokens", new { Name = "ci" }))
            .Content.ReadFromJsonAsync<TokenDto>();

        var script = factory.CreateClient();
        script.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        (await script.GetAsync($"/api/pages/{pageId}")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // A nightly export would otherwise dwarf every human in "most viewed".
        Assert.Empty(await db.PageViews.AsNoTracking().Where(v => v.PageId == pageId).ToListAsync());
    }

    [Fact]
    public async Task A_refused_read_is_not_counted()
    {
        using var factory = new TestAppFactory();

        var owner = factory.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync();
        var pageId = await CreatePageAsync(owner, spaceId);

        var spaces = await owner.GetFromJsonAsync<List<TestHelpers.SpaceDto>>("/api/spaces");
        var key = spaces!.Single(s => s.Id == spaceId).Key;
        await owner.PostAsJsonAsync($"/api/spaces/{key}/permissions",
            new { PrincipalType = 0, PrincipalId = ownerId, Operation = 2 });

        var outsider = factory.CreateClient();
        await outsider.RegisterAndSignInAsync();
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/pages/{pageId}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.PageViews.AsNoTracking().Where(v => v.PageId == pageId).ToListAsync());
    }

    [Fact]
    public async Task An_authenticated_request_stamps_last_seen()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();

        (await client.GetAsync("/api/spaces")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lastSeen = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).LastSeenAt;

        Assert.NotNull(lastSeen);
        Assert.True(DateTimeOffset.UtcNow - lastSeen < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Last_seen_writes_are_throttled_per_user()
    {
        var tracker = new Tesria.Api.Infrastructure.Telemetry.LastSeenTracker();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // First request for a user writes; the next several do not: otherwise
        // this would be one UPDATE per request to learn almost nothing.
        Assert.True(tracker.ShouldWrite(a));
        Assert.False(tracker.ShouldWrite(a));
        Assert.False(tracker.ShouldWrite(a));

        // Throttling is per user, not global.
        Assert.True(tracker.ShouldWrite(b));
    }
}
