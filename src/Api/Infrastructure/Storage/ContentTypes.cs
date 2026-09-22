namespace Tesria.Api.Infrastructure.Storage;

/// <summary>
/// What an uploaded file will be served as (dev-plan 3.4).
///
/// The client's declared type is a suggestion, not a fact. Where the bytes
/// say what they are (a handful of common signatures) the bytes win. Where
/// they do not, the declared type is kept unless it is something a browser
/// might <em>execute</em> (HTML, SVG, XML, scripts) in which case the file
/// is served as an opaque download. Downloads already carry
/// <c>Content-Disposition: attachment</c> and the global <c>nosniff</c>
/// header; this closes the remaining gap, where a same-origin HTML or SVG
/// attachment opened in a tab would run with the site's cookies.
/// </summary>
public static class ContentTypes
{
    public const string Opaque = "application/octet-stream";

    private static readonly HashSet<string> Scriptable = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/html", "application/xhtml+xml", "image/svg+xml", "text/xml", "application/xml",
        "application/javascript", "text/javascript", "application/ecmascript", "text/ecmascript",
        "application/x-javascript", "text/vbscript", "application/mathml+xml", "application/xslt+xml",
        "text/cache-manifest", "multipart/x-mixed-replace",
    };

    /// <summary>Decides the stored type from the first bytes and the declared type.</summary>
    public static string Resolve(ReadOnlySpan<byte> head, string? declared)
    {
        if (Sniff(head) is { } sniffed) return sniffed;

        var type = (declared ?? "").Split(';')[0].Trim();
        if (type.Length == 0) return Opaque;
        if (Scriptable.Contains(type)) return Opaque;
        // Anything that looks like markup is treated as markup regardless of the label.
        if (LooksLikeMarkup(head)) return Opaque;
        return type;
    }

    private static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 8 && h[..8].SequenceEqual("\u0089PNG\r\n\u001a\n"u8)) return "image/png";
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "image/jpeg";
        if (h.Length >= 6 && (h[..6].SequenceEqual("GIF87a"u8) || h[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        if (h.Length >= 5 && h[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        return null;
    }

    private static bool LooksLikeMarkup(ReadOnlySpan<byte> h)
    {
        var i = 0;
        while (i < h.Length && (h[i] == ' ' || h[i] == '\t' || h[i] == '\r' || h[i] == '\n')) i++;
        if (i >= h.Length || h[i] != '<') return false;
        var rest = h[i..];
        return StartsWithIgnoreCase(rest, "<!doctype") || StartsWithIgnoreCase(rest, "<html")
            || StartsWithIgnoreCase(rest, "<svg") || StartsWithIgnoreCase(rest, "<?xml")
            || StartsWithIgnoreCase(rest, "<script") || StartsWithIgnoreCase(rest, "<body")
            || StartsWithIgnoreCase(rest, "<head") || StartsWithIgnoreCase(rest, "<iframe");
    }

    private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> span, string prefix)
    {
        if (span.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (char.ToLowerInvariant((char)span[i]) != prefix[i]) return false;
        return true;
    }
}
