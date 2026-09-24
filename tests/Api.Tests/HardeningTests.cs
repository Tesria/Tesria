using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}
