using System.Net;
using System.Net.Http.Json;
using ConfluenceClone.Api.Features.Export;
using Xunit;

namespace ConfluenceClone.Api.Tests;

public class ProseMirrorRendererTests
{
    private const string Rich = """
    {"type":"doc","content":[
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Setup"}]},
      {"type":"paragraph","content":[
        {"type":"text","text":"Run "},
        {"type":"text","marks":[{"type":"code"}],"text":"npm ci"},
        {"type":"text","text":" then "},
        {"type":"text","marks":[{"type":"bold"}],"text":"build"},
        {"type":"text","text":"."}]},
      {"type":"bulletList","content":[
        {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"first"}]}]},
        {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"second"}]}]}]},
      {"type":"codeBlock","attrs":{"language":"bash"},"content":[{"type":"text","text":"echo hi"}]}
    ]}
    """;

    [Fact]
    public void Renders_html_structure_and_marks()
    {
        var html = ProseMirrorRenderer.ToHtml(Rich);
        Assert.Contains("<h2>Setup</h2>", html);
        Assert.Contains("<code>npm ci</code>", html);
        Assert.Contains("<strong>build</strong>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("language-bash", html);
        Assert.Contains("echo hi", html);
    }

    [Fact]
    public void Renders_markdown_structure_and_marks()
    {
        var md = ProseMirrorRenderer.ToMarkdown(Rich);
        Assert.Contains("## Setup", md);
        Assert.Contains("`npm ci`", md);
        Assert.Contains("**build**", md);
        Assert.Contains("- first", md);
        Assert.Contains("- second", md);
        Assert.Contains("```bash", md);
    }

    [Fact]
    public void Escapes_html_so_exported_content_cannot_inject_markup()
    {
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","text":"<script>alert('x')</script>"}]}]}
        """;
        var html = ProseMirrorRenderer.ToHtml(doc);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Malformed_or_empty_content_renders_empty_rather_than_throwing()
    {
        Assert.Equal(string.Empty, ProseMirrorRenderer.ToHtml("{not json"));
        Assert.Equal(string.Empty, ProseMirrorRenderer.ToHtml(""));
    }

    private const string TableDoc = """
    {"type":"doc","content":[
      {"type":"table","content":[
        {"type":"tableRow","content":[
          {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Name"}]}]},
          {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Qty"}]}]}]},
        {"type":"tableRow","content":[
          {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Widget | Pro"}]}]},
          {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"3"}]}]}]}
      ]}
    ]}
    """;

    [Fact]
    public void Renders_table_as_html()
    {
        var html = ProseMirrorRenderer.ToHtml(TableDoc);
        Assert.Contains("<table>", html);
        Assert.Contains("<th><p>Name</p>\n</th>", html);
        Assert.Contains("<td><p>Widget | Pro</p>\n</td>", html);
    }

    [Fact]
    public void Renders_table_as_a_pipe_escaped_markdown_table()
    {
        var md = ProseMirrorRenderer.ToMarkdown(TableDoc);
        Assert.Contains("| Name | Qty |", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("Widget \\| Pro", md); // literal pipe in cell content is escaped
    }

    private const string TaskListDoc = """
    {"type":"doc","content":[
      {"type":"taskList","content":[
        {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"Done thing"}]}]},
        {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"Todo thing"}]}]}
      ]}
    ]}
    """;

    [Fact]
    public void Renders_task_list_as_html_with_disabled_checkboxes()
    {
        var html = ProseMirrorRenderer.ToHtml(TaskListDoc);
        Assert.Contains("data-type=\"taskList\"", html);
        Assert.Contains("<input type=\"checkbox\" disabled checked />", html);
        Assert.Contains("<input type=\"checkbox\" disabled />", html);
        Assert.Contains("Done thing", html);
    }

    [Fact]
    public void Renders_task_list_as_gfm_markdown_checkboxes()
    {
        var md = ProseMirrorRenderer.ToMarkdown(TaskListDoc);
        Assert.Contains("- [x] Done thing", md);
        Assert.Contains("- [ ] Todo thing", md);
    }

    private const string ImageDoc = """
    {"type":"doc","content":[
      {"type":"image","attrs":{"src":"/api/attachments/abc/download","alt":"a diagram","title":null}}
    ]}
    """;

    [Fact]
    public void Renders_image_as_html_img_tag()
    {
        var html = ProseMirrorRenderer.ToHtml(ImageDoc);
        Assert.Contains("<img src=\"/api/attachments/abc/download\" alt=\"a diagram\" />", html);
    }

    [Fact]
    public void Renders_image_as_markdown()
    {
        var md = ProseMirrorRenderer.ToMarkdown(ImageDoc);
        Assert.Contains("![a diagram](/api/attachments/abc/download)", md);
    }

    [Fact]
    public void An_image_node_does_not_silently_vanish_without_the_image_case()
    {
        // Regression guard for the exact bug class Phase 4 exists to close: a
        // leaf node with no children previously rendered nothing at all.
        var html = ProseMirrorRenderer.ToHtml(ImageDoc);
        Assert.NotEmpty(html);
    }
}

public class ExportEndpointTests
{
    private record PageDetail(Guid Id, Guid SpaceId, string Title);

    private const string Doc = """
    {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hello export"}]}]}
    """;

    private static async Task<(TestAppFactory, HttpClient, PageDetail)> NewClientWithPage()
    {
        var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Export Me", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDetail>();
        return (factory, client, page!);
    }

    [Fact]
    public async Task Exports_markdown_with_title_and_content()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=markdown");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/markdown", res.Content.Headers.ContentType?.MediaType);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("# Export Me", body);
        Assert.Contains("hello export", body);
    }

    [Fact]
    public async Task Exports_standalone_html()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=html");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("<!doctype html>", body);
        Assert.Contains("<h1>Export Me</h1>", body);
        Assert.Contains("hello export", body);
    }

    [Fact]
    public async Task Unknown_format_is_rejected()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=pdf");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Export_requires_authentication()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;
        var anon = factory.CreateClient();

        var res = await anon.GetAsync($"/api/pages/{page.Id}/export?format=markdown");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
