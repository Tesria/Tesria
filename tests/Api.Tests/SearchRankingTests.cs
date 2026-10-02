using Tesria.Api.Features.Search;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The parts of search that are pure functions (dev-plan 23.1): BM25's
/// arithmetic, the query's words, the text a page is indexed by, and how a
/// snippet is put together. Ranking against a real index is in
/// <see cref="SearchPostgresTests"/>.
/// </summary>
public class SearchRankingTests
{
    private static readonly Bm25.Corpus Wiki = new(Documents: 1000, AverageTitleLength: 3, AverageBodyLength: 100);

    private static double Body(int tf, int bodyLength = 100, long df = 10) =>
        Bm25.Score(Wiki, titleLength: 3, bodyLength, [new Bm25.Term(df, 0, tf)]);

    // ---- BM25

    [Fact]
    public void A_rarer_word_counts_for_more()
    {
        Assert.True(Bm25.Idf(1000, 1) > Bm25.Idf(1000, 10));
        Assert.True(Bm25.Idf(1000, 10) > Bm25.Idf(1000, 500));
        Assert.True(Body(1, df: 2) > Body(1, df: 200));
    }

    [Fact]
    public void A_word_on_every_page_still_counts_a_little_and_never_against()
    {
        Assert.True(Bm25.Idf(1000, 1000) > 0);
        // A stale or impossible count is clamped rather than going negative.
        Assert.Equal(Bm25.Idf(1000, 1000), Bm25.Idf(1000, 5000));
        Assert.True(Bm25.Idf(0, 0) >= 0);
    }

    [Fact]
    public void Repeated_mentions_add_less_and_less_up_to_a_ceiling()
    {
        var one = Body(1);
        var two = Body(2);
        var three = Body(3);
        var many = Body(1000);
        Assert.True(two > one && three > two);
        Assert.True(two - one > three - two);
        // K1 saturation: a word's score approaches (K1 + 1) times its rarity, never more.
        Assert.True(many < Bm25.Idf(1000, 10) * (Bm25.K1 + 1));
        Assert.True(many > 0.95 * Bm25.Idf(1000, 10) * (Bm25.K1 + 1));
    }

    [Fact]
    public void A_shorter_page_with_the_same_mentions_ranks_higher()
    {
        Assert.True(Body(2, bodyLength: 50) > Body(2, bodyLength: 100));
        Assert.True(Body(2, bodyLength: 100) > Body(2, bodyLength: 400));
        // A page of average length is scored as if length did not matter:
        // tf / (1 - b + b * 1) = tf.
        var idf = Bm25.Idf(1000, 10);
        Assert.Equal(idf * 2 * (Bm25.K1 + 1) / (Bm25.K1 + 2), Body(2), 10);
    }

    [Fact]
    public void Length_normalization_is_partial_with_b()
    {
        // With B = 0.75, a page twice the average length needs 1.75 times the
        // mentions, not twice, to score the same.
        Assert.Equal(1 - Bm25.B + Bm25.B * 2, 1.75, 10);
        Assert.Equal(Body(7, bodyLength: 200), Body(4, bodyLength: 100), 10);
    }

    [Fact]
    public void A_word_in_the_title_outweighs_several_in_the_text()
    {
        // t6-019: the page titled "Working offline" before pages that only mention offline.
        var titled = Bm25.Score(Wiki, titleLength: 2, bodyLength: 30, [new Bm25.Term(10, 1, 0)]);
        var mentioned = Bm25.Score(Wiki, titleLength: 3, bodyLength: 100, [new Bm25.Term(10, 0, 4)]);
        Assert.True(titled > mentioned);
        // And the title weight is what does it: as body text, one mention is less.
        Assert.True(Bm25.Score(Wiki, 2, 30, [new Bm25.Term(10, 0, 1)]) < mentioned);
        // A word in both is one word found twice, saturating together.
        var both = Bm25.Score(Wiki, 2, 30, [new Bm25.Term(10, 1, 3)]);
        Assert.True(both > titled && both < Bm25.Idf(1000, 10) * (Bm25.K1 + 1));
    }

