using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

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
    public void Malformed_or_empty_content_renders_empty_rather_than_throwing()
    {
        // Content comes from stored JSON, which the API accepts as arbitrary
        // JSON: an export must not be the thing that discovers it is broken.
        Assert.Equal(string.Empty, ProseMirrorRenderer.ToMarkdown("{not json"));
        Assert.Equal(string.Empty, ProseMirrorRenderer.ToMarkdown(""));
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

    private const string TableWithWidthDoc = """
    {"type":"doc","content":[
      {"type":"table","attrs":{"width":420},"content":[
        {"type":"tableRow","content":[
          {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Name"}]}]}]}
      ]}
    ]}
    """;

    private const string PanelDoc = """
    {"type":"doc","content":[
      {"type":"panel","attrs":{"panelType":"warning"},"content":[
        {"type":"paragraph","content":[{"type":"text","text":"Mind the gap"}]}]}
    ]}
    """;

    private const string TaskListDoc = """
    {"type":"doc","content":[
      {"type":"taskList","content":[
        {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"Done thing"}]}]},
        {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"Todo thing"}]}]}
      ]}
    ]}
    """;

    private const string ImageDoc = """
    {"type":"doc","content":[
      {"type":"image","attrs":{"src":"/api/attachments/abc/download","alt":"a diagram","title":null}}
    ]}
    """;

    private const string FormattingDoc = """
    {"type":"doc","content":[
      {"type":"paragraph","attrs":{"textAlign":"center"},"content":[
        {"type":"text","marks":[{"type":"underline"}],"text":"underlined"},
        {"type":"text","text":" and "},
        {"type":"text","marks":[{"type":"highlight"}],"text":"highlighted"}
      ]}
    ]}
    """;

    private const string MentionDoc = """
    {"type":"doc","content":[
      {"type":"paragraph","content":[
        {"type":"text","text":"ask "},
        {"type":"mention","attrs":{"userId":"11111111-1111-1111-1111-111111111111","label":"Ana <b>"}},
        {"type":"text","text":" and "},
        {"type":"mention","attrs":{"userId":"22222222-2222-2222-2222-222222222222","label":"  "}}]},
      {"type":"taskList","content":[
        {"type":"taskItem","attrs":{"checked":false,"assigneeId":"11111111-1111-1111-1111-111111111111","assigneeName":"Ana"},
         "content":[{"type":"paragraph","content":[
           {"type":"mention","attrs":{"userId":"11111111-1111-1111-1111-111111111111","label":"Ana"}},
           {"type":"text","text":" to review"}]}]}]}
    ]}
    """;

    private const string InkAndIndentDoc = """
    {"type":"doc","content":[
      {"type":"paragraph","attrs":{"textIndent":2,"textAlign":"center"},"content":[
        {"type":"text","marks":[{"type":"textColor","attrs":{"color":"red"}}],"text":"warning"},
        {"type":"text","text":" H"},
        {"type":"text","marks":[{"type":"subscript"}],"text":"2"},
        {"type":"text","text":"O and x"},
        {"type":"text","marks":[{"type":"superscript"}],"text":"2"}]},
      {"type":"heading","attrs":{"level":2,"textIndent":9},"content":[{"type":"text","text":"Deep"}]},
      {"type":"paragraph","attrs":{"textIndent":"3; position: fixed"},"content":[
        {"type":"text","marks":[{"type":"textColor","attrs":{"color":"chartreuse; background: url(x)"}}],"text":"hostile"}]}
    ]}
    """;

    private const string StructuralDoc = """
    {"type":"doc","content":[
      {"type":"tableOfContents"},
      {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Plan"}]},
      {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Step one"}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Plan"}]},
      {"type":"paragraph","content":[
        {"type":"text","marks":[{"type":"link","attrs":{"href":"#step-one"}}],"text":"jump"},
        {"type":"text","text":" "},
        {"type":"status","attrs":{"text":"In progress","color":"yellow"}},
        {"type":"text","text":" due "},
        {"type":"date","attrs":{"date":"2026-09-10"}}]},
      {"type":"expand","attrs":{"title":"Details <b>"},"content":[
        {"type":"paragraph","content":[{"type":"text","text":"hidden text"}]}]},
      {"type":"decision","content":[
        {"type":"paragraph","content":[{"type":"text","text":"Ship it"}]}]},
      {"type":"layoutSection","attrs":{"width":"wide"},"content":[
        {"type":"layoutColumn","attrs":{"width":33.33},"content":[{"type":"paragraph","content":[{"type":"text","text":"left col"}]}]},
        {"type":"layoutColumn","attrs":{"width":66.67},"content":[{"type":"paragraph","content":[{"type":"text","text":"right col"}]}]}]}
    ]}
    """;

    [Fact]
    public void Renders_table_as_a_pipe_escaped_markdown_table()
    {
        var md = ProseMirrorRenderer.ToMarkdown(TableDoc);
        Assert.Contains("| Name | Qty |", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("Widget \\| Pro", md); // literal pipe in cell content is escaped
    }

    [Fact]
    public void Table_width_and_layout_do_not_affect_markdown_export()
    {
        // GFM pipe tables have no width concept: it degrades silently, same as
        // any other display-only attribute (e.g. image border/shadow). The
        // width is not lost, it is carried by the captured formats instead.
        var md = ProseMirrorRenderer.ToMarkdown(TableWithWidthDoc);
        Assert.Contains("| Name |", md);
    }

    [Fact]
    public void Renders_a_panel_as_a_labelled_blockquote_in_markdown()
    {
        var md = ProseMirrorRenderer.ToMarkdown(PanelDoc);
        Assert.Contains("> **Warning**", md);
        Assert.Contains("> Mind the gap", md);
    }

    [Fact]
    public void Renders_task_list_as_gfm_markdown_checkboxes()
    {
        var md = ProseMirrorRenderer.ToMarkdown(TaskListDoc);
        Assert.Contains("- [x] Done thing", md);
        Assert.Contains("- [ ] Todo thing", md);
    }

    [Fact]
    public void Renders_image_as_markdown()
    {
        var md = ProseMirrorRenderer.ToMarkdown(ImageDoc);
        Assert.Contains("![a diagram](/api/attachments/abc/download)", md);
    }

    [Fact]
    public void Renders_highlight_as_raw_mark_in_markdown_and_drops_text_align()
    {
        var md = ProseMirrorRenderer.ToMarkdown(FormattingDoc);
        Assert.Contains("<mark>highlighted</mark>", md);
        Assert.DoesNotContain("text-align", md); // no Markdown alignment concept, intentionally dropped
    }

    [Fact]
    public void Renders_a_mention_as_plain_at_name_in_markdown_without_repeating_the_assignee()
    {
        var md = ProseMirrorRenderer.ToMarkdown(MentionDoc);
        Assert.Contains("ask @Ana <b> and @Unknown user", md);
        // The assignee attribute is a copy of the mention already in the item.
        Assert.Contains("- [ ] @Ana to review", md);
        Assert.DoesNotContain("@Ana @Ana", md);
    }

    [Fact]
    public void Renders_text_colour_and_scripts_as_raw_html_in_markdown_and_drops_indent()
    {
        var md = ProseMirrorRenderer.ToMarkdown(InkAndIndentDoc);
        Assert.Contains("<span style=\"color: #bf2600\">warning</span>", md);
        Assert.Contains("H<sub>2</sub>O", md);
        Assert.Contains("x<sup>2</sup>", md);
        // Markdown has no block indent to carry it into.
        Assert.DoesNotContain("margin-left", md);
    }

    [Fact]
    public void Renders_structural_blocks_as_markdown_with_anchors_only_because_the_page_links_to_headings()
    {
        var md = ProseMirrorRenderer.ToMarkdown(StructuralDoc);
        Assert.Contains("- [Plan](#plan)\n  - [Step one](#step-one)\n  - [Plan](#plan-2)\n", md);
        Assert.Contains("<a id=\"step-one\"></a>\n### Step one", md);
        Assert.Contains("[jump](#step-one) `In progress` due 10 Sep 2026", md);
        Assert.Contains("**Details <b>**\n\nhidden text", md);
        Assert.Contains("> **Decision:**\n>\n> Ship it", md);
        Assert.Contains("left col\n\nright col", md);
    }

    [Fact]
    public void Markdown_headings_carry_no_anchor_when_nothing_links_to_them()
    {
        var md = ProseMirrorRenderer.ToMarkdown(Rich);
        Assert.DoesNotContain("<a id=", md);
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
    public async Task Html_is_captured_rather_than_generated_and_needs_the_renderer()
    {
        // Until 12.1 this asserted the shape of HTML built in-process. There
        // is no such HTML any more: the file is the page itself, photographed
        // by the sidecar, which is why an export finally looks like the page.
        // Without a sidecar the format is honestly unavailable.
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;

        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=html");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_format_is_rejected()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;

        // Was `pdf` until dev-plan 8.1 made that a real format; `docx` is
        // the stand-in for "a format this app does not have".
        var res = await client.GetAsync($"/api/pages/{page.Id}/export?format=docx");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Export_of_a_page_anonymous_cannot_see_is_masked()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;
        var anon = factory.CreateClient();

        // Export is open to anonymous readers of public pages (dev-plan 5.2);
        // for anything else it is 404, never 401: the masking rule.
        var res = await anon.GetAsync($"/api/pages/{page.Id}/export?format=markdown");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
