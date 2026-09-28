using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// "Trust this device" (dev-plan 15.5): the page. Since 14.4 (the review's
/// SEC-01) it serves no scripts and shows no fingerprint: both would arrive
/// over the same unprotected connection as the certificate they vouch for.
/// </summary>
public class TrustTests
{
    private static HttpClient Client(TestAppFactory factory, string host = "wiki-server.local")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    [Fact]
    public async Task The_page_opens_without_signing_in_and_fills_in_the_address_it_was_reached_at()
    {
        using var factory = new TestAppFactory();
        var res = await Client(factory).GetAsync("/trust");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType!.MediaType);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Trust This Server on Your Device", html);
        Assert.Contains("value=\"wiki-server.local\"", html);
        Assert.Contains("https://wiki-server.local/", html);
        // Everything it needs comes from /trust, so it works over plain HTTP.
        Assert.Contains("src=\"/trust/trust.js\"", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task Windows_gets_a_line_that_prints_the_fingerprint_and_a_checked_one()
    {
        // PowerShell's execution policy stops a downloaded .ps1 from running,
        // and an employer can lock it (2026-09-23). A typed command is not
        // subject to it, and CurrentUser\Root needs no administrator. Since
        // 2026-09-27 checking is optional (the owner's decision, SSH-style):
        // the line shown prints the fingerprint and trusts; the checked form,
        // which the page swaps in once one is pasted, imports nothing unless
        // it matches.
        using var factory = new TestAppFactory();
        var html = System.Net.WebUtility.HtmlDecode(await Client(factory).GetStringAsync("/trust"));
        Assert.Contains("Invoke-WebRequest -UseBasicParsing -Uri \"http://wiki-server.local/ca.crt\"", html);
        Assert.Contains("Write-Host (\"SHA-256: \"", html);
        Assert.Contains("-CertStoreLocation Cert:\\CurrentUser\\Root", html);
        Assert.DoesNotContain("PASTE-THE-FINGERPRINT", html);
        // The checked template, filled by the page's script.
        Assert.Contains("http://{address}/ca.crt", html);
        Assert.Contains("if ($h -eq \"{hex}\") { Import-Certificate", html);
    }

    [Fact]
    public async Task A_mac_trusts_with_one_typed_line_and_no_script()
    {
        // No script and no GitHub (2026-09-27), so it works on a network with
        // no internet: the certificate comes from the server itself.
        using var factory = new TestAppFactory();
        var html = System.Net.WebUtility.HtmlDecode(await Client(factory).GetStringAsync("/trust"));
        Assert.Contains("curl -fsS http://wiki-server.local/ca.crt -o /tmp/tesria-ca.crt && openssl x509", html);
        Assert.Contains("security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain", html);
        Assert.Contains("cut -d= -f2 | tr -d :)\" = \"{hex}\" ]; then sudo security add-trusted-cert", html);
    }

    [Fact]
    public async Task The_scripts_come_from_the_release_with_the_fingerprint_optional()
    {
        using var factory = new TestAppFactory();
        var html = System.Net.WebUtility.HtmlDecode(await Client(factory).GetStringAsync("/trust"));
        Assert.Contains("https://github.com/Tesria/Tesria/releases/latest/download/trust-ca.sh", html);
        Assert.Contains("bash trust-ca.sh wiki-server.local", html);
        Assert.Contains("bash trust-ca.sh --fingerprint {fingerprint} {address}", html);
        Assert.Contains("https://github.com/Tesria/Tesria/releases/latest/download/trust-ca.ps1", html);
        Assert.Contains("-Fingerprint {hex}", html);
    }

    [Fact]
    public async Task The_server_no_longer_hands_out_scripts()
    {
        // A script fetched over plain HTTP could be anyone's; an altered one
        // needs no certificate at all.
        using var factory = new TestAppFactory();
        foreach (var path in new[] { "/trust/trust-tesria.sh?address=wiki", "/trust/trust-tesria.ps1?address=wiki" })
        {
            var res = await Client(factory).GetAsync(path);
            var text = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("#!/usr/bin/env bash", text);
            Assert.DoesNotContain("Import-Certificate", text);
        }
    }

    [Fact]
    public async Task The_page_never_shows_a_fingerprint_and_defers_to_the_docs()
    {
        // The page is what an attacker on the network can change, so a
        // fingerprint on it would vouch for nothing.
        using var factory = new TestAppFactory();
        factory.Services.GetRequiredService<Tesria.Api.Infrastructure.Security.LocalAuthority>()
            .Set(new("AA:BB:CC:DD", "11:22:33", "Caddy Local Authority"));
        var html = await Client(factory).GetStringAsync("/trust");
        Assert.DoesNotContain("AA:BB:CC:DD", html);
        Assert.DoesNotContain("11:22:33", html);
        Assert.Contains("https://tesria.com/docs/installation-and-operations/trusting-the-local-certificate/", html);
        Assert.Contains("If this page and the docs ever differ, follow the docs.", html);
    }

    [Fact]
    public async Task Opened_by_number_the_page_names_the_server_when_it_knows_its_name()
    {
        // A number never matches the certificate, so the address bar says
        // "Not secure" even on a device that trusts the server (the owner's
        // Windows machine, 2026-09-23). The fix is the name; say which.
        using var named = new TestAppFactory(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://studio.local" });
        var html = await Client(named, "192.168.1.50").GetStringAsync("/trust");
        Assert.Contains("value=\"192.168.1.50\"", html);
        Assert.Contains("This server's name is <strong>studio.local</strong>", html);

        // localhost is no use to another device, so it is never suggested.
        using var local = new TestAppFactory(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://localhost" });
        var plain = await Client(local, "192.168.1.50").GetStringAsync("/trust");
        Assert.DoesNotContain("This server's name is", plain);
        Assert.Contains("Local hostname", plain);
    }

    [Fact]
    public async Task Its_script_and_stylesheets_are_served()
    {
        using var factory = new TestAppFactory();
        var client = Client(factory);
        Assert.Contains("guessDevice", await client.GetStringAsync("/trust/trust.js"));
        Assert.Contains(".trust-step", await client.GetStringAsync("/trust/trust.css"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/trust/app.css")).StatusCode);
    }

    [Fact]
    public async Task A_server_with_a_public_certificate_has_nothing_to_set_up()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Tls:Caddyfile"] = "deploy/Caddyfile.public" });
        var html = await Client(factory).GetStringAsync("/trust");
        Assert.Contains("Nothing to Set Up", html);
        Assert.DoesNotContain("trust-ca.sh", html);

        var instance = await Client(factory).GetFromJsonAsync<System.Text.Json.JsonElement>("/api/instance");
        Assert.False(instance.GetProperty("ownCertificate").GetBoolean());
    }

    [Fact]
    public async Task The_sign_in_page_is_told_when_the_server_has_its_own_certificate()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Tls:Caddyfile"] = "deploy/Caddyfile" });
        var instance = await Client(factory).GetFromJsonAsync<System.Text.Json.JsonElement>("/api/instance");
        Assert.True(instance.GetProperty("ownCertificate").GetBoolean());
    }

    [Fact]
    public void Both_scripts_check_a_fingerprint_when_given_and_print_it_otherwise()
    {
        // The release attaches these (14.4). Given a fingerprint, a mismatch
        // trusts nothing, and the check comes before any install; without one,
        // they print it and trust (2026-09-27, optional by the owner's decision).
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/scripts"));
        var sh = File.ReadAllText(Path.Combine(root, "trust-ca.sh"));
        Assert.Contains("Not checked, as no --fingerprint was given", sh);
        Assert.Contains("elif [ \"$ACTUAL\" != \"$EXPECTED\" ]; then", sh);
        Assert.True(sh.IndexOf("$ACTUAL\" != \"$EXPECTED", StringComparison.Ordinal) < sh.IndexOf("add-trusted-cert", StringComparison.Ordinal));
        var ps1 = File.ReadAllText(Path.Combine(root, "trust-ca.ps1")).Replace("\r\n", "\n");
        Assert.Contains("[string]$Fingerprint = \"\"", ps1);
        Assert.Contains("Not checked, as no -Fingerprint was given", ps1);
        Assert.True(ps1.IndexOf("elseif ($actual -ne $expected)", StringComparison.Ordinal) < ps1.IndexOf("Import-Certificate -FilePath", StringComparison.Ordinal));
    }
}
