using System.Text;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Search;

/// <summary>
/// The passage of a page that actually matched.
///
/// The first 200 characters of a page is not a snippet: searching
/// "webhook" and being shown a page's opening sentence tells a reader (and
/// an assistant) nothing about why it came back, so the only way to judge
/// relevance is to open every result. Postgres has <c>ts_headline</c> for
/// exactly this; it has no EF binding, so it is written by hand.
/// </summary>
public static class SearchSnippets
{
    /// <summary>Marks the matching words. Plain text rather than HTML: these snippets go to an assistant as often as to a browser.</summary>
    private const string StartMarker = "**";
    private const string StopMarker = "**";

    // While a snippet is assembled, the marks and the gap between two
    // fragments are control characters, which page text does not contain,
    // so they cannot be confused with a "**" or "…" the page itself has.
    private const char Start = '\u0001';
    private const char Stop = '\u0002';
    private const char Gap = '\u0003';

    /// <summary>Between two passages of the same page.</summary>
    private const string FragmentDelimiter = " … ";

    /// <summary>Between two blocks of a page (T9-016): a heading and the paragraph after it, two table cells.</summary>
    private const string BlockSeparator = " · ";

    private const int FallbackWindow = 240;

    /// <summary>
    /// Snippets for pages already narrowed and permission-checked by the
    /// caller. One round trip for the whole page of results, not one per result.
    /// </summary>
    /// <param name="query">The query as searched: on PostgreSQL, already folded (<see cref="SearchIndex.Fold"/>).</param>
    public static async Task<Dictionary<Guid, string>> ForAsync(
        AppDbContext db, IReadOnlyList<Guid> pageIds, string query, CancellationToken ct)
    {
        var found = new Dictionary<Guid, string>();
        if (pageIds.Count == 0) return found;

        if (!db.Database.IsNpgsql())
        {
            // SQLite (tests) has no full-text engine here; a window around the
            // first matching word is still far better than the page's opening.
            var rows = await db.Pages.AsNoTracking()
                .Where(p => pageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.SearchText })
                .ToListAsync(ct);
            foreach (var row in rows) found[row.Id] = Window(row.SearchText, query);
            return found;
        }

