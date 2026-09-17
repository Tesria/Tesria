using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Table of contents options (Confluence Cloud's macro parameters) at export.
/// The editor applies the same rules in tocOptions.ts; these pin them.
/// </summary>
public class TocOptionsTests
{
    private const string Headings = """
      {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Setup"}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Step one"}]},
      {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Step two"}]},
      {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Detail"}]},
      {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Appendix A"}]}
    """;

    private static string Doc(string tocAttrs) =>
        "{\"type\":\"doc\",\"content\":[{\"type\":\"tableOfContents\",\"attrs\":{" + tocAttrs + "}}," + Headings + "]}";

    private static string Toc(string html)
    {
        var start = html.IndexOf("<nav data-type=\"table-of-contents\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no table of contents rendered");
        return html[start..(html.IndexOf("</nav>", start, StringComparison.Ordinal) + 6)];
    }

    [Fact]
    public void No_options_renders_exactly_as_before()
    {
        var toc = Toc(ProseMirrorRenderer.ToHtml(Doc("")));
        Assert.StartsWith("<nav data-type=\"table-of-contents\">\n<ul>\n", toc);
        Assert.DoesNotContain("style=", toc);
        Assert.DoesNotContain("class=", toc);
    }

    [Fact]
    public void Heading_levels_limit_what_is_listed()
    {
        var toc = Toc(ProseMirrorRenderer.ToHtml(Doc("\"minLevel\":2,\"maxLevel\":2")));
        Assert.Contains("Step one", toc);
        Assert.Contains("Step two", toc);
        Assert.DoesNotContain("Setup", toc);
        Assert.DoesNotContain("Detail", toc);
    }

    [Fact]
    public void Include_and_exclude_are_case_sensitive_wildcards_separated_by_pipes()
    {
        var included = Toc(ProseMirrorRenderer.ToHtml(Doc("\"include\":\"Step*|Setup\"")));
        Assert.Contains("Setup", included);
        Assert.Contains("Step one", included);
        Assert.DoesNotContain("Appendix", included);
        Assert.DoesNotContain("Detail", included);

        var excluded = Toc(ProseMirrorRenderer.ToHtml(Doc("\"exclude\":\"Appendix*\"")));
        Assert.DoesNotContain("Appendix", excluded);
        Assert.Contains("Detail", excluded);

        // Case sensitive: "step*" matches nothing, so nothing is listed.
        Assert.DoesNotContain("table-of-contents", ProseMirrorRenderer.ToHtml(Doc("\"include\":\"step*\"")));
    }

    [Fact]
    public void Section_numbers_follow_the_outline()
    {
        var toc = Toc(ProseMirrorRenderer.ToHtml(Doc("\"sectionNumbers\":true")));
        Assert.Contains(">1 Setup</a>", toc);
        Assert.Contains(">1.1 Step one</a>", toc);
        Assert.Contains(">1.2 Step two</a>", toc);
        Assert.Contains(">1.2.1 Detail</a>", toc);
        Assert.Contains(">2 Appendix A</a>", toc);
        Assert.Contains("list-style-type: none", toc);
    }

    [Fact]
    public void Horizontal_list_is_one_line_of_links_in_document_order()
    {
        var toc = Toc(ProseMirrorRenderer.ToHtml(Doc("\"display\":\"horizontal\"")));
        Assert.Contains("class=\"toc--horizontal\"", toc);
        Assert.DoesNotContain("<ul", toc);
        Assert.True(toc.IndexOf("Setup", StringComparison.Ordinal) < toc.IndexOf("Detail", StringComparison.Ordinal));

        var md = ProseMirrorRenderer.ToMarkdown(Doc("\"display\":\"horizontal\""));
        Assert.Contains("[Setup](#setup) | [Step one](#step-one) | [Step two](#step-two) | [Detail](#detail) | [Appendix A](#appendix-a)", md);
    }

    [Theory]
    [InlineData("numbered", "list-style-type: decimal")]
    [InlineData("square", "list-style-type: square")]
    [InlineData("circle", "list-style-type: circle")]
    [InlineData("none", "list-style-type: none")]
    public void Bullet_style_sets_the_list_style(string style, string expected)
    {
        Assert.Contains(expected, Toc(ProseMirrorRenderer.ToHtml(Doc($"\"bulletStyle\":\"{style}\""))));
    }

    [Fact]
    public void Mixed_bullets_cycle_by_depth()
    {
        var toc = Toc(ProseMirrorRenderer.ToHtml(Doc("\"bulletStyle\":\"mixed\"")));
        Assert.Contains("<ul style=\"list-style-type: disc\">", toc);
        Assert.Contains("<ul style=\"list-style-type: circle\">", toc);
        Assert.Contains("<ul style=\"list-style-type: square\">", toc);
    }

    [Fact]
    public void Indent_accepts_a_css_length_and_nothing_else()
    {
        Assert.Contains("padding-left: 10px", Toc(ProseMirrorRenderer.ToHtml(Doc("\"indent\":\"10px\""))));
        var hostile = Toc(ProseMirrorRenderer.ToHtml(Doc("\"indent\":\"1px; background:url(x)\"")));
        Assert.DoesNotContain("background", hostile);
        Assert.DoesNotContain("style=", hostile);
    }

    [Fact]
    public void Css_class_keeps_only_valid_class_names()
    {
        var toc = Toc(ProseMirrorRenderer.ToHtml(Doc("\"cssClass\":\"my-toc \\\"><script>x</script> other_toc\"")));
        Assert.Contains("class=\"my-toc other_toc\"", toc);
        Assert.DoesNotContain("script", toc);
    }

    [Fact]
    public void Exclude_in_pdf_marks_the_toc_for_the_print_stylesheet()
    {
        Assert.Contains("class=\"toc--exclude-print\"", Toc(ProseMirrorRenderer.ToHtml(Doc("\"excludeInPdf\":true"))));
    }

    [Fact]
    public void Numbered_bullets_become_an_ordered_list_in_markdown()
    {
        var md = ProseMirrorRenderer.ToMarkdown(Doc("\"bulletStyle\":\"numbered\""));
        Assert.Contains("1. [Setup](#setup)", md);
        Assert.Contains("   1. [Step one](#step-one)", md);
        Assert.Contains("   2. [Step two](#step-two)", md);
    }
}
