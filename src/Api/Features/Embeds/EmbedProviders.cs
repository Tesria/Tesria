using System.Text.RegularExpressions;

namespace Tesria.Api.Features.Embeds;

/// <summary>
/// Turns the URL a person pasted into the URL that actually frames.
///
/// Every provider here is a *narrowing*: a YouTube watch page becomes the
/// no-cookie embed player, a Google Doc becomes its preview view. A host on
/// the allowlist with no provider rule falls through to the URL as pasted,
/// which is what makes "allowlist our internal Grafana" work without code.
/// </summary>
public static partial class EmbedProviders
{
    public sealed record Embed(string Url, string Provider, string? AspectRatio);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{6,20}$")] private static partial Regex IdRegex();

    /// <summary>The canonical frame URL for this address, or null to frame it as given.</summary>
    public static Embed? Resolve(Uri uri)
    {
        // Not TrimStart('w', '.') — that eats the leading letters of any host
        // beginning with w ("wiki.example.com" would become "iki.example.com").
        // The EndsWith checks below already cover the "www." forms.
        var host = uri.Host.ToLowerInvariant().TrimEnd('.');
        var path = uri.AbsolutePath.Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        if (host.EndsWith("youtube.com", StringComparison.Ordinal) || host.EndsWith("youtube-nocookie.com", StringComparison.Ordinal))
        {
            // watch?v=, /embed/, /shorts/, /live/ all reduce to one id.
            var id = query["v"]
                ?? (segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live" ? segments[1] : null);
            return Valid(id) ? new Embed($"https://www.youtube-nocookie.com/embed/{id}", "YouTube", "16 / 9") : null;
        }
        if (host == "youtu.be")
            return Valid(segments.FirstOrDefault()) ? new Embed($"https://www.youtube-nocookie.com/embed/{segments[0]}", "YouTube", "16 / 9") : null;

        if (host.EndsWith("vimeo.com", StringComparison.Ordinal))
        {
            var id = segments.LastOrDefault(s => s.All(char.IsDigit) && s.Length > 4);
            return id is null ? null : new Embed($"https://player.vimeo.com/video/{id}", "Vimeo", "16 / 9");
        }

        if (host.EndsWith("loom.com", StringComparison.Ordinal))
        {
            var id = segments.Length >= 2 && segments[0] is "share" or "embed" ? segments[1] : null;
            return id is null || !id.All(char.IsAsciiLetterOrDigit) ? null : new Embed($"https://www.loom.com/embed/{id}", "Loom", "16 / 9");
        }

        if (host.EndsWith("figma.com", StringComparison.Ordinal))
            // Figma takes the original URL as a parameter rather than an id.
            return new Embed($"https://www.figma.com/embed?embed_host=tesria&url={Uri.EscapeDataString(uri.ToString())}", "Figma", "4 / 3");

        if (host.EndsWith("miro.com", StringComparison.Ordinal))
        {
            var id = segments.Length >= 3 && segments[0] == "app" && segments[1] == "board" ? segments[2] : null;
            return id is null ? null : new Embed($"https://miro.com/app/live-embed/{id}", "Miro", "4 / 3");
        }

        if (host.EndsWith("codepen.io", StringComparison.Ordinal))
        {
            var id = segments.Length >= 3 && segments[1] is "pen" or "embed" ? segments[2] : null;
            return id is null ? null : new Embed($"https://codepen.io/{segments[0]}/embed/{id}", "CodePen", "4 / 3");
        }

        if (host is "docs.google.com" or "drive.google.com")
        {
            // .../edit and .../view both have a /preview form that frames.
            var trimmed = path.EndsWith("/edit", StringComparison.Ordinal) ? path[..^5]
                : path.EndsWith("/view", StringComparison.Ordinal) ? path[..^5]
                : path.EndsWith("/preview", StringComparison.Ordinal) ? path[..^8]
                : path;
            return new Embed($"https://{uri.Host}/{trimmed}/preview", "Google", "4 / 3");
        }

        return null;

        static bool Valid(string? id) => id is not null && IdRegex().IsMatch(id);
    }
}
