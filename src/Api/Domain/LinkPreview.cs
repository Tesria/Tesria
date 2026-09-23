namespace Tesria.Api.Domain;

/// <summary>
/// A cached Open Graph summary of an external page (dev-plan Phase 7 Wave E,
/// "smart links"). Cached because a page with twenty links must not make
/// twenty outbound requests every time anyone opens it, and because those
/// requests leave the instance, which is exactly what the egress guard
/// exists to keep rare and controlled.
/// </summary>
public class LinkPreview
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 of the normalized URL: the lookup key, and bounded unlike the URL itself.</summary>
    public required string UrlHash { get; set; }

    public required string Url { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SiteName { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Null when the fetch succeeded; the reason when it did not (a refusal is cached too, briefly).</summary>
    public string? Error { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
