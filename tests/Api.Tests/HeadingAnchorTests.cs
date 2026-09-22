using System.Text.Json;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Pins the heading-id rule shared with <c>headingAnchors.ts</c>. The
/// expected values here are what the editor produces for the same input;
/// if one side changes, links saved as <c>#slug</c> stop resolving on the
/// other.
/// </summary>
public class HeadingAnchorTests
{
    [Theory]
    [InlineData("Setup", "setup")]
    [InlineData("  Getting Started!  ", "getting-started")]
    // The dash is written as an escape, not typed: the character is what
    // this case exists to exercise, and the repo keeps none in prose.
    [InlineData("C# & .NET 10 \u2014 notes", "c-net-10-notes")]
    [InlineData("Überblick über Größen", "überblick-über-größen")]
    [InlineData("日本語 の 見出し", "日本語-の-見出し")]
    [InlineData("---", "heading")]
    [InlineData("", "heading")]
    [InlineData("v2.0 (beta)", "v2-0-beta")]
    public void Slugifies_like_the_editor(string text, string expected) =>
        Assert.Equal(expected, HeadingAnchors.Slugify(text));

    [Fact]
    public void Deduplicates_in_document_order_with_numeric_suffixes()
    {
        var doc = JsonDocument.Parse("""
        {"type":"doc","content":[
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Setup"}]},
          {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Setup"}]},
          {"type":"panel","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"setup"}]}]},
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Setup-2"}]}
        ]}
        """).RootElement;

        var anchors = HeadingAnchors.Collect(doc);
        Assert.Equal(["setup", "setup-2", "setup-3", "setup-2-2"], anchors.Select(a => a.Id));
        Assert.Equal([2, 3, 2, 2], anchors.Select(a => a.Level));
    }
}
