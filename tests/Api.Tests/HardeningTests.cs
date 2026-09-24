using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tesria.Api.Infrastructure;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Enterprise hardening (dev-plan 14.3), each item held here once built.</summary>
public class HardeningTests
{
    private record UserDto(Guid Id);

    [Fact]
    public async Task Every_new_account_is_in_the_audit_log_with_how_it_was_made()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        (await owner.PostAsJsonAsync("/api/auth/register", new { Email = "owner@example.com", DisplayName = "Owner", Password = "supersecret" })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = false })).EnsureSuccessStatusCode();
        var invite = await (await owner.PostAsJsonAsync("/api/admin/invites", new { })).Content.ReadFromJsonAsync<JsonElement>();
        (await f.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "sam@example.com", DisplayName = "Sam", Password = "supersecret", InviteToken = invite.GetProperty("token").GetString() }))
            .EnsureSuccessStatusCode();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AuditLogs.Where(a => a.Action == "user.registered").Select(a => a.MetadataJson!).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, m => m.Contains("\"How\":\"first account\"") && m.Contains("owner@example.com"));
        Assert.Contains(rows, m => m.Contains("\"How\":\"invite\"") && m.Contains("sam@example.com"));
    }

    [Theory]
    [InlineData("64:ff9b::a00:1", true)]        // NAT64 of 10.0.0.1
    [InlineData("64:ff9b::7f00:1", true)]       // NAT64 of 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe", true)]    // NAT64 of 169.254.169.254
    [InlineData("64:ff9b::808:808", false)]     // NAT64 of 8.8.8.8: a public host
    [InlineData("64:ff9b:1::a00:1", true)]      // local-use NAT64 of 10.0.0.1
    [InlineData("2002:c0a8:101::1", true)]      // 6to4 of 192.168.1.1
    [InlineData("2002:808:808::1", false)]      // 6to4 of 8.8.8.8
    [InlineData("100.100.100.100", true)]       // carrier NAT, Tailscale's range
    [InlineData("198.18.0.1", true)]
    [InlineData("100::1", true)]
    [InlineData("2606:4700::1111", false)]
    public void Addresses_that_carry_a_private_address_are_private(string ip, bool expected) =>
        Assert.Equal(expected, Tesria.Api.Infrastructure.Security.PrivateNetworks.IsPrivateOrLocal(IPAddress.Parse(ip)));

    [Fact]
    public async Task Queued_email_is_sent_in_the_background()
    {
        var recorder = new RecordingEmailSender();
        var services = new ServiceCollection()
            .AddSingleton<Tesria.Api.Infrastructure.Email.IEmailSender>(recorder)
            .BuildServiceProvider();
        var queue = new Tesria.Api.Infrastructure.Email.EmailQueue(
            services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Tesria.Api.Infrastructure.Email.EmailQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        queue.Enqueue(new Tesria.Api.Infrastructure.Email.EmailMessage("a@example.com", "Subject", "Body"));
        for (var i = 0; i < 50 && recorder.Sent.Count == 0; i++) await Task.Delay(20);

        Assert.Equal("a@example.com", Assert.Single(recorder.Sent).To);
        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Under_Compose_only_loopback_and_the_stacks_subnet_are_trusted_proxies()
    {
        static Microsoft.Extensions.Configuration.IConfiguration Config(params (string, string)[] pairs) =>
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(pairs.ToDictionary(p => p.Item1, p => (string?)p.Item2)).Build();
        var ts = Tesria.Api.Infrastructure.Security.ProxyTrust.TrustedNetworksFrom;

        Assert.Equal("127.0.0.0/8,::1/128,10.203.0.0/24", ts(Config(("Proxy:ComposeSubnet", "10.203.0.0/24"))));
        // An explicit list still wins, and outside Compose the old default applies.
        Assert.Equal("192.0.2.10/32", ts(Config(("Proxy:ComposeSubnet", "10.203.0.0/24"), ("Proxy:TrustedNetworks", "192.0.2.10/32"))));
        Assert.Null(ts(Config()));

        var networks = Tesria.Api.Infrastructure.Security.ProxyTrust.ParseNetworks(ts(Config(("Proxy:ComposeSubnet", "10.203.0.0/24")))).ToList();
        Assert.Contains(networks, n => n.Contains(IPAddress.Parse("10.203.0.7")));
        Assert.DoesNotContain(networks, n => n.Contains(IPAddress.Parse("192.168.1.20")));
    }

    /// <summary>Remembers what the collaboration service would have been told to close.</summary>
    private sealed class RecordingCollab : Tesria.Api.Infrastructure.Collab.ICollabNotifier
    {
        public System.Collections.Concurrent.ConcurrentQueue<Tesria.Api.Infrastructure.Collab.CollabRevocation> Revoked = new();
        public Task NotifyAsync(Guid pageId, string contentJson, Tesria.Api.Infrastructure.Collab.WriteSource source, int version, CancellationToken ct = default) => Task.CompletedTask;
        public Task MaintenanceAsync(bool on, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeAsync(Tesria.Api.Infrastructure.Collab.CollabRevocation revocation, CancellationToken ct = default)
        {
            Revoked.Enqueue(revocation);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Changes_to_access_close_the_live_editing_connections_they_touch()
    {
        var collab = new RecordingCollab();
        using var factory = new TestAppFactory();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<Tesria.Api.Infrastructure.Collab.ICollabNotifier>();
            s.AddSingleton<Tesria.Api.Infrastructure.Collab.ICollabNotifier>(collab);
        }));
        var owner = app.CreateClient();
        (await owner.PostAsJsonAsync("/api/auth/register", new { Email = "owner@example.com", DisplayName = "Owner", Password = "supersecret" })).EnsureSuccessStatusCode();
        var member = app.CreateClient();
        var sam = (await (await member.PostAsJsonAsync("/api/auth/register", new { Email = "sam@example.com", DisplayName = "Sam", Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>())!.Id;
        var spaceId = await owner.CreateSpaceAsync("LIVE");
        var page = (await (await owner.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, Title = "Plan",
            ContentJson = """{"type":"doc","content":[{"type":"paragraph"}]}""" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        async Task<List<Tesria.Api.Infrastructure.Collab.CollabRevocation>> After(Func<Task> act)
        {
            collab.Revoked.Clear();
            await act();
            return [.. collab.Revoked];
        }

        Assert.Contains(new(SpaceId: spaceId), await After(async () =>
            (await owner.PostAsJsonAsync("/api/spaces/LIVE/permissions", new { PrincipalType = 0, PrincipalId = sam, Operation = 0 })).EnsureSuccessStatusCode()));
        Assert.Contains(new(PageId: page), await After(async () =>
            (await owner.PostAsJsonAsync($"/api/pages/{page}/restrictions", new { PrincipalType = 0, PrincipalId = sam, Operation = 1 })).EnsureSuccessStatusCode()));
        var group = (await (await owner.PostAsJsonAsync("/api/groups", new { Name = "Team", Description = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Contains(new(UserId: sam), await After(async () =>
            (await owner.PostAsJsonAsync($"/api/groups/{group}/members", new { UserId = sam })).EnsureSuccessStatusCode()));
        Assert.Contains(new(All: true), await After(async () =>
            (await owner.DeleteAsync($"/api/groups/{group}")).EnsureSuccessStatusCode()));
        Assert.Contains(new(UserId: sam), await After(async () =>
            (await owner.PutAsJsonAsync($"/api/admin/users/{sam}/status", new { Status = 1 })).EnsureSuccessStatusCode()));
        // Nothing to do with access: nothing closed.
        Assert.Empty(await After(async () =>
            (await owner.PutAsJsonAsync($"/api/pages/{page}", new { Title = "Plan", ContentJson = """{"type":"doc","content":[{"type":"paragraph"}]}""" })).EnsureSuccessStatusCode()));
    }

    [Fact]
    public async Task Pictures_can_be_limited_to_listed_hosts_everywhere_the_rule_applies()
    {
        using var f = new TestAppFactory();
        var admin = f.CreateClient();
        await admin.RegisterAndSignInAsync();

        static string ImgSrc(HttpResponseMessage res) =>
            res.Headers.GetValues("Content-Security-Policy").Single()
                .Split("; ").Single(d => d.StartsWith("img-src "));

        // Off by default: any https host, and the editor is told nothing is restricted.
        var before = await admin.GetAsync("/api/instance");
        Assert.Equal("img-src 'self' data: blob: https:", ImgSrc(before));
        Assert.Equal(JsonValueKind.Null, (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imageHosts").ValueKind);

        (await admin.PutAsJsonAsync("/api/admin/settings", new { RestrictImageHosts = true, ImageAllowlist = " .Imgur.com \ncdn.example.org" }))
            .EnsureSuccessStatusCode();

        var after = await f.CreateClient().GetAsync("/api/instance");
        Assert.Equal("img-src 'self' data: blob: https://*.imgur.com https://cdn.example.org", ImgSrc(after));
        var hosts = (await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imageHosts").EnumerateArray().Select(h => h.GetString()).ToList();
        Assert.Equal([".imgur.com", "cdn.example.org"], hosts);

        // An exported page carries the same rule, since no header of ours reaches it.
        using var scope = f.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<Tesria.Api.Infrastructure.Settings.ISiteSettingsService>().GetAsync();
        Assert.Equal(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"img-src 'self' data: blob: https://*.imgur.com https://cdn.example.org\" />",
            Tesria.Api.Infrastructure.Security.ImagePolicy.ExportMeta(settings));

        // Restricted with nothing listed: uploads only.
        (await admin.PutAsJsonAsync("/api/admin/settings", new { ImageAllowlist = "" })).EnsureSuccessStatusCode();
        Assert.Equal("img-src 'self' data: blob:", ImgSrc(await admin.GetAsync("/api/instance")));
    }
}
