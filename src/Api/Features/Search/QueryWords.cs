using System.Text;

namespace Tesria.Api.Features.Search;

/// <summary>
/// The words a query asks for, read from PostgreSQL's own rendering of the
/// parsed query (<c>websearch_to_tsquery(...)::text</c>), so they are stemmed
/// and folded exactly as the index is.
/// </summary>
public static class QueryWords
{
    /// <summary>More than this many words add little to the order and cost a count each.</summary>
    public const int Max = 8;

    /// <summary>
    /// The distinct words the query wants, in order, without the ones it
    /// excludes: in <c>'launch' &amp; !'beta' &amp; !( 'a' &lt;-&gt; 'b' )</c>,
    /// only <c>launch</c>. An excluded word is on none of the results, so it
    /// would add nothing to any score.
    /// </summary>
    public static IReadOnlyList<string> Wanted(string tsquery)
    {
        var words = new List<string>();
        var negated = new Stack<bool>();
        var not = false;
        for (var i = 0; i < tsquery.Length; i++)
        {
            switch (tsquery[i])
            {
                case '!':
                    not = !not;
                    break;
                case '(':
                    negated.Push(not || (negated.Count > 0 && negated.Peek()));
                    not = false;
                    break;
                case ')':
                    if (negated.Count > 0) negated.Pop();
                    break;
                case '\'':
                    // A quoted lexeme; '' inside it is a quote.
                    var word = new StringBuilder();
                    for (i++; i < tsquery.Length; i++)
                    {
                        if (tsquery[i] != '\'') { word.Append(tsquery[i]); continue; }
                        if (i + 1 < tsquery.Length && tsquery[i + 1] == '\'') { word.Append('\''); i++; continue; }
                        break;
                    }
                    var excluded = not || (negated.Count > 0 && negated.Peek());
                    not = false;
                    var text = word.ToString();
                    if (!excluded && text.Length > 0 && !words.Contains(text)) words.Add(text);
                    break;
                case '&' or '|' or '<':
                    not = false;
                    break;
            }
        }
        return words.Count > Max ? words.Take(Max).ToList() : words;
    }
}