    [Fact]
    public void Each_query_word_adds_its_own_score_and_a_missing_one_nothing()
    {
        var common = new Bm25.Term(500, 0, 2);
        var rare = new Bm25.Term(2, 0, 2);
        var both = Bm25.Score(Wiki, 3, 100, [common, rare]);
        Assert.Equal(Bm25.Score(Wiki, 3, 100, [common]) + Bm25.Score(Wiki, 3, 100, [rare]), both, 10);
        Assert.Equal(0, Bm25.Score(Wiki, 3, 100, [new Bm25.Term(5, 0, 0)]));
        Assert.Equal(0, Bm25.Score(Wiki, 3, 100, []));
        // An empty wiki or unknown averages do not divide by zero.
        Assert.True(double.IsFinite(Bm25.Score(new Bm25.Corpus(0, 0, 0), 0, 0, [new Bm25.Term(0, 1, 1)])));
    }

    // ---- The query's words

    [Fact]
    public void The_wanted_words_leave_out_excluded_ones()
    {
        Assert.Equal(["launch"], QueryWords.Wanted("'launch' & !'beta'"));
        Assert.Equal(["releas", "checklist"], QueryWords.Wanted("'releas' <-> 'checklist'"));
        Assert.Equal(["launch", "releas"], QueryWords.Wanted("'launch' | 'releas' | 'launch'"));
        Assert.Equal(["keep"], QueryWords.Wanted("'keep' & !( 'a' <-> 'b' )"));
        Assert.Equal(["it's"], QueryWords.Wanted("'it''s'"));
        Assert.Empty(QueryWords.Wanted(""));
        Assert.Equal(QueryWords.Max, QueryWords.Wanted(string.Join(" & ", Enumerable.Range(0, 20).Select(i => $"'w{i}'"))).Count);
    }

    // ---- What a page is indexed by

    private static string Doc(params string[] blocks) =>
        $$"""{"type":"doc","content":[{{string.Join(",", blocks)}}]}""";

    private static string Para(params string[] inline) =>
        $$"""{"type":"paragraph","content":[{{string.Join(",", inline)}}]}""";

    private static string Text(string text, string? mark = null) =>
        mark is null
            ? $$"""{"type":"text","text":"{{text}}"}"""
            : $$"""{"type":"text","text":"{{text}}","marks":[{"type":"{{mark}}"}]}""";

    [Fact]
    public void Blocks_are_separated_and_inline_text_is_joined_as_written()
    {
        // T9-016: "see Label pages ." and "What is found Different endings".
        var text = SearchableText.Extract(Doc(
            """{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"What is found"}]}""",
            Para(Text("see "), Text("Label pages", "link"), Text(".")),
            """{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"one"}]}]},{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"two"}]}]}]}"""));
        Assert.Equal("What is found\nsee Label pages.\none\ntwo", text);
    }

    [Fact]
    public void Chips_count_as_the_words_a_reader_sees()
    {
        // T5-026: "Attendees , , ," where a date, mentions and a status were.
        var text = SearchableText.Extract(Doc(Para(
            Text("Met on "), """{"type":"date","attrs":{"date":"2026-09-02"}}""",
            Text(" with "), """{"type":"mention","attrs":{"id":"x","label":"Priya Natarajan"}}""",
            Text(", "), """{"type":"mention","attrs":{"id":"y","label":"Sam Lee"}}""",
            Text(": "), """{"type":"status","attrs":{"text":"On track","color":"green"}}""")));
        Assert.Equal("Met on 2 Sep 2026 with @Priya Natarajan, @Sam Lee: On track", text);
    }

    [Fact]
    public void A_diagram_counts_by_its_labels_not_its_source()
    {
        // t6-021: "flowchart LR A[Laptop] -->|change| B(Sync service) ..."
        const string source = "flowchart LR\n  A[Laptop] -->|change| B(Sync service)\n  B --> C[(Storage)]\n  db[(\"Database\")] --> pgb[\"pgbackrest service\"]\n  classDef big fill:#f9f";
        var text = SearchableText.Extract(Doc(
            $$"""{"type":"codeBlock","attrs":{"language":"mermaid"},"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(source)}}}]}"""));
        Assert.Equal("Laptop\nchange\nSync service\nStorage\nDatabase\npgbackrest service", text);

        Assert.Equal(["Kestrel Sync releases", "2026", "Version 2, with offline editing", "Hello John"],
            SearchableText.MermaidLabels("timeline\n  title Kestrel Sync releases\n  2026 : Version 2, with offline editing\nsequenceDiagram\n  Alice->>John: Hello John")
                .Select(l => l.Trim()));
    }

