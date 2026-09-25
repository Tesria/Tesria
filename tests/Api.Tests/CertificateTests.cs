using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The certificate card in Administration (dev-plan 14.4, the review's
/// SEC-01): the fingerprint people compare before trusting the server, given
/// only on a connection nobody on the network could have altered.
/// </summary>
public class CertificateTests
{
    private record Status(bool OwnCertificate, bool Known, string? Channel, string? Sha256, string? Sha1, string? Subject);

    private static readonly LocalAuthority.Fingerprints Fake = new(
        "4B:1E:09:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66",
        "01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67", "Caddy Local Authority");

    private static async Task<(TestAppFactory, HttpClient)> OwnerAsync(Dictionary<string, string?>? settings = null)
    {
        var factory = settings is null ? new TestAppFactory() : new TestAppFactory(settings);
        factory.Services.GetRequiredService<LocalAuthority>().Set(Fake);
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        return (factory, owner);
    }

    private static async Task<Status> AskAsync(HttpClient client, string ip, string? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/certificate");
        request.Headers.Add(TestRemoteIpStartupFilter.Header, ip);
        if (host is not null) request.Headers.Host = host;
        var res = await client.SendAsync(request);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Status>())!;
    }

    [Fact]
    public async Task On_the_server_computer_the_card_shows_the_fingerprints()
    {
        var (factory, owner) = await OwnerAsync();
        using var _ = factory;
        var status = await AskAsync(owner, "127.0.0.1");
        Assert.Equal("server", status.Channel);
        Assert.Equal(Fake.Sha256, status.Sha256);
        Assert.Equal(Fake.Sha1, status.Sha1);
    }

    [Theory]
    [InlineData("192.168.1.20", null)]          // another computer on the LAN
    [InlineData("192.168.65.1", null)]          // Docker Desktop without the forwarder: anyone
    [InlineData("100.101.102.103", "wiki.local")] // a tailnet address, but not through Tailscale's name
    public async Task Over_a_connection_that_could_be_intercepted_it_withholds_them(string ip, string? host)
    {
        var (factory, owner) = await OwnerAsync();
        using var _ = factory;
        var status = await AskAsync(owner, ip, host);
        Assert.True(status.OwnCertificate);
        Assert.True(status.Known);
        Assert.Null(status.Channel);
        Assert.Null(status.Sha256);
        Assert.Null(status.Sha1);
    }

    [Fact]
    public async Task Through_tailscale_at_its_own_name_the_card_shows_them()
    {
        // The browser checked Tailscale's public certificate on the way in.
        var (factory, owner) = await OwnerAsync();
        using var _ = factory;
        var status = await AskAsync(owner, "100.101.102.103", "tesria.example-tailnet.ts.net");
        Assert.Equal("tailnet", status.Channel);
        Assert.Equal(Fake.Sha256, status.Sha256);
    }

    [Fact]
    public async Task A_server_with_a_public_certificate_has_nothing_to_show()
    {
        var (factory, owner) = await OwnerAsync(new() { ["Tls:Caddyfile"] = "deploy/Caddyfile.public" });
        using var _ = factory;
        var status = await AskAsync(owner, "127.0.0.1");
        Assert.False(status.OwnCertificate);
        Assert.Null(status.Sha256);
    }

    [Fact]
    public async Task Only_administrators_see_the_card()
    {
        var (factory, owner) = await OwnerAsync();
        using var _ = factory;
        var member = factory.CreateClient();
        await member.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/certificate")).StatusCode);
    }
}
