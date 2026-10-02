namespace Tesria.Api.Features.Search;

/// <summary>
/// How a page is indexed on PostgreSQL, in one place: the generated
/// <c>SearchVector</c> column, and the folding that the query and the
/// snippets must apply in exactly the same way.
/// </summary>
public static class SearchIndex
{
    /// <summary>The text search configuration: English stemming and stop words.</summary>
    public const string Config = "english";

    /// <summary>
    /// What the index and the query both see instead of the text itself.
    ///
    /// <para>Accents do not matter (t6-R02): "cafe" finds "café" and "naive"
    /// finds "naïve", as the page tree's filter already does (treeFilter.ts:
    /// NFD, then drop the combining marks U+0300 to U+036F). Done with
    /// built-in functions rather than the <c>unaccent</c> extension, so the
    /// database image is unchanged; the final NFC puts back together what NFD
    /// split without an accent to drop (Hangul, for one).</para>
    ///
    /// <para>A slash is a space: PostgreSQL's parser reads "word/word" as one
    /// compound lexeme (a file path), which makes each half unsearchable on
    /// its own ("Hocuspocus/Yjs").</para>
    ///
    /// <para>Every change keeps one character for one, so a snippet cut from
    /// folded text can be put back into the page's own spelling
    /// (<see cref="SearchSnippets.Restore"/>). Applied in SQL on both sides,
    /// the index and the query, so the two cannot disagree.</para>
    /// </summary>
    public static string Fold(string sqlExpression) =>
        $"normalize(regexp_replace(normalize(translate({sqlExpression}, '/', ' '), NFD), '[\\u0300-\\u036f]', '', 'g'), NFC)";

    /// <summary>
    /// What folding can change: a slash, and letters with accents in the
    /// Latin, Greek and Cyrillic alphabets, Vietnamese, Hangul and loose
    /// combining marks. A text with none of them is its own fold, and
    /// skipping the two normalizations for it saves most of a snippet's
    /// extra time.
    /// </summary>
    private const string Foldable = @"[/\u00c0-\u024f\u0300-\u036f\u0370-\u03ff\u0400-\u04ff\u1e00-\u1fff\uac00-\ud7a3]";

    /// <summary>
    /// <see cref="Fold"/>, or null for text it would leave as it is. For
    /// snippets only: a letter outside <see cref="Foldable"/> that folding
    /// would still change can cost a highlight there, never a match, because
    /// the index and the query always fold in full.
    /// </summary>
    public static string FoldedOrNull(string sqlExpression) =>
        $"case when {sqlExpression} ~ '{Foldable}' then {Fold(sqlExpression)} end";

    /// <summary>
    /// The generated column: the title with weight A and the text with
    /// weight D, so ranking can tell a word in the title from one in the
    /// text (<see cref="Bm25.TitleWeight"/>).
    /// </summary>
    public static readonly string VectorSql =
        $"setweight(to_tsvector('{Config}', {Fold("\"Title\"")}), 'A') || "
        + $"to_tsvector('{Config}', {Fold("\"SearchText\"")})";
}
