using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// PDF and single-file HTML export (dev-plan 8.1, rebuilt by 12.1).
///
/// Both are now captured from the page's own render route by the sidecar, so
/// what can be asserted in-process is the contract around that: which formats
/// exist, and what happens on an instance with no renderer. The fidelity of
/// the capture itself is the fixture and the matrix in
/// docs/export-fidelity.md, because it takes a browser to have an opinion
/// about it.
/// </summary>
public class PdfExportTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title);
    private record AttachmentDto(Guid Id, Guid PageId, string Filename, string ContentType, long Size);

    private const string Plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"body"}]}]}""";

    /// <summary>A 1x1 PNG — the smallest thing that is unambiguously an image.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<PageDetail> NewPage(HttpClient c, Guid spaceId, string content) =>
        (await (await c.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Exportable", ContentJson = content }))
            .Content.ReadFromJsonAsync<PageDetail>())!;

    [Fact]
    public async Task Pdf_is_offered_but_answers_503_with_advice_when_no_renderer_is_configured()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var page = await NewPage(client, await client.CreateSpaceAsync(), Plain);

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=pdf");
        // Not a 400 (the format is real) and not a 500 (nothing is broken):
        // the instance simply has no renderer, and the user is told what to
        // do instead rather than left at a dead end.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("Markdown", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_format_still_names_the_ones_that_exist()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var page = await NewPage(client, await client.CreateSpaceAsync(), Plain);

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=doc");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("pdf", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Html_needs_the_renderer_too_and_says_so()
    {
        // HTML used to be built in-process; since 12.1 it is a capture of the
        // real page, so an instance with no sidecar cannot produce one. It
        // says which format it can still produce rather than failing blankly.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var page = await NewPage(client, await client.CreateSpaceAsync(), Plain);

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=html");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("Markdown", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Markdown_keeps_its_attachment_urls()
    {
        // The half of the old inlining test that still means something: a
        // data: URI is unreadable in a text file, so Markdown keeps the URL.
        // The HTML file's images being carried inside it is the sidecar's
        // job now, and is checked in the fidelity walk.
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await NewPage(client, spaceId, Plain);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "dot.png");
        var attachment = await (await client.PostAsync($"/api/pages/{page.Id}/attachments", form))
            .Content.ReadFromJsonAsync<AttachmentDto>();

        var withImage = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"image\",\"attrs\":"
            + "{\"src\":\"/api/attachments/" + attachment!.Id + "/download\",\"alt\":\"dot\"}}]}]}";
        (await client.PutAsJsonAsync($"/api/pages/{page.Id}", new { ContentJson = withImage })).EnsureSuccessStatusCode();

        var md = await (await client.GetAsync($"/api/pages/{page.Id}/export?format=markdown")).Content.ReadAsStringAsync();

        Assert.Contains($"/api/attachments/{attachment.Id}/download", md);
        Assert.DoesNotContain("data:image", md);
    }
}
