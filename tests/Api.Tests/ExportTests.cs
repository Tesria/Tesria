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
    public void Renders_html_structure_and_marks()
    {
        var html = ProseMirrorRenderer.ToHtml(Rich);
        Assert.Contains("<h2 id=\"setup\">Setup</h2>", html);
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

    private const string TableWithWidthDoc = """
    {"type":"doc","content":[
      {"type":"table","attrs":{"width":420},"content":[
        {"type":"tableRow","content":[
          {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Name"}]}]}]}
      ]}
    ]}
    """;

    private const string TableFullWidthDoc = """
    {"type":"doc","content":[
      {"type":"table","attrs":{"layout":"full-width"},"content":[
        {"type":"tableRow","content":[
          {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Name"}]}]}]}
      ]}
    ]}
    """;

    [Fact]
    public void Renders_manually_sized_table_width_as_an_inline_style_capped_to_the_viewport()
    {
        var html = ProseMirrorRenderer.ToHtml(TableWithWidthDoc);
        Assert.Contains("<table style=\"width: min(420px, 100%)\">", html);
    }

    [Fact]
    public void Renders_full_width_table_layout_as_an_inline_style()
    {
        var html = ProseMirrorRenderer.ToHtml(TableFullWidthDoc);
        Assert.Contains("<table style=\"width: 100%\">", html);
    }

    [Fact]
    public void Table_width_and_layout_do_not_affect_markdown_export()
    {
        // GFM pipe tables have no width concept — degrades silently, same as
        // any other display-only attribute (e.g. image border/shadow).
        var md = ProseMirrorRenderer.ToMarkdown(TableWithWidthDoc);
        Assert.Contains("| Name |", md);
    }

    private const string PanelDoc = """
    {"type":"doc","content":[
      {"type":"panel","attrs":{"panelType":"warning"},"content":[
        {"type":"paragraph","content":[{"type":"text","text":"Mind the gap"}]}]}
    ]}
    """;

    private const string CellBackgroundDoc = """
    {"type":"doc","content":[
      {"type":"table","content":[
        {"type":"tableRow","content":[
          {"type":"tableHeader","attrs":{"backgroundColor":"#deebff"},"content":[
            {"type":"paragraph","content":[{"type":"text","text":"Name"}]}]},
          {"type":"tableCell","attrs":{"backgroundColor":"#ffbdad"},"content":[
            {"type":"paragraph","content":[{"type":"text","text":"Value"}]}]}]}
      ]}
    ]}
    """;

    private const string HostileColorDoc = """
    {"type":"doc","content":[
      {"type":"table","content":[
        {"type":"tableRow","content":[
          {"type":"tableCell","attrs":{"backgroundColor":"red;} body { display: none } td {"},"content":[
            {"type":"paragraph","content":[{"type":"text","text":"x"}]}]}]}
      ]}
    ]}
    """;

    [Fact]
    public void Renders_a_panel_with_its_type_label_and_inlined_colours()
    {
        var html = ProseMirrorRenderer.ToHtml(PanelDoc);
        Assert.Contains("data-panel-type=\"warning\"", html);
        Assert.Contains("<strong>Warning</strong>", html);
        // Colours are inlined, not left to a stylesheet — an exported file is
        // opened standalone, with none of the app's CSS.
        Assert.Contains("background: #fff7d6", html);
        Assert.Contains("Mind the gap", html);
    }

    [Fact]
    public void Renders_a_panel_as_a_labelled_blockquote_in_markdown()
    {
        var md = ProseMirrorRenderer.ToMarkdown(PanelDoc);
        Assert.Contains("> **Warning**", md);
        Assert.Contains("> Mind the gap", md);
    }

    private const string UnknownPanelDoc = """
    {"type":"doc","content":[
      {"type":"panel","attrs":{"panelType":"nonsense"},"content":[
        {"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}
    ]}
    """;

    [Fact]
    public void Renders_an_unknown_panel_type_as_info_rather_than_failing()
    {
        var html = ProseMirrorRenderer.ToHtml(UnknownPanelDoc);
        Assert.Contains("data-panel-type=\"info\"", html);
        Assert.Contains("hi", html);
    }

    [Fact]
    public void Renders_table_cell_background_colours_on_both_cell_kinds()
    {
        var html = ProseMirrorRenderer.ToHtml(CellBackgroundDoc);
        Assert.Contains("<th style=\"background-color: #deebff\">", html);
        Assert.Contains("<td style=\"background-color: #ffbdad\">", html);
    }

    private const string ColoredHighlightDoc = """
    {"type":"doc","content":[
      {"type":"paragraph","content":[
        {"type":"text","marks":[{"type":"highlight","attrs":{"color":"#fff0b3"}}],"text":"lit"}]}
    ]}
    """;

    private const string LegacyHighlightDoc = """
    {"type":"doc","content":[
      {"type":"paragraph","content":[
        {"type":"text","marks":[{"type":"highlight"}],"text":"lit"}]}
    ]}
    """;

    [Fact]
    public void Renders_a_highlight_colour_when_the_mark_carries_one()
    {
        Assert.Contains(
            "<mark style=\"background-color: #fff0b3\">lit</mark>",
            ProseMirrorRenderer.ToHtml(ColoredHighlightDoc));

        // Highlights stored before the palette existed have no colour attr and
        // must still render as a plain <mark>, not a broken style.
        Assert.Contains("<mark>lit</mark>", ProseMirrorRenderer.ToHtml(LegacyHighlightDoc));
    }

    [Fact]
    public void Drops_a_colour_attribute_that_is_not_a_plain_hex_value()
    {
        // Document JSON is stored as-is, so a colour reaching a `style`
        // attribute unvalidated would be CSS injection into exported HTML.
        var html = ProseMirrorRenderer.ToHtml(HostileColorDoc);
        Assert.DoesNotContain("display: none", html);
        Assert.Contains("<td>", html);
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

    private const string FormattingDoc = """
    {"type":"doc","content":[
      {"type":"paragraph","attrs":{"textAlign":"center"},"content":[
        {"type":"text","marks":[{"type":"underline"}],"text":"underlined"},
        {"type":"text","text":" and "},
        {"type":"text","marks":[{"type":"highlight"}],"text":"highlighted"}
      ]}
    ]}
    """;

    [Fact]
    public void Renders_underline_and_highlight_and_text_align_as_html()
    {
        var html = ProseMirrorRenderer.ToHtml(FormattingDoc);
        Assert.Contains("<p style=\"text-align: center\">", html);
        Assert.Contains("<u>underlined</u>", html);
        Assert.Contains("<mark>highlighted</mark>", html);
    }

    [Fact]
    public void Renders_highlight_as_raw_mark_in_markdown_and_drops_text_align()
    {
        var md = ProseMirrorRenderer.ToMarkdown(FormattingDoc);
        Assert.Contains("<mark>highlighted</mark>", md);
        Assert.DoesNotContain("text-align", md); // no Markdown alignment concept — intentionally dropped
    }
    // -- Phase 7 Wave B formatting -------------------------------------------

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

    [Fact]
    public void Renders_text_colour_scripts_and_indent_as_html()
    {
        var html = ProseMirrorRenderer.ToHtml(InkAndIndentDoc);
        Assert.Contains("<span style=\"color: #bf2600\">warning</span>", html);
        Assert.Contains("<sub>2</sub>", html);
        Assert.Contains("<sup>2</sup>", html);
        Assert.Contains("<p style=\"text-align: center; margin-left: 3.5rem\">", html);
    }

    [Fact]
    public void Clamps_indent_and_falls_back_on_an_unknown_text_colour()
    {
        var html = ProseMirrorRenderer.ToHtml(InkAndIndentDoc);
        // 9 levels is clamped to the editor's own maximum of 4 (4 x 1.75rem).
        Assert.Contains("margin-left: 7rem", html);
        Assert.DoesNotContain("15.75rem", html);
        // The mark stores a colour *name*, so nothing from the document ever
        // reaches the style attribute — an unknown name renders as grey.
        Assert.DoesNotContain("chartreuse", html);
        Assert.DoesNotContain("url(x)", html);
        Assert.DoesNotContain("position: fixed", html);
        Assert.Contains("<span style=\"color: #42526e\">hostile</span>", html);
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

    // -- Phase 7 Wave A structural blocks ------------------------------------

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
    public void Renders_heading_ids_and_a_nested_table_of_contents_in_html()
    {
        var html = ProseMirrorRenderer.ToHtml(StructuralDoc);
        Assert.Contains("<h1 id=\"plan\">Plan</h1>", html);
        Assert.Contains("<h3 id=\"step-one\">", html);
        Assert.Contains("<h2 id=\"plan-2\">", html);
        Assert.Contains("<nav data-type=\"table-of-contents\">", html);
        // Both the H3 and the H2 that follows it are sub-sections of the H1,
        // so both nest under it — the same tree TocView.tsx builds.
        Assert.Contains(
            "<li><a href=\"#plan\">Plan</a><ul>\n"
            + "<li><a href=\"#step-one\">Step one</a></li>\n"
            + "<li><a href=\"#plan-2\">Plan</a></li>\n"
            + "</ul>\n</li>", html);
        Assert.Contains("<a href=\"#step-one\" rel=\"noreferrer\">jump</a>", html);
    }

    [Fact]
    public void Renders_status_date_expand_decision_and_layout_in_html()
    {
        var html = ProseMirrorRenderer.ToHtml(StructuralDoc);
        Assert.Contains("data-status=\"yellow\"", html);
        Assert.Contains("background: #fff0b3", html);
        Assert.Contains(">In progress</span>", html);
        Assert.Contains("<time datetime=\"2026-09-10\">10 Sep 2026</time>", html);
        Assert.Contains("<details open><summary>Details &lt;b&gt;</summary>", html);
        Assert.Contains("<strong>Decision</strong>", html);
        Assert.Contains("data-type=\"layout-section\" data-width=\"wide\"", html);
        Assert.Contains("flex: 33.33 1 0%", html);
        Assert.Contains("flex: 66.67 1 0%", html);
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

    private const string HostileStructuralDoc = """
    {"type":"doc","content":[{"type":"paragraph","content":[
      {"type":"status","attrs":{"text":"<img src=x onerror=alert(1)>","color":"red; background: url(evil)"}},
      {"type":"date","attrs":{"date":"2026-13-45\" onclick=\"x"}}]},
      {"type":"layoutSection","content":[
        {"type":"layoutColumn","attrs":{"width":"1; color: red"},"content":[{"type":"paragraph"}]},
        {"type":"layoutColumn","attrs":{"width":500},"content":[{"type":"paragraph"}]}]}
    ]}
    """;

    [Fact]
    public void Structural_block_attributes_never_reach_markup_or_styles_unfiltered()
    {
        var html = ProseMirrorRenderer.ToHtml(HostileStructuralDoc);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
        Assert.DoesNotContain("url(evil)", html);
        Assert.Contains("data-status=\"grey\"", html);
        Assert.DoesNotContain("<time", html);
        Assert.DoesNotContain("onclick=\"x", html);
        Assert.DoesNotContain("color: red", html);
        Assert.Contains("flex: 1 1 0%", html);
        Assert.DoesNotContain("flex: 500", html);
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
    public async Task Export_of_a_page_anonymous_cannot_see_is_masked()
    {
        var (factory, client, page) = await NewClientWithPage();
        using var _ = factory;
        var anon = factory.CreateClient();

        // Export is open to anonymous readers of public pages (dev-plan 5.2);
        // for anything else it is 404, never 401 — the masking rule.
        var res = await anon.GetAsync($"/api/pages/{page.Id}/export?format=markdown");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
