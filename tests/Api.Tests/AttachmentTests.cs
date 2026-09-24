using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Tesria.Api.Tests;

public class AttachmentTests
{
    private record PageDetail(Guid Id, Guid SpaceId, Guid? ParentPageId, string Title);
    private record AttachmentResponse(
        Guid Id, Guid PageId, string Filename, string ContentType, long Size,
        Guid UploadedById, DateTimeOffset CreatedAt);

    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<(TestAppFactory, HttpClient, Guid pageId)> NewClientWithPage()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "P", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        return (factory, client, page!.Id);
    }

    private static MultipartFormDataContent FileContent(string name, string text, string contentType)
    {
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        bytes.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { bytes, "file", name } };
    }

    [Fact]
    public async Task Upload_then_list_and_download_roundtrips_content()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;

        var upload = await client.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("notes.txt", "hello world", "text/plain"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var meta = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.Equal("notes.txt", meta!.Filename);
        Assert.Equal(11, meta.Size);

        var list = await client.GetFromJsonAsync<List<AttachmentResponse>>($"/api/pages/{pageId}/attachments");
        Assert.Single(list!);

        var downloaded = await client.GetStringAsync($"/api/attachments/{meta.Id}/download");
        Assert.Equal("hello world", downloaded);
    }

    [Fact]
    public async Task A_PDF_can_be_viewed_inline_and_framed_by_this_site_only()
    {
        // The file block frames a PDF; pointed at the download, which says
        // "attachment" and may not be framed, it downloaded the file instead.
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;
        var pdf = await (await client.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("Plan.pdf", "%PDF-1.7\n%fake\n", "application/pdf"))).Content.ReadFromJsonAsync<AttachmentResponse>();

        var view = await client.GetAsync($"/api/attachments/{pdf!.Id}/view");
        view.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", view.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", view.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("SAMEORIGIN", view.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("frame-ancestors 'self'", view.Headers.GetValues("Content-Security-Policy").Single());

        // The download is unchanged: a download, not framable.
        var download = await client.GetAsync($"/api/attachments/{pdf.Id}/download");
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("DENY", download.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Only_a_PDF_is_viewed_inline()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;
        var text = await (await client.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("notes.txt", "hello", "text/plain"))).Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attachments/{text!.Id}/view")).StatusCode);

        // Nor to someone who may not see the page.
        var pdf = await (await client.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("Plan.pdf", "%PDF-1.7\n", "application/pdf"))).Content.ReadFromJsonAsync<AttachmentResponse>();
        var stranger = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/attachments/{pdf!.Id}/view")).StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_empty_file()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;

        var res = await client.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("empty.txt", "", "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_attachment()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;
        var meta = await (await client.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("a.txt", "data", "text/plain"))).Content.ReadFromJsonAsync<AttachmentResponse>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/attachments/{meta!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attachments/{meta.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<AttachmentResponse>>($"/api/pages/{pageId}/attachments"))!);
    }

    [Fact]
    public async Task Upload_requires_authentication()
    {
        var (factory, client, pageId) = await NewClientWithPage();
        using var _ = factory;
        var anon = factory.CreateClient();

        var res = await anon.PostAsync($"/api/pages/{pageId}/attachments",
            FileContent("x.txt", "data", "text/plain"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
