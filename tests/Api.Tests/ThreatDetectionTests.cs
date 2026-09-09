using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Threat detection and admin alerting (dev-plan 3.3): each detector fires at
/// its threshold and not below, the cooldown suppresses repeats, admins are
/// notified, and the blocklist turns callers away before authentication.
/// </summary>
public class ThreatDetectionTests
{
    private record RegisteredDto(Guid Id);
    private record AlertDto(Guid Id, string Kind, int Severity, string Key, string? Ip, int Status, string? Note);
    private record EventDto(Guid Id, string Kind, int Severity, string Key);
    private record NotificationDto(Guid Id, string Action, string TargetType, Guid TargetId);
    private record BlockDto(Guid Id, string Cidr);
    private record OverviewDto(int OpenAlerts, int CriticalOpen, int BlockedNetworks, long BlockedHits);
    private const string Doc = """{"type":"doc","content":[]}""";

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

    /// <summary>The admin on loopback, with address limits high enough not to interfere.</summary>
    private static async Task<HttpClient> AdminAsync(TestAppFactory factory)
    {
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings",
            new { LoginRateLimitPerMinute = 10_000, AnonymousRateLimitPerMinute = 100_000, LockoutThreshold = 1000 }))
            .EnsureSuccessStatusCode();
        return admin;
    }

    private static async Task<List<AlertDto>> AlertsAsync(HttpClient admin, string kind) =>
        (await admin.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all"))!
            .Where(a => a.Kind == kind).ToList();

    [Fact]
    public async Task A_burst_of_failed_sign_ins_from_one_address_alerts_once()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        var attacker = From(factory, "203.0.113.5");
        for (var i = 0; i < SecurityThresholds.FailedLoginsPerAddress - 1; i++)
            await LoginAsync(attacker, "admin@example.com", "nope");
        Assert.Empty(await AlertsAsync(admin, "login.failed_burst_ip")); // below threshold: nothing

        await LoginAsync(attacker, "admin@example.com", "nope");
        var alert = Assert.Single(await AlertsAsync(admin, "login.failed_burst_ip"));
        Assert.Equal("203.0.113.5", alert.Ip);

        // Cooldown: the attack continuing does not produce an alert a second.
        for (var i = 0; i < SecurityThresholds.FailedLoginsPerAddress; i++)
            await LoginAsync(attacker, "admin@example.com", "nope");
        Assert.Single(await AlertsAsync(admin, "login.failed_burst_ip"));

        // And every admin was told, with a pointer to the alert.
        var notes = await admin.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        var note = Assert.Single(notes!, n => n.Action == "security.alert" && n.TargetId == alert.Id);
        Assert.Equal("security", note.TargetType);
    }

    [Fact]
    public async Task Failures_against_many_accounts_from_one_address_is_credential_stuffing()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        var attacker = From(factory, "203.0.113.6");
        for (var i = 1; i <= SecurityThresholds.DistinctAccountsPerAddress; i++)
            await LoginAsync(attacker, $"victim{i}@example.com", "nope"); // accounts need not exist

        var alert = Assert.Single(await AlertsAsync(admin, "login.credential_stuffing"));
        Assert.Equal(2, alert.Severity); // Critical
    }

    [Fact]
    public async Task An_admin_signing_in_from_a_new_address_is_flagged_but_not_the_first_time()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        // First sign-in ever: no history, no baseline, no alert.
        (await LoginAsync(From(factory, "203.0.113.10"), "admin@example.com", "supersecret")).EnsureSuccessStatusCode();
        Assert.Empty(await AlertsAsync(admin, "login.admin_new_address"));

        // Same address again: known, quiet.
        (await LoginAsync(From(factory, "203.0.113.10"), "admin@example.com", "supersecret")).EnsureSuccessStatusCode();
        Assert.Empty(await AlertsAsync(admin, "login.admin_new_address"));

        // A different one: flagged.
        (await LoginAsync(From(factory, "198.51.100.77"), "admin@example.com", "supersecret")).EnsureSuccessStatusCode();
        var alert = Assert.Single(await AlertsAsync(admin, "login.admin_new_address"));
        Assert.Equal("198.51.100.77", alert.Ip);

        // Members are not tracked this way — their blast radius is smaller
        // and the false-positive rate on a mobile workforce would be high.
        await RegisterAsync(factory.CreateClient(), "member@example.com");
        await LoginAsync(From(factory, "203.0.113.1"), "member@example.com", "supersecret");
        await LoginAsync(From(factory, "203.0.113.2"), "member@example.com", "supersecret");
        Assert.Single(await AlertsAsync(admin, "login.admin_new_address"));
    }

    [Fact]
    public async Task Promoting_an_administrator_always_alerts()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");

        (await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        var alert = Assert.Single(await AlertsAsync(admin, "admin.promoted"));
        Assert.Equal(member.Id.ToString(), alert.Key);
    }

    [Fact]
    public async Task Toggling_public_spaces_is_a_critical_alert_in_both_directions()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        (await admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = false })).EnsureSuccessStatusCode();
        var alerts = await AlertsAsync(admin, "settings.public_spaces_toggled");
        Assert.Equal(2, alerts.Count);
        Assert.All(alerts, a => Assert.Equal(2, a.Severity));
    }

    [Fact]
    public async Task Trashing_many_pages_quickly_alerts()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var spaceId = await admin.CreateSpaceAsync();

        var ids = new List<Guid>();
        for (var i = 0; i < SecurityThresholds.PagesRemovedPerActor; i++)
        {
            var page = await (await admin.PostAsJsonAsync("/api/pages",
                new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = $"P{i}", ContentJson = Doc }))
                .Content.ReadFromJsonAsync<Dictionary<string, object>>();
            ids.Add(Guid.Parse(page!["id"].ToString()!));
        }
        foreach (var id in ids.Take(SecurityThresholds.PagesRemovedPerActor - 1))
            (await admin.DeleteAsync($"/api/pages/{id}")).EnsureSuccessStatusCode();
        Assert.Empty(await AlertsAsync(admin, "content.mass_removal"));

        (await admin.DeleteAsync($"/api/pages/{ids[^1]}")).EnsureSuccessStatusCode();
        Assert.Single(await AlertsAsync(admin, "content.mass_removal"));
    }

    [Fact]
    public async Task Minting_many_tokens_quickly_alerts()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        (await admin.PutAsJsonAsync("/api/admin/settings", new { TokenMintLimitPerHour = 100 })).EnsureSuccessStatusCode();

        for (var i = 0; i < SecurityThresholds.TokensPerActor; i++)
            (await admin.PostAsJsonAsync("/api/api-tokens", new { Name = $"t{i}" })).EnsureSuccessStatusCode();
        Assert.Single(await AlertsAsync(admin, "token.minting_burst"));
    }

    [Fact]
    public async Task A_webhook_aimed_inside_the_network_alerts()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        await admin.CreateSpaceAsync("HOOK");

        // Refused by 3.4's egress guard — and still reported, because the
        // attempt is the interesting part.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/spaces/HOOK/webhooks",
            new { Url = "http://169.254.169.254/latest/meta-data/", Events = "*" })).StatusCode);
        Assert.Single(await AlertsAsync(admin, "webhook.private_target"));
    }

    [Fact]
    public async Task A_broken_audit_chain_becomes_a_critical_alert()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        await admin.CreateSpaceAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AuditLogs.OrderBy(a => a.Sequence).FirstAsync();
            row.Action = "nothing.to.see";
            await db.SaveChangesAsync();
        }

        await factory.Services.GetRequiredService<AuditChainMonitor>().RunOnceAsync(CancellationToken.None);
        var alert = Assert.Single(await AlertsAsync(admin, "audit.chain_broken"));
        Assert.Equal(2, alert.Severity);
    }

    [Fact]
    public async Task Alerts_are_acknowledged_and_resolved_with_a_note()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");
        await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 });
        var alert = Assert.Single(await AlertsAsync(admin, "admin.promoted"));

        var acked = await (await admin.PostAsJsonAsync($"/api/admin/security/alerts/{alert.Id}/acknowledge",
            new { Note = "looking" })).Content.ReadFromJsonAsync<AlertDto>();
        Assert.Equal(1, acked!.Status);

        var resolved = await (await admin.PostAsJsonAsync($"/api/admin/security/alerts/{alert.Id}/resolve",
            new { Note = "expected: onboarding" })).Content.ReadFromJsonAsync<AlertDto>();
        Assert.Equal(2, resolved!.Status);
        Assert.Equal("expected: onboarding", resolved.Note);

        // Resolved alerts drop out of the default (open) list.
        var open = await admin.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts");
        Assert.DoesNotContain(open!, a => a.Id == alert.Id);
    }

    [Fact]
    public async Task A_blocked_address_is_refused_before_authentication()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);

        var victim = From(factory, "203.0.113.50");
        (await LoginAsync(victim, "admin@example.com", "supersecret")).EnsureSuccessStatusCode();
        (await victim.GetAsync("/api/spaces")).EnsureSuccessStatusCode();

        var block = await (await admin.PostAsJsonAsync("/api/admin/security/blocks",
            new { Cidr = "203.0.113.0/24", Reason = "scanner" })).Content.ReadFromJsonAsync<BlockDto>();
        Assert.Equal("203.0.113.0/24", block!.Cidr);

        // A valid session does not help: the door is shut before auth runs.
        Assert.Equal(HttpStatusCode.Forbidden, (await victim.GetAsync("/api/spaces")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await From(factory, "203.0.113.99").GetAsync("/api/health")).StatusCode);
        (await From(factory, "198.51.100.1").GetAsync("/api/health")).EnsureSuccessStatusCode();

        var overview = await admin.GetFromJsonAsync<OverviewDto>("/api/admin/security/overview");
        Assert.Equal(1, overview!.BlockedNetworks);
        Assert.True(overview.BlockedHits >= 2);

        (await admin.DeleteAsync($"/api/admin/security/blocks/{block.Id}")).EnsureSuccessStatusCode();
        (await victim.GetAsync("/api/spaces")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task You_cannot_block_your_own_address()
    {
        using var factory = new TestAppFactory();
        var admin = await AdminAsync(factory);
        // The admin is on loopback.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/security/blocks",
            new { Cidr = "127.0.0.0/8" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/security/blocks",
            new { Cidr = "not-an-address" })).StatusCode);
    }

    [Fact]
    public void Events_are_append_only_from_the_apps_point_of_view()
    {
        // SQLite cannot enforce the role split; what can be checked is that
        // the table is on the list the owner connection revokes from.
        Assert.Contains("SecurityEvents", DatabaseRoles.AppendOnlyTables);
    }

    [Fact]
    public async Task Members_see_none_of_it()
    {
        using var factory = new TestAppFactory();
        await AdminAsync(factory);
        var member = factory.CreateClient();
        await RegisterAsync(member, "member@example.com");

        foreach (var path in new[] { "/api/admin/security/overview", "/api/admin/security/events",
                     "/api/admin/security/alerts", "/api/admin/security/blocks" })
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(path)).StatusCode);
    }
}
