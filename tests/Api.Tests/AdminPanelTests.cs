using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The admin panel's server side (dev-plan 2.2, 2.4, 2.5). The guards matter
/// more than the listings: several of these actions can lock an instance out
/// of its own administration.
/// </summary>
public class AdminPanelTests
{
    private record RegisteredDto(Guid Id, string Email, string DisplayName, int Role,
        string? AvatarHash, int? AvatarVariant, bool HasPassword, List<string> RecoveryCodes);
    private record AdminUserDto(
        Guid Id, string Email, string DisplayName, int Role, int Status,
        string? AvatarHash, int? AvatarVariant, bool HasPassword, bool IsSso,
        int RecoveryCodesRemaining, DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt);
    private record AdminSpaceDto(
        Guid Id, string Key, string Name, string? Description, bool Archived,
        Guid CreatedById, string CreatedByName, int PageCount, long StorageBytes, DateTimeOffset CreatedAt);
    private record TokenDto(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, string Token);
    private record DailyPoint(DateOnly Date, int Count);
    private record PeopleDto(int Total, int Admins, int Suspended, int ActiveLast7Days,
        int ActiveLast30Days, int NewInRange, List<DailyPoint> LoginsPerDay, List<DailyPoint> FailedLoginsPerDay);
    private record ContentDto(int Spaces, int Pages, int Versions, int Comments, int Attachments,
        long StorageBytes, List<DailyPoint> PagesCreatedPerDay);
    private record TopPageDto(Guid PageId, string Title, string SpaceKey, int Views);
    private record UsageDto(int ViewsInRange, List<DailyPoint> ViewsPerDay,
        List<TopPageDto> TopPages, List<object> TopEditors);
    private record DashboardDto(int RangeDays, DateTimeOffset GeneratedAt,
        PeopleDto People, ContentDto Content, UsageDto Usage);

