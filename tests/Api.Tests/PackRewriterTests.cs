using System.Text.Json.Nodes;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The rewrite half of a pack import (dev-plan 8.5 step 3).
///
/// These are fixture tests because the rewriter's mistakes are all quiet
/// ones: a link that points at the source instance, a mention aimed at a
/// stranger, a highlight over nothing. None of them throws, and none shows up
/// in a walk unless you happen to click the right word.
/// </summary>
public class PackRewriterTests
{
    private static readonly Guid OldPage = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NewPage = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Stranger = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OldFile = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid NewFile = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OldComment = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid NewComment = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static PackRewriter.Maps Maps() => new(
        "TARGET",
        new Dictionary<Guid, Guid> { [OldPage] = NewPage },
        new Dictionary<Guid, Guid> { [OldFile] = NewFile },
        new Dictionary<Guid, Guid> { [OldComment] = NewComment });

    /// <summary>
    /// Fixtures name ids as %Token% rather than interpolating them: JSON's
    /// closing braces and a raw string's are the same character, and the
    /// escaping that reconciles the two makes a fixture unreadable.
    /// </summary>
    private static string Fill(string json) => json
        .Replace("%OldPage%", OldPage.ToString())
        .Replace("%NewPage%", NewPage.ToString())
        .Replace("%Stranger%", Stranger.ToString())
        .Replace("%OldFile%", OldFile.ToString())
        .Replace("%NewFile%", NewFile.ToString())
        .Replace("%OldComment%", OldComment.ToString())
        .Replace("%NewComment%", NewComment.ToString());

    private static JsonNode Doc(string inner) =>
        JsonNode.Parse("{\"type\":\"doc\",\"content\":[" + Fill(inner) + "]}")!;

    private static string Rewritten(string inner) =>
        PackRewriter.Rewrite(Doc(inner), Maps()).ToJsonString();

