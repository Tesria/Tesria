using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Table of contents options (Confluence Cloud's macro parameters).
///
/// These used to be asserted against the HTML export. Since dev-plan 12.1
/// there is no HTML export to assert against: HTML and PDF are captured from
/// the page, where `tocOptions.ts` applies the same rules, and Markdown is
/// the format still rendered here. What these pin is therefore the option
/// *semantics* (which headings are listed, in what order, and how they are
/// numbered) in the one renderer that still has an opinion about them.
///
/// The bullet *styling* (disc, circle, square, and the cycle by depth) is no
/// longer expressed in any server-side renderer: Markdown has no bullet
/// shapes, and the exported page gets them from the stylesheet. It is
/// covered by the fidelity capture rather than here
/// (`docs/export-fidelity.md`).
///
/// The same is true of `indent`, `cssClass` and `excludeInPdf`, which used
/// to be sanitized here against hostile attribute values. They are sanitized
/// in `tocOptions.ts` instead (`LENGTH` and `CLASS_TOKEN`), which is the
/// right place now that the browser is the renderer, but this repo has no
/// frontend tests: if that filtering is ever loosened, nothing fails.
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

    /// <summary>The contents list alone: everything before the headings themselves.</summary>
    private static string Toc(string markdown)
    {
        var end = markdown.IndexOf("# Setup", StringComparison.Ordinal);
        return end < 0 ? markdown : markdown[..end];
    }

    private static string Render(string tocAttrs) => ProseMirrorRenderer.ToMarkdown(Doc(tocAttrs));

    [Fact]
    public void Every_heading_is_listed_by_default()
    {
        var toc = Toc(Render(""));

        Assert.Contains("[Setup](#setup)", toc);
        Assert.Contains("[Step one](#step-one)", toc);
        Assert.Contains("[Detail](#detail)", toc);
        Assert.Contains("[Appendix A](#appendix-a)", toc);
    }

    [Fact]
    public void Heading_levels_limit_what_is_listed()
    {
        var toc = Toc(Render("\"minLevel\":2,\"maxLevel\":2"));

        Assert.Contains("Step one", toc);
        Assert.Contains("Step two", toc);
        Assert.DoesNotContain("Setup", toc);
        Assert.DoesNotContain("Detail", toc);
    }

    [Fact]
    public void Include_and_exclude_are_case_sensitive_wildcards_separated_by_pipes()
    {
        var included = Toc(Render("\"include\":\"Step*|Setup\""));
        Assert.Contains("Setup", included);
        Assert.Contains("Step one", included);
        Assert.DoesNotContain("Appendix", included);
        Assert.DoesNotContain("Detail", included);

        var excluded = Toc(Render("\"exclude\":\"Appendix*\""));
        Assert.DoesNotContain("Appendix", excluded);
        Assert.Contains("Detail", excluded);

        // Case sensitive: "step*" matches nothing, so nothing is listed at all.
        Assert.DoesNotContain("(#step-one)", Render("\"include\":\"step*\""));
    }

    [Fact]
    public void Section_numbers_follow_the_outline()
    {
        var toc = Toc(Render("\"sectionNumbers\":true"));

        Assert.Contains("[1 Setup](#setup)", toc);
        Assert.Contains("[1.1 Step one](#step-one)", toc);
        Assert.Contains("[1.2 Step two](#step-two)", toc);
        Assert.Contains("[1.2.1 Detail](#detail)", toc);
        Assert.Contains("[2 Appendix A](#appendix-a)", toc);
    }

    [Fact]
    public void Section_numbers_replace_numbered_bullets_rather_than_doubling_them()
    {
        // Numbering the text and the marker would read "1. 1 Setup".
        var toc = Toc(Render("\"sectionNumbers\":true,\"bulletStyle\":\"numbered\""));

        Assert.Contains("- [1 Setup](#setup)", toc);
        Assert.DoesNotContain("1. [1 Setup]", toc);
    }

    [Fact]
    public void Numbered_bullets_without_section_numbers_are_an_ordered_list()
    {
        var toc = Toc(Render("\"bulletStyle\":\"numbered\""));

        Assert.Contains("1. [Setup](#setup)", toc);
        Assert.Contains("2. [Appendix A](#appendix-a)", toc);
    }

    [Fact]
    public void Horizontal_list_is_one_line_of_links_in_document_order()
    {
        var md = Render("\"display\":\"horizontal\"");

        Assert.Contains(
            "[Setup](#setup) | [Step one](#step-one) | [Step two](#step-two) | [Detail](#detail) | [Appendix A](#appendix-a)",
            md);
    }

    [Fact]
    public void A_contents_list_that_matches_nothing_renders_nothing()
    {
        var md = Render("\"include\":\"Nothing matches this\"");

        // Not an empty list and not a heading with no items under it: absent.
        Assert.DoesNotContain("(#", Toc(md));
    }
}
