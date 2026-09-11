using Tesria.Api.Infrastructure.Mentions;
using Xunit;

namespace Tesria.Api.Tests;

public class MentionExtractionTests
{
    private const string Alice = "11111111-1111-1111-1111-111111111111";
    private const string Bob = "22222222-2222-2222-2222-222222222222";

    private static string DocMentioning(params string[] ids) =>
        $$"""
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","text":"hi "}
          {{string.Join("", ids.Select(id => $$""","{"type":"mention","attrs":{"userId":"{{id}}","label":"Someone"}}"""))}}
        ]}]}
        """;

    [Fact]
    public void Finds_mentions_at_any_depth_and_deduplicates()
    {
        var doc = $$"""
        {"type":"doc","content":[
          {"type":"paragraph","content":[{"type":"mention","attrs":{"userId":"{{Alice}}"}}]},
          {"type":"layoutSection","content":[{"type":"layoutColumn","content":[
            {"type":"panel","content":[{"type":"paragraph","content":[
              {"type":"mention","attrs":{"userId":"{{Bob}}"}},
              {"type":"mention","attrs":{"userId":"{{Alice}}"}}]}]}]}]}
        ]}
        """;
        Assert.Equal([Guid.Parse(Alice), Guid.Parse(Bob)], Mentions.UserIdsIn(doc).OrderBy(g => g.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"doc","content":[{"type":"mention","attrs":{"userId":"not-a-guid"}}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"mention"}]}""")]
    public void Malformed_input_yields_nothing_rather_than_throwing(string doc) =>
        Assert.Empty(Mentions.UserIdsIn(doc));

    [Fact]
    public void Only_newly_added_mentions_are_notified()
    {
        var before = DocMentioning(Alice);
        var after = DocMentioning(Alice, Bob);
        // Alice was already mentioned, so a later edit must not ping her again.
        Assert.Equal([Guid.Parse(Bob)], Mentions.NewlyMentioned(before, after, authorId: Guid.NewGuid()));
    }

    [Fact]
    public void Mentioning_yourself_notifies_nobody()
    {
        var after = DocMentioning(Alice);
        Assert.Empty(Mentions.NewlyMentioned(null, after, authorId: Guid.Parse(Alice)));
    }
}
