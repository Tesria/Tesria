using Tesria.Api.Domain;
using Tesria.Api.Features.Embeds;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Where pages may show images from (dev-plan 14.3, security gap 2). One
/// rule, read in three places: the app's CSP, an exported site's
/// <c>&lt;meta&gt;</c> CSP, and the editor, which says why a picture from
/// an unlisted host does not show. Hosts are written and matched exactly as
/// the embed allowlist's are.
/// </summary>
public static class ImagePolicy
{
    /// <summary>The hosts images may come from besides this instance, or null when any https host may.</summary>
    public static string[]? Hosts(SiteSettings s) =>
        s.RestrictImageHosts ? EmbedAllowlist.Parse(s.ImageAllowlist) : null;

    /// <summary>The <c>img-src</c> sources after <c>'self' data: blob:</c>.</summary>
    public static string CspSources(SiteSettings s) =>
        Hosts(s) is { } hosts ? string.Join(' ', EmbedAllowlist.CspSources(hosts)) : "https:";

    /// <summary>
    /// The same rule for an exported page, which is opened from a disk or a
    /// plain web host with no headers of ours: a <c>&lt;meta&gt;</c> CSP that
    /// governs images and nothing else. Empty when images are not restricted.
    /// </summary>
    public static string ExportMeta(SiteSettings s)
    {
        if (!s.RestrictImageHosts) return "";
        var sources = CspSources(s);
        return $"<meta http-equiv=\"Content-Security-Policy\" content=\"img-src 'self' data: blob:{(sources.Length > 0 ? " " + sources : "")}\" />";
    }
}