    [Fact]
    public void Rewrites_a_link_to_a_page_in_the_pack()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"see",
             "marks":[{"type":"link","attrs":{"href":"/spaces/SRC/pages/%OldPage%"}}]}]}
            """);

        Assert.Contains($"/spaces/TARGET/pages/{NewPage}", json);
        Assert.DoesNotContain(OldPage.ToString(), json);
    }

    [Fact]
    public void Keeps_the_anchor_on_a_rewritten_link()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"see",
             "marks":[{"type":"link","attrs":{"href":"/spaces/SRC/pages/%OldPage%#setup"}}]}]}
            """);

        Assert.Contains($"/spaces/TARGET/pages/{NewPage}#setup", json);
    }

    [Fact]
    public void Leaves_a_link_to_a_page_that_is_not_in_the_pack_alone()
    {
        // The site export would write "#" here. A pack would not: re-imported
        // into the same instance this link still resolves, and a link that
        // 404s honestly beats one silently sent somewhere else.
        var href = $"/spaces/OTHER/pages/{Stranger}";
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"see",
             "marks":[{"type":"link","attrs":{"href":"/spaces/OTHER/pages/%Stranger%"}}]}]}
            """);

        Assert.Contains(href, json);
    }

    [Fact]
    public void Rewrites_an_attachment_download_link()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"file",
             "marks":[{"type":"link","attrs":{"href":"/api/attachments/%OldFile%/download"}}]}]}
            """);

        Assert.Contains($"/api/attachments/{NewFile}/download", json);
    }

    [Fact]
    public void Rewrites_an_image_src_as_well_as_an_href()
    {
        // The src is an attribute on a node, not a mark, and was the shape
        // most likely to be missed by an allow-list of link attributes.
        var json = Rewritten("""
            {"type":"image","attrs":{"src":"/api/attachments/%OldFile%/download","alt":"a"}}
            """);

        Assert.Contains($"/api/attachments/{NewFile}/download", json);
        Assert.DoesNotContain(OldFile.ToString(), json);
    }

    [Fact]
    public void Keeps_a_mention_readable_and_drops_the_user_id()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[
             {"type":"mention","attrs":{"userId":"%Stranger%","label":"Dana Okafor"}}]}
            """);

        Assert.Contains("Dana Okafor", json);
        Assert.DoesNotContain(Stranger.ToString(), json);
        Assert.Contains("\"userId\":null", json);
    }

    [Fact]
    public void Keeps_a_task_assignee_name_and_drops_the_id()
    {
        var json = Rewritten("""
            {"type":"taskItem","attrs":{"assigneeId":"%Stranger%","assigneeName":"Dana Okafor","checked":false},
             "content":[{"type":"paragraph","content":[{"type":"text","text":"ship it"}]}]}
            """);

        Assert.Contains("Dana Okafor", json);
        Assert.DoesNotContain(Stranger.ToString(), json);
        Assert.Contains("\"assigneeId\":null", json);
    }

    [Fact]
    public void Rewrites_a_comment_mark_to_the_new_comment()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"this bit",
             "marks":[{"type":"comment","attrs":{"commentId":"%OldComment%"}}]}]}
            """);

        Assert.Contains(NewComment.ToString(), json);
        Assert.DoesNotContain(OldComment.ToString(), json);
    }

    [Fact]
    public void Drops_a_comment_mark_with_nothing_behind_it_but_keeps_the_words()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"this bit",
             "marks":[{"type":"comment","attrs":{"commentId":"%Stranger%"}}]}]}
            """);

        Assert.Contains("this bit", json);
        Assert.DoesNotContain("comment", json);
        // Not left as an empty array either, so the document is identical to
        // one that never carried the mark.
        Assert.DoesNotContain("marks", json);
    }

    [Fact]
    public void Keeps_other_marks_on_text_whose_comment_mark_is_dropped()
    {
        var json = Rewritten("""
            {"type":"paragraph","content":[{"type":"text","text":"this bit",
             "marks":[{"type":"bold"},{"type":"comment","attrs":{"commentId":"%Stranger%"}}]}]}
            """);

        Assert.Contains("bold", json);
        Assert.DoesNotContain("\"comment\"", json);
    }

    [Fact]
    public void Rewrites_the_page_a_dynamic_block_includes()
    {
        var json = Rewritten("""
            {"type":"dynamicBlock","attrs":{"kind":"include-page","params":{"page":"%OldPage%"}}}
            """);

        Assert.Contains(NewPage.ToString(), json);
        Assert.DoesNotContain(OldPage.ToString(), json);
    }

    [Fact]
    public void Leaves_a_dynamic_block_pointing_outside_the_pack_alone()
    {
        var json = Rewritten("""
            {"type":"dynamicBlock","attrs":{"kind":"include-page","params":{"page":"%Stranger%"}}}
            """);

        Assert.Contains(Stranger.ToString(), json);
    }

    [Fact]
    public void Leaves_a_dynamic_blocks_other_parameters_alone()
    {
        var json = Rewritten("""
            {"type":"dynamicBlock","attrs":{"kind":"content-by-label","params":{"labels":"release","limit":"25"}}}
            """);

        Assert.Contains("release", json);
        Assert.Contains("25", json);
    }

    [Fact]
    public void Does_not_modify_the_document_it_was_given()
    {
        // A pack is read once and a page's versions are imported from the
        // same parsed nodes; a rewriter that edited in place would corrupt
        // every version after the first.
        var doc = Doc("""
            {"type":"paragraph","content":[{"type":"text","text":"see",
             "marks":[{"type":"link","attrs":{"href":"/spaces/SRC/pages/%OldPage%"}}]}]}
            """);
        var before = doc.ToJsonString();

        PackRewriter.Rewrite(doc, Maps());

        Assert.Equal(before, doc.ToJsonString());
    }

    [Fact]
    public void Rewriting_twice_changes_nothing_the_second_time()
    {
        var doc = Doc("""
            {"type":"paragraph","content":[
             {"type":"mention","attrs":{"userId":"%Stranger%","label":"Dana"}},
             {"type":"text","text":"see","marks":[{"type":"comment","attrs":{"commentId":"%OldComment%"}}]}]}
            """);

        var once = PackRewriter.Rewrite(doc, Maps());
        // The comment map is keyed by the *source* id, so a second pass finds
        // nothing to do rather than dropping the mark it just rewrote. This is
        // what makes a re-import of a pack safe.
        var twice = PackRewriter.Rewrite(once, Maps() with
        {
            Comments = new Dictionary<Guid, Guid> { [NewComment] = NewComment },
        });

        Assert.Equal(once.ToJsonString(), twice.ToJsonString());
    }

    [Fact]
    public void Rewrites_a_page_link_pasted_into_a_comment_body()
    {
        var body = PackRewriter.RewriteText(
            $"as we said in /spaces/SRC/pages/{OldPage} this is fine", Maps());

        Assert.Equal($"as we said in /spaces/TARGET/pages/{NewPage} this is fine", body);
    }

    [Fact]
    public void Leaves_a_comment_body_with_no_links_untouched()
    {
        const string body = "Looks good to me.";
        Assert.Equal(body, PackRewriter.RewriteText(body, Maps()));
    }

    [Fact]
    public void Survives_a_document_with_nothing_to_rewrite()
    {
        var json = Rewritten("""{"type":"paragraph","content":[{"type":"text","text":"plain"}]}""");
        Assert.Contains("plain", json);
    }

    [Fact]
    public void Rewrites_a_link_nested_deep_in_a_table()
    {
        var json = Rewritten("""
            {"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[
             {"type":"paragraph","content":[{"type":"text","text":"see",
              "marks":[{"type":"link","attrs":{"href":"/spaces/SRC/pages/%OldPage%"}}]}]}]}]}]}
            """);

        Assert.Contains($"/spaces/TARGET/pages/{NewPage}", json);
    }
}
