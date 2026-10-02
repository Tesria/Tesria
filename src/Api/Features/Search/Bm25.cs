namespace Tesria.Api.Features.Search;

/// <summary>
/// BM25 relevance, with the title as a field of its own (BM25F, in the
/// simple form of Robertson and Zaragoza): dev-plan 23.1.
///
/// <para>PostgreSQL's <c>ts_rank</c>, which ranked search before 0.9, counts
/// how often the words appear but not how rare they are across the wiki or
/// how long the page is, so a long page that mentions a common word often
/// beat a short page about it, and a page titled with the word ranked below
/// pages that only mention it (t6-019). BM25 weighs each word by its rarity
/// (<see cref="Idf"/>), lets repeated mentions count for less and less
/// (<see cref="K1"/>), and evens out page length (<see cref="B"/>).</para>
///
/// <para>Pure arithmetic over counts the database supplies
/// (<see cref="PageSearch"/>), so it is tested on its own.</para>
/// </summary>
public static class Bm25
{
    /// <summary>
    /// Term frequency saturation: how quickly further mentions stop adding.
    /// 1.2 is the usual choice (Lucene, Elasticsearch); with it a word's
    /// contribution approaches at most <c>K1 + 1</c> times its rarity.
    /// </summary>
    public const double K1 = 1.2;

    /// <summary>
    /// Length normalization, from 0 (length ignored) to 1 (fully
    /// proportional). 0.75 is the usual choice: a page twice the average
    /// length needs noticeably more mentions to score the same.
    /// </summary>
    public const double B = 0.75;

    /// <summary>
    /// How much one occurrence in the title counts against one in the text.
    /// A title is the page saying what it is about, so a page titled
    /// "Working offline" should come before pages that only mention
    /// offline, even several times (t6-019). Measured on the seeded DOCS
    /// space by searching for each of its 203 titles: the page with that
    /// title came first for 125 of them at 1 (no boost), 169 at 3, 182 at 6,
    /// 189 at 8 and 191 at 10. Past 8 little is gained, and the text of the
    /// pages that share a title word counts for less and less. The ones still
    /// below first share their word with many titles ("Pages" among "Page
    /// tree", "Page properties" and the like), where the text should decide.
    /// </summary>
    public const double TitleWeight = 8;

    /// <summary>What the scores are relative to: the pages the searcher can see.</summary>
    /// <param name="Documents">How many pages.</param>
    /// <param name="AverageTitleLength">Their mean title length, in distinct words.</param>
    /// <param name="AverageBodyLength">Their mean text length, in distinct words.</param>
    public readonly record struct Corpus(long Documents, double AverageTitleLength, double AverageBodyLength);

    /// <summary>One query word on one page.</summary>
    /// <param name="DocumentFrequency">How many of the corpus's pages contain it.</param>
    /// <param name="InTitle">How often it appears in this page's title.</param>
    /// <param name="InBody">How often it appears in this page's text.</param>
    public readonly record struct Term(long DocumentFrequency, int InTitle, int InBody);

    /// <summary>
    /// Inverse document frequency, in the form that never goes negative
    /// (Lucene's): a word on almost every page still counts a little, and a
    /// word on one page in a thousand counts about seven times as much.
    /// </summary>
    public static double Idf(long documents, long documentFrequency)
    {
        var n = Math.Max(documents, 0);
        var df = Math.Clamp(documentFrequency, 0, n);
        return Math.Log(1 + (n - df + 0.5) / (df + 0.5));
    }

    /// <summary>A page's score for the query words: higher is better, 0 when none appear.</summary>
    /// <param name="titleLength">This page's title length, in the units of <see cref="Corpus.AverageTitleLength"/>.</param>
    /// <param name="bodyLength">This page's text length, in the units of <see cref="Corpus.AverageBodyLength"/>.</param>
    public static double Score(Corpus corpus, int titleLength, int bodyLength, IEnumerable<Term> terms)
    {
        var titleNorm = Normalization(titleLength, corpus.AverageTitleLength);
        var bodyNorm = Normalization(bodyLength, corpus.AverageBodyLength);
        var score = 0.0;
        foreach (var term in terms)
        {
            // BM25F: the fields' frequencies are weighted and length-normalized
            // first, then saturated once, so a word in both the title and the
            // text is one word found twice, not two independent matches.
            var tf = TitleWeight * Math.Max(term.InTitle, 0) / titleNorm + Math.Max(term.InBody, 0) / bodyNorm;
            if (tf <= 0) continue;
            score += Idf(corpus.Documents, term.DocumentFrequency) * tf * (K1 + 1) / (K1 + tf);
        }
        return score;
    }

    /// <summary>1 for a field of average length, more for a longer one, less for a shorter one.</summary>
    private static double Normalization(int length, double average) =>
        average > 0 ? 1 - B + B * Math.Max(length, 0) / average : 1;
}