    private const int Member = 0, Admin = 1;
    private const int Active = 0, Suspended = 1;
    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<RegisteredDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<RegisteredDto>())!;

    [Fact]
    public async Task The_user_list_reports_role_status_and_recovery_state()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        await RegisterAsync(factory.CreateClient(), "member@example.com");

        var users = await admin.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users");
        Assert.Equal(2, users!.Count);

        var owner = users.Single(u => u.Email == "admin@example.com");
        Assert.Equal(Admin, owner.Role);
        Assert.Equal(Active, owner.Status);
        Assert.False(owner.IsSso);
        // Everyone registering now gets a set, so the admin can see who has none.
        Assert.Equal(8, owner.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task Suspending_an_account_kills_its_live_session()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        var member = factory.CreateClient();
        var registered = await RegisterAsync(member, "member@example.com");
        (await member.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        (await admin.PutAsJsonAsync($"/api/admin/users/{registered.Id}/status",
            new { Status = Suspended })).EnsureSuccessStatusCode();

        // The point of a suspension: it takes effect now, not when the cookie
        // happens to expire.
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { Email = "member@example.com", Password = "supersecret" })).StatusCode);

        // And it is reversible.
        (await admin.PutAsJsonAsync($"/api/admin/users/{registered.Id}/status",
            new { Status = Active })).EnsureSuccessStatusCode();
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Email = "member@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_demoted_or_suspended()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        var owner = await RegisterAsync(admin, "admin@example.com");
        await RegisterAsync(factory.CreateClient(), "member@example.com");

        // An instance with no admin has no way back: nobody could change
        // settings, issue invites or restore access without editing the database.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            $"/api/admin/users/{owner.Id}/role", new { Role = Member })).StatusCode);

        // Self-suspension is refused separately, before the last-admin check.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            $"/api/admin/users/{owner.Id}/status", new { Status = Suspended })).StatusCode);
    }

    [Fact]
    public async Task Demotion_is_allowed_once_another_admin_exists()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        var owner = await RegisterAsync(admin, "admin@example.com");
        var second = await RegisterAsync(factory.CreateClient(), "second@example.com");

        (await admin.PutAsJsonAsync($"/api/admin/users/{second.Id}/role", new { Role = Admin }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/users/{owner.Id}/role", new { Role = Member }))
            .EnsureSuccessStatusCode();

        // Having demoted itself, that session is no longer an admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Sessions_and_tokens_are_revoked_separately()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        var member = factory.CreateClient();
        var registered = await RegisterAsync(member, "member@example.com");
        var token = await (await member.PostAsJsonAsync("/api/api-tokens", new { Name = "ci" }))
            .Content.ReadFromJsonAsync<TokenDto>();

        var script = factory.CreateClient();
        script.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);

        (await admin.PostAsync($"/api/admin/users/{registered.Id}/revoke-sessions", null))
            .EnsureSuccessStatusCode();

        // A token authenticates through a different scheme, so revoking
        // sessions deliberately leaves it working — "lock this account out"
        // needs both actions, which is why they are separate. Probed on a
        // route that stays closed to anonymous callers (dev-plan 5.2 opened
        // the spaces list to them).
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/me")).StatusCode);
        (await script.GetAsync("/api/notifications")).EnsureSuccessStatusCode();

        (await admin.PostAsync($"/api/admin/users/{registered.Id}/revoke-tokens", null))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await script.GetAsync("/api/notifications")).StatusCode);
    }

    [Fact]
    public async Task The_space_list_reports_counts_and_storage_but_no_content()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var spaceId = await admin.CreateSpaceAsync();
        await admin.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Secret", ContentJson = Doc });

        var spaces = await admin.GetFromJsonAsync<List<AdminSpaceDto>>("/api/admin/spaces");
        var space = spaces!.Single(s => s.Id == spaceId);

        Assert.Equal(1, space.PageCount);
        Assert.Equal(0, space.StorageBytes);
        Assert.Equal("admin@example.com", space.CreatedByName);

        // Metadata only — admins do not bypass space permissions, so this
        // endpoint must not become a way around that.
        var raw = await admin.GetStringAsync("/api/admin/spaces");
        Assert.DoesNotContain("contentJson", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_dashboard_returns_every_section_in_one_response()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var spaceId = await admin.CreateSpaceAsync();
        var page = await (await admin.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Read me", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var pageId = Guid.Parse(page!["id"].ToString()!);
        await admin.GetAsync($"/api/pages/{pageId}");

        var dash = await admin.GetFromJsonAsync<DashboardDto>("/api/admin/dashboard?rangeDays=30");

        Assert.Equal(30, dash!.RangeDays);
        Assert.Equal(1, dash.People.Total);
        Assert.Equal(1, dash.People.Admins);
        Assert.Equal(1, dash.Content.Spaces);
        Assert.Equal(1, dash.Content.Pages);
        Assert.Equal(1, dash.Usage.ViewsInRange);
        Assert.Equal("Read me", dash.Usage.TopPages.Single().Title);

        // Every day in the range is present, including the empty ones — a
        // sparkline built only from active days reads as steady use when the
        // truth is the opposite.
        Assert.Equal(30, dash.Usage.ViewsPerDay.Count);
        Assert.Equal(30, dash.People.LoginsPerDay.Count);
    }

    [Fact]
    public async Task The_dashboard_range_is_clamped()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        Assert.Equal(365, (await admin.GetFromJsonAsync<DashboardDto>("/api/admin/dashboard?rangeDays=99999"))!.RangeDays);
        Assert.Equal(1, (await admin.GetFromJsonAsync<DashboardDto>("/api/admin/dashboard?rangeDays=0"))!.RangeDays);
    }

    [Fact]
    public async Task Every_admin_endpoint_refuses_a_member()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "admin@example.com");

        var member = factory.CreateClient();
        var registered = await RegisterAsync(member, "member@example.com");

        foreach (var path in new[] { "/api/admin/users", "/api/admin/spaces", "/api/admin/dashboard", "/api/audit" })
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(path)).StatusCode);

        // Groups: readable by anyone signed in (the permission picker needs
        // the list), shaped only by administrators.
        (await member.GetAsync("/api/groups")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync("/api/groups", new { Name = "Rogue" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync(
            $"/api/admin/users/{registered.Id}/role", new { Role = Admin })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsync($"/api/admin/users/{registered.Id}/revoke-sessions", null)).StatusCode);
    }
}
