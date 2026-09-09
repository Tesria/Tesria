using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Egress and input hardening (dev-plan 3.4): the SSRF guard at save and at
/// connect, attachment content types, and the CSRF header.
/// </summary>
public class EgressAndInputTests
{
    private record TokenDto(Guid Id, string Token);
    private record AttachmentDto(Guid Id, string ContentType, string Filename);
    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task RegisterAsync(HttpClient client, string email) =>
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .EnsureSuccessStatusCode();

    private static EgressGuard Guard(string? allowed = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Egress:AllowedNetworks"] = allowed }).Build());

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://[::1]/hook")]
    [InlineData("http://10.1.2.3/hook")]
    [InlineData("http://172.16.0.5:8080/hook")]
    [InlineData("http://192.168.1.1/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost:5432/")]
    [InlineData("http://db.internal/")]
    [InlineData("http://printer.local/")]
    [InlineData("ftp://203.0.113.1/")]
    [InlineData("http://user:pass@203.0.113.1/")]
    [InlineData("http://0.0.0.0/")]
    public async Task Private_local_and_odd_targets_are_refused(string url)
    {
        Assert.NotNull(await Guard().ValidateAsync(url));
    }

    [Fact]
    public async Task A_public_address_is_allowed_and_an_operator_can_open_a_private_range()
    {
        Assert.Null(await Guard().ValidateAsync("https://203.0.113.10/hook"));
        Assert.NotNull(await Guard().ValidateAsync("http://192.168.1.20/hook"));
        // A LAN automation server, deliberately allowed.
        Assert.Null(await Guard("192.168.1.0/24").ValidateAsync("http://192.168.1.20/hook"));
        Assert.NotNull(await Guard("192.168.1.0/24").ValidateAsync("http://192.168.2.20/hook"));
    }

    [Fact]
    public async Task The_connect_itself_refuses_a_private_address()
    {
        // Validation can be passed by a name that resolves publicly now and
        // privately later. The handler checks the address it is about to dial.
        using var client = new HttpClient(Guard().CreateHandler());
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1:9/"));
        Assert.IsType<EgressBlockedException>(ex.InnerException);
    }

    [Fact]
    public async Task Creating_a_webhook_to_a_private_target_is_refused_and_recorded()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        await admin.CreateSpaceAsync("HOOK");

        var res = await admin.PostAsJsonAsync("/api/spaces/HOOK/webhooks",
            new { Url = "http://169.254.169.254/latest/meta-data/", Events = "*" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // Nothing saved, but the attempt is on the security page.
        var events = await admin.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/admin/security/events");
        Assert.Contains(events!, e => e["kind"].ToString() == "webhook.private_target");

        (await admin.PostAsJsonAsync("/api/spaces/HOOK/webhooks",
            new { Url = "https://203.0.113.10/hook", Events = "*" })).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("text/html", "<html><script>alert(1)</script></html>", "application/octet-stream")]
    [InlineData("image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg'><script>1</script></svg>", "application/octet-stream")]
    [InlineData("text/plain", "<!DOCTYPE html><html>", "application/octet-stream")]
    [InlineData("text/plain", "just some notes", "text/plain")]
    [InlineData("application/pdf", "%PDF-1.7 ...", "application/pdf")]
    [InlineData("text/html", "%PDF-1.7 ...", "application/pdf")]
    public void Scriptable_types_are_served_opaque_and_bytes_beat_labels(string declared, string body, string expected)
    {
        Assert.Equal(expected, ContentTypes.Resolve(System.Text.Encoding.ASCII.GetBytes(body), declared));
    }

    [Fact]
    public async Task An_uploaded_html_file_downloads_as_an_opaque_attachment()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var spaceId = await admin.CreateSpaceAsync();
        var page = await (await admin.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "P", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var pageId = page!["id"].ToString();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("<html><script>document.cookie</script></html>"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(file, "file", "evil.html");

        var uploaded = await (await admin.PostAsync($"/api/pages/{pageId}/attachments", form))
            .Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.Equal("application/octet-stream", uploaded!.ContentType);

        var download = await admin.GetAsync($"/api/attachments/{uploaded.Id}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_cookie_session_must_send_the_csrf_header_on_state_changes()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        var forged = factory.CreateClient();
        await RegisterAsync(forged, "user@example.com"); // signs in with the header present
        forged.DefaultRequestHeaders.Remove("X-Requested-With");

        // Reads are fine; a state change without the marker is not.
        (await forged.GetAsync("/api/spaces")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync("/api/spaces",
            new { Key = "X1", Name = "Forged", Description = (string?)null })).StatusCode);

        forged.DefaultRequestHeaders.Add("X-Requested-With", "Tesria");
        (await forged.PostAsJsonAsync("/api/spaces",
            new { Key = "X1", Name = "Fine", Description = (string?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Bearer_token_callers_are_exempt_from_the_csrf_header()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var token = await (await admin.PostAsJsonAsync("/api/api-tokens", new { Name = "ci" }))
            .Content.ReadFromJsonAsync<TokenDto>();

        var script = factory.CreateClient();
        script.DefaultRequestHeaders.Remove("X-Requested-With");
        script.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);

        // No cookie, nothing to forge.
        (await script.PostAsJsonAsync("/api/spaces",
            new { Key = "T1", Name = "From script", Description = (string?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Signing_in_needs_no_header_because_there_is_no_session_yet()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "admin@example.com");

        var fresh = factory.CreateClient();
        fresh.DefaultRequestHeaders.Remove("X-Requested-With");
        (await fresh.PostAsJsonAsync("/api/auth/login",
            new { Email = "admin@example.com", Password = "supersecret" })).EnsureSuccessStatusCode();
    }
}