        // The headline is cut from the folded text, which is what the query
        // matches (t6-R02: "cafe" finds "café"), and then put back into the
        // page's own spelling by Restore. Text with nothing to fold is used
        // as it is, and only folded text comes back with its original.
        var headlines = await PageSearch.QueryAsync(db, $"""
            select "Id", case when f is not null then "SearchText" end, f,
                   ts_headline('{SearchIndex.Config}', coalesce(f, "SearchText"),
                       websearch_to_tsquery('{SearchIndex.Config}', @query), @options)
            from (select "Id", "SearchText", {SearchIndex.FoldedOrNull("\"SearchText\"")} as f
                  from "Pages" where "Id" = any(@ids)) folded
            """,
            [
                ("query", query),
                ("ids", pageIds.ToArray()),
                ("options", $"StartSel={Start}, StopSel={Stop}, MaxWords=40, MinWords=20, ShortWord=3, MaxFragments=2, FragmentDelimiter={Gap}"),
            ],
            r => (Id: r.GetGuid(0), Text: r.IsDBNull(1) ? null : r.GetString(1), Folded: r.IsDBNull(2) ? null : r.GetString(2),
                  Headline: r.IsDBNull(3) ? "" : r.GetString(3)),
            ct);
        foreach (var h in headlines)
            found[h.Id] = Present(h.Text is null || h.Folded is null ? h.Headline : Restore(h.Headline, h.Text, h.Folded));
        return found;
    }

    /// <summary>
    /// Puts a headline cut from the folded text back into the page's own
    /// spelling: each passage is found in the folded text and replaced by the
    /// same stretch of the original. Folding keeps one character for one
    /// wherever an accent is dropped from a letter, so the two line up; where
    /// they do not (text with its accents typed as separate marks), the
    /// passage is left as folded, still readable, only without its accents.
    /// </summary>
    public static string Restore(string headline, string original, string folded)
    {
        if (headline.Length == 0 || ReferenceEquals(original, folded) || original == folded
            || original.Length != folded.Length)
            return headline;

        var restored = new StringBuilder(headline.Length);
        foreach (var fragment in headline.Split(Gap))
        {
            if (restored.Length > 0) restored.Append(Gap);
            var plain = new StringBuilder(fragment.Length);
            foreach (var c in fragment)
                if (c is not (Start or Stop)) plain.Append(c);
            var at = folded.IndexOf(plain.ToString(), StringComparison.Ordinal);
            if (at < 0 || plain.Length == 0)
            {
                restored.Append(fragment);
                continue;
            }
            var source = at;
            foreach (var c in fragment)
                restored.Append(c is Start or Stop ? c : original[source++]);
        }
        return restored.ToString();
    }

    /// <summary>
    /// The finished snippet: passages joined by " … ", the matching words in
    /// **bold**, and a break between blocks shown as " · " (T9-016), or as a
    /// plain space after a full stop, where a sentence already ends there.
    /// </summary>
    public static string Present(string assembled)
    {
        var sb = new StringBuilder(assembled.Length + 16);
        var i = 0;
        while (i < assembled.Length)
        {
            var c = assembled[i];
            if (c == Gap)
            {
                TrimEnd(sb);
                sb.Append(FragmentDelimiter);
                i++;
                while (i < assembled.Length && char.IsWhiteSpace(assembled[i])) i++;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                var end = i;
                var lineBreak = false;
                while (end < assembled.Length && char.IsWhiteSpace(assembled[end]))
                {
                    lineBreak |= assembled[end] == '\n';
                    end++;
                }
                var atEdge = sb.Length == 0 || end >= assembled.Length || assembled[end] == Gap;
                if (!atEdge)
                    sb.Append(lineBreak && !EndsSentence(sb) ? BlockSeparator : " ");
                i = end;
                continue;
            }
            sb.Append(c switch { Start => StartMarker, Stop => StopMarker, _ => c.ToString() });
            i++;
        }
        TrimEnd(sb);
        return sb.ToString().Trim();
    }

    /// <summary>Whether the text so far ends a sentence, looking past a closing bold mark.</summary>
    private static bool EndsSentence(StringBuilder sb)
    {
        var at = sb.Length - 1;
        while (at >= 0 && sb[at] == '*') at--;
        return at >= 0 && sb[at] is '.' or '!' or '?' or ':' or ';' or '…';
    }

    private static void TrimEnd(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
    }

    /// <summary>
    /// A window around the first query word found, with that word marked:
    /// the fallback when the database cannot do it properly.
    /// </summary>
    public static string Window(string text, string term)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var words = term.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('"', '\'', '(', ')'))
            .Where(w => w.Length > 2)
            .ToList();

        var at = -1;
        var matched = "";
        foreach (var word in words)
        {
            var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || (at >= 0 && index >= at)) continue;
            at = index;
            matched = word;
        }
        if (at < 0)
            return Present(text.Length <= FallbackWindow ? text : text[..FallbackWindow].TrimEnd() + " …");

        var start = Math.Max(0, at - FallbackWindow / 3);
        var length = Math.Min(FallbackWindow, text.Length - start);
        var slice = text.Substring(start, length);
        // Mark the hit, so the fallback reads like the real thing.
        var hit = slice.IndexOf(matched, StringComparison.OrdinalIgnoreCase);
        if (hit >= 0)
            slice = slice[..hit] + Start + slice.Substring(hit, matched.Length) + Stop
                  + slice[(hit + matched.Length)..];

        return (start > 0 ? "… " : "") + Present(slice) + (start + length < text.Length ? " …" : "");
    }
}
