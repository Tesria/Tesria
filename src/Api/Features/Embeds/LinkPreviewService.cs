using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Embeds;

public interface ILinkPreviewService
{
    Task<LinkPreview> GetAsync(string url, CancellationToken ct);
}

/// <summary>
/// Fetches an external page's Open Graph tags for a smart link, through the
/// SSRF guard (dev-plan 3.4) and never around it. Results are cached: a
/// successful preview for a week, a failure for an hour, so a dead link does
/// not mean an outbound request on every page view.
/// </summary>
public sealed partial class LinkPreviewService(AppDbContext db, EgressGuard egress) : ILinkPreviewService
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromHours(1);

    /// <summary>Enough for a &lt;head&gt;. A page that puts its OG tags past this is not worth the bandwidth.</summary>
    private const int MaxBytes = 256 * 1024;

    [GeneratedRegex("""<meta[^>]+?(?:property|name)\s*=\s*["'](og:[a-z:]+|description|title)["'][^>]*?>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex("""content\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ContentRegex();

    [GeneratedRegex("""<title[^>]*>(.*?)</title>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    public async Task<LinkPreview> GetAsync(string url, CancellationToken ct)
    {
        var normalized = Normalize(url);
        var hash = Hash(normalized);

        var cached = await db.LinkPreviews.FirstOrDefaultAsync(p => p.UrlHash == hash, ct);
        var ttl = cached?.Error is null ? SuccessTtl : FailureTtl;
        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < ttl) return cached;

        var fresh = await FetchAsync(normalized, ct);
        if (cached is null)
        {
            cached = new LinkPreview { Id = Guid.NewGuid(), UrlHash = hash, Url = normalized };
            db.LinkPreviews.Add(cached);
        }
        cached.Title = fresh.Title;
        cached.Description = fresh.Description;
        cached.SiteName = fresh.SiteName;
        cached.ImageUrl = fresh.ImageUrl;
        cached.Error = fresh.Error;
        cached.FetchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return cached;
    }

    private async Task<LinkPreview> FetchAsync(string url, CancellationToken ct)
    {
        var result = new LinkPreview { UrlHash = "", Url = url };
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            result.Error = "Not a web address.";
            return result;
        }

        try
        {
            using var handler = egress.CreateHandler();
            using var client = new HttpClient(handler) { Timeout = EgressGuard.Timeout };
            using var response = await egress.SendAsync(client, target =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, target);
                request.Headers.UserAgent.ParseAdd("Tesria-LinkPreview/1.0");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                return request;
            }, uri, ct);

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"The site answered {(int)response.StatusCode}.";
                return result;
            }
            if (response.Content.Headers.ContentType?.MediaType is { } type
                && !type.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "That address is not a web page.";
                return result;
            }

            var html = await ReadCappedAsync(response, ct);
            Populate(result, html, uri);
            return result;
        }
        catch (EgressBlockedException ex)
        {
            // The guard's own reason is safe to show: it is about the address
            // the author typed, not about this instance's network.
            result.Error = ex.Message;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            result.Error = "Could not reach that address.";
            return result;
        }
    }

    /// <summary>Reads at most <see cref="MaxBytes"/>: a hostile server must not be able to stream forever.</summary>
    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes];
        var read = 0;
        while (read < MaxBytes)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, MaxBytes - read), ct);
            if (n == 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static void Populate(LinkPreview result, string html, Uri uri)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTagRegex().Matches(html))
        {
            var key = tag.Groups[1].Value.ToLowerInvariant();
            var content = ContentRegex().Match(tag.Value);
            if (content.Success && !tags.ContainsKey(key)) tags[key] = Decode(content.Groups[1].Value);
        }

        result.Title = Clamp(tags.GetValueOrDefault("og:title")
            ?? tags.GetValueOrDefault("title")
            ?? (TitleRegex().Match(html) is { Success: true } m ? Decode(m.Groups[1].Value) : null), 500);
        result.Description = Clamp(tags.GetValueOrDefault("og:description") ?? tags.GetValueOrDefault("description"), 1000);
        result.SiteName = Clamp(tags.GetValueOrDefault("og:site_name") ?? uri.Host, 200);

        // An image is rendered by the browser, so it must be an absolute https
        // URL, never a data: URI from a page we do not control, and never a
        // relative path resolved against our own origin.
        var image = tags.GetValueOrDefault("og:image");
        if (image is not null
            && Uri.TryCreate(image, UriKind.Absolute, out var imageUri)
            && imageUri.Scheme == Uri.UriSchemeHttps)
            result.ImageUrl = Clamp(imageUri.ToString(), 2048);

        if (result.Title is null) result.Error = "That page has no title to show.";
    }

    private static string? Clamp(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string Decode(string value) => System.Net.WebUtility.HtmlDecode(value).Replace('\n', ' ').Trim();

    /// <summary>Drops the fragment so <c>#a</c> and <c>#b</c> share one cache entry; everything else is left alone.</summary>
    public static string Normalize(string url)
    {
        var trimmed = (url ?? "").Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;
        return new UriBuilder(uri) { Fragment = "" }.Uri.ToString();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
