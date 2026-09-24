using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// "Trust this device" (dev-plan 15.5): the page, and the scripts with the
/// address written in. The address ends up in a script someone runs as an
/// administrator, so most of this is about what may not get in.
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
        Assert.Contains("Trust this server on your device", html);
        Assert.Contains("value=\"wiki-server.local\"", html);
        Assert.Contains("https://wiki-server.local/", html);
        // Everything it needs comes from /trust, so it works over plain HTTP.
        Assert.Contains("src=\"/trust/trust.js\"", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task Windows_gets_a_line_to_paste_because_script_files_are_blocked_by_default()
    {
        // PowerShell's execution policy stops a downloaded .ps1 from running,
        // and an employer can lock it (the owner's Windows machine, 2026-09-23).
        // A typed command is not subject to it, and CurrentUser\Root needs no
        // administrator.
        using var factory = new TestAppFactory();
        var html = System.Net.WebUtility.HtmlDecode(await Client(factory).GetStringAsync("/trust"));
        Assert.Contains("Invoke-WebRequest -UseBasicParsing -Uri \"http://wiki-server.local/ca.crt\"", html);
        Assert.Contains("-CertStoreLocation Cert:\\CurrentUser\\Root", html);
        // The template the page's script fills as the address changes.
        Assert.Contains("data-template=\"$c = ", html);
        Assert.Contains("http://{address}/ca.crt", html);
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
    public async Task The_mac_and_linux_script_comes_with_the_address_written_in()
    {
        using var factory = new TestAppFactory();
        var res = await Client(factory).GetAsync("/trust/trust-tesria.sh?address=Studio.Local");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("trust-tesria.sh", res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var text = await res.Content.ReadAsStringAsync();
        Assert.Contains("TESRIA_ADDRESS=\"studio.local\"", text);
        Assert.DoesNotContain("TESRIA_ADDRESS=\"localhost\"", text);
        Assert.StartsWith("#!/usr/bin/env bash", text);
        Assert.DoesNotContain("\r\n", text);
    }

    [Fact]
    public async Task The_windows_script_comes_with_the_address_written_in_and_as_windows_expects_it()
    {
        using var factory = new TestAppFactory();
        var bytes = await Client(factory).GetByteArrayAsync("/trust/trust-tesria.ps1?address=wiki-server.local");
        // A byte order mark, so Windows PowerShell 5.1 reads it as UTF-8.
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("$TesriaAddress = \"wiki-server.local\"", text);
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\r\r", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wiki;rm -rf ~")]
    [InlineData("wiki\"; Remove-Item C:\\ #")]
    [InlineData("$(id)")]
    [InlineData("`whoami`")]
    [InlineData("wiki server")]
    [InlineData("wiki\nserver")]
    [InlineData("https://wiki")]
    [InlineData("wiki:8443")]
    [InlineData("wiki/ca.crt")]
    [InlineData("-wiki")]
    [InlineData("wiki..local")]
    public async Task An_address_that_is_not_a_plain_name_is_refused_before_it_reaches_a_script(string address)
    {
        using var factory = new TestAppFactory();
        var client = Client(factory);
        foreach (var kind in new[] { "sh", "ps1" })
        {
            var res = await client.GetAsync($"/trust/trust-tesria.{kind}?address={Uri.EscapeDataString(address)}");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task An_ip_address_is_allowed_because_the_page_explains_the_catch()
    {
        using var factory = new TestAppFactory();
        var text = await Client(factory).GetStringAsync("/trust/trust-tesria.sh?address=192.0.2.10");
        Assert.Contains("TESRIA_ADDRESS=\"192.0.2.10\"", text);
    }

    [Fact]
    public async Task A_server_with_a_public_certificate_has_nothing_to_set_up()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?> { ["Tls:Caddyfile"] = "deploy/Caddyfile.public" });
        var html = await Client(factory).GetStringAsync("/trust");
        Assert.Contains("Nothing to set up", html);
        Assert.DoesNotContain("trust-tesria.sh", html);

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
    public void Both_scripts_in_deploy_still_have_the_line_the_page_fills_in()
    {
        // The endpoints refuse to serve a template that lost its marker; this
        // catches it at test time instead of on someone's first download.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/scripts"));
        Assert.Contains(Tesria.Api.Features.Trust.TrustEndpoints.ShMarker, File.ReadAllText(Path.Combine(root, "trust-ca.sh")));
        Assert.Contains(Tesria.Api.Features.Trust.TrustEndpoints.Ps1Marker, File.ReadAllText(Path.Combine(root, "trust-ca.ps1")));
    }
}
