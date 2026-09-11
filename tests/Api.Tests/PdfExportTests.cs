using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// PDF export (dev-plan 8.1) and the image inlining that makes an export a
/// file you can actually keep.
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
        Assert.Contains("print it to PDF", await res.Content.ReadAsStringAsync());
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
    public async Task Html_export_inlines_attached_images_so_the_file_works_on_its_own()
    {
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

        var html = await (await client.GetAsync($"/api/pages/{page.Id}/export?format=html")).Content.ReadAsStringAsync();
        Assert.Contains("src=\"data:image/png;base64,", html);
        // The authenticated URL is gone — that is the point.
        Assert.DoesNotContain($"/api/attachments/{attachment.Id}/download", html);

        // Markdown keeps the URL: a data: URI is unreadable in a text file.
        var md = await (await client.GetAsync($"/api/pages/{page.Id}/export?format=markdown")).Content.ReadAsStringAsync();
        Assert.Contains($"/api/attachments/{attachment.Id}/download", md);
        Assert.DoesNotContain("data:image", md);
    }

    [Fact]
    public async Task An_image_that_is_not_an_attachment_is_left_alone()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"image","attrs":{"src":"https://example.com/logo.png"}}]}]}""";
        var page = await NewPage(client, await client.CreateSpaceAsync(), doc);

        var html = await (await client.GetAsync($"/api/pages/{page.Id}/export?format=html")).Content.ReadAsStringAsync();
        Assert.Contains("https://example.com/logo.png", html);
    }
}