    [Fact]
    public void Code_keeps_its_lines_apart_and_drawn_things_add_nothing()
    {
        var text = SearchableText.Extract(Doc(
            """{"type":"codeBlock","attrs":{"language":"bash"},"content":[{"type":"text","text":"docker compose ps\n\n  docker compose logs app"}]}""",
            """{"type":"math","attrs":{"latex":"x^2","display":true}}""",
            """{"type":"expand","attrs":{"title":"More detail"},"content":[{"type":"paragraph","content":[{"type":"text","text":"Inside"}]}]}""",
            """{"type":"image","attrs":{"src":"/x.png","alt":"alt text","caption":"A caption"}}"""));
        Assert.Equal("docker compose ps\ndocker compose logs app\nMore detail\nInside\nA caption", text);
    }

    [Fact]
    public void Slashes_are_kept_for_snippets_and_bad_json_is_empty()
    {
        // The index reads a slash as a space (SearchIndex.Fold); the text keeps it.
        Assert.Equal("Hocuspocus/Yjs", SearchableText.Extract(Doc(Para(Text("Hocuspocus/Yjs")))));
        Assert.Equal("", SearchableText.Extract("not json"));
        // An old or odd document is read as far as it makes sense, never thrown
        // on: the migrate step rebuilds every page with this.
        Assert.Equal("ok", SearchableText.Extract("""{"type":"doc","content":[{"type":"codeBlock","content":["x",3]},7,{"type":"paragraph","content":[{"type":"text","text":"ok"},{"type":"text","text":5}]}]}"""));
        Assert.Equal("", SearchableText.Extract(""));
    }

    // ---- Snippets

    [Fact]
    public void A_snippet_shows_where_blocks_meet()
    {
        // A heading runs into its paragraph without a mark between them (T9-016);
        // after a full stop the sentence already shows the break.
        Assert.Equal("What is found · Different endings of a word **match**. Searching works",
            SearchSnippets.Present("What is found\nDifferent endings of a word \u0001match\u0002.\nSearching works"));
        Assert.Equal("Property · Value · Status · On track",
            SearchSnippets.Present("Property\nValue\nStatus\nOn track"));
        Assert.Equal("first **one** … second",
            SearchSnippets.Present("first \u0001one\u0002\u0003second\n"));
        Assert.Equal("ends **here**. Next", SearchSnippets.Present("ends \u0001here\u0002.\nNext"));
        Assert.Equal("**Title** · Next", SearchSnippets.Present("\u0001Title\u0002\nNext"));
    }

    [Fact]
    public void A_snippet_cut_from_folded_text_gets_its_accents_back()
    {
        // t6-R02: "cafe" matches "café"; the reader still sees café.
        const string original = "Un café naïve\nsur la terrasse";
        const string folded = "Un cafe naive\nsur la terrasse";
        var headline = "Un \u0001cafe\u0002 naive\nsur";
        Assert.Equal("Un **café** naïve · sur", SearchSnippets.Present(SearchSnippets.Restore(headline, original, folded)));

        // Two passages, each put back where it was found.
        Assert.Equal("Un \u0001café\u0002\u0003\u0001terrasse\u0002",
            SearchSnippets.Restore("Un \u0001cafe\u0002\u0003\u0001terrasse\u0002", original, folded));

        // When the two do not line up, the folded passage is kept as it is.
        Assert.Equal(headline, SearchSnippets.Restore(headline, "Un cafe\u0301 naive", folded));
    }

    [Fact]
    public void The_fallback_window_marks_the_word_and_separates_blocks()
    {
        Assert.Equal("Intro · the **widget** roadmap", SearchSnippets.Window("Intro\nthe widget roadmap", "widget"));
    }
}
