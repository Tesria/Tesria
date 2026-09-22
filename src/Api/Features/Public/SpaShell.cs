using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Branding;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Public;

/// <summary>
/// The SPA's HTML shell, as the server sends it for every application route
/// (dev-plan 13.1, decision 3; before that, public pages only, dev-plan 5.2).
///
/// <para><b>Why the server writes into the shell at all.</b> Two things have
/// to be right before any script runs. The theme: a forced dark theme that
/// arrives with the JavaScript bundle is a flash of white on every load. And
/// the tab title and link preview, which crawlers read without running
/// anything. So the shell arrives with the title, the favicon, the custom
/// accent's stylesheet and the theme locks already in it.</para>
///
/// <para><b>What it must never touch: the inline script.</b> The CSP allows
/// that script by its SHA-256, computed at startup from the file on disk
/// (<c>SecurityHeadersMiddleware</c>). Change one character of it per
/// instance and the browser refuses to run it, silently. So everything
/// instance-specific goes into attributes on <c>&lt;html&gt;</c>, a
/// <c>&lt;style&gt;</c> block, <c>&lt;link&gt;</c>s and the title, and the
/// script reads the attributes. A test renders a branded shell from the real
/// <c>index.html</c> and compares the script's bytes.</para>
///
/// <para><b>Public titles are decided as the anonymous reader.</b> Link
/// previews are fetched by crawlers without a session, and a shared link
/// must never carry a private page's title, whoever pasted it. Signed-in
/// readers get their page's title from the SPA a moment later.</para>
/// </summary>
public sealed partial class SpaShell(IWebHostEnvironment env)
{
    private string? _shell;

    /// <summary>The built index.html, read once. Null when there is no web root.</summary>
    public string? Source()
    {
        if (_shell is not null) return _shell;
        var file = env.WebRootFileProvider?.GetFileInfo("index.html");
        if (file is null || !file.Exists) return null;
        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        return _shell = reader.ReadToEnd();
    }

    /// <summary>The fallback endpoint: every application route that is not a file or an API.</summary>
    public static async Task<IResult> Handle(
        HttpContext context, SpaShell shells, AppDbContext db, IPermissionService perms,
        ISiteSettingsService settingsService, IConfiguration config)
    {
        // An unknown API route is a 404, never a page of HTML a client would
        // try to parse as JSON.
        var requested = context.Request.Path;
        if (requested.StartsWithSegments("/api") || requested.StartsWithSegments("/mcp"))
            return Results.NotFound();

        var shell = shells.Source();
        if (shell is null) return Results.NotFound();

        var settings = await settingsService.GetAsync();
        var brand = BrandView.From(settings);
        var path = context.Request.Path.Value ?? "/";

        var title = BrandTitle.Format(settings.InstanceName, section: BrandTitle.SectionFor(path));
        string? meta = null;

        var m = PublicPath().Match(path);
        if (m.Success)
        {
            string? spaceName = null, pageTitle = null, description = null;
            var key = m.Groups["key"].Value.ToUpperInvariant();
            if (m.Groups["page"].Success && Guid.TryParse(m.Groups["page"].Value, out var pageId))
            {
                if (await perms.IsPubliclyViewablePageAsync(pageId))
                {
                    var page = await db.Pages.AsNoTracking().Where(p => p.Id == pageId)
                        .Select(p => new { p.Title, p.SearchText, SpaceName = p.Space!.Name })
                        .FirstOrDefaultAsync();
                    if (page is not null)
                    {
                        spaceName = page.SpaceName;
                        pageTitle = page.Title;
                        description = Excerpt(page.SearchText);
                    }
                }
            }
            else
            {
                var space = await db.Spaces.AsNoTracking().Where(x => x.Key == key)
                    .Select(x => new { x.Id, x.Name, x.Description }).FirstOrDefaultAsync();
                if (space is not null && await perms.IsPubliclyViewableSpaceAsync(space.Id))
                {
                    spaceName = space.Name;
                    description = space.Description;
                }
            }

            if (spaceName is not null)
            {
                title = BrandTitle.Format(settings.InstanceName, spaceName, pageTitle);
                var url = $"{SiteUrl.Resolve(settings, config)}{context.Request.Path}";
                meta = OpenGraph(pageTitle ?? spaceName, description, url, settings.InstanceName);
            }
        }

        var html = Render(shell, title, meta, brand);
        context.Response.Headers.CacheControl = "no-cache";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Writes the title, optional link-preview tags and the branding into the
    /// shell, leaving every script exactly as it was.
    /// </summary>
    public static string Render(string shell, string title, string? meta, BrandView brand)
    {
        var html = TitleTag().Replace(shell,
            $"<title>{WebUtility.HtmlEncode(title)}</title>" + (meta is null ? "" : "\n" + meta), 1);

        if (brand.FaviconLinks() is { } favicons)
            html = IconLink().Replace(html, favicons, 1);

        var attrs = brand.HtmlAttributes();
        if (attrs.Count > 0)
        {
            var extra = string.Concat(attrs.Select(a => $" {a.Name}=\"{WebUtility.HtmlEncode(a.Value)}\""));
            html = HtmlTag().Replace(html, match => match.Value[..^1] + extra + ">", 1);
        }

        // The stylesheet is built only from normalised #rrggbb values, so
        // nothing a person typed reaches it as text (decision 4).
        var css = brand.AccentStylesheet();
        if (css.Length > 0)
            html = HeadClose().Replace(html, $"<style id=\"brand-accent\">{css}</style>\n</head>", 1);

        return html;
    }

    public static string OpenGraph(string title, string? description, string url, string instance)
    {
        var d = WebUtility.HtmlEncode(description ?? "");
        return $"""
            <meta name="description" content="{d}" />
            <meta property="og:type" content="article" />
            <meta property="og:title" content="{WebUtility.HtmlEncode(title)}" />
            <meta property="og:description" content="{d}" />
            <meta property="og:url" content="{WebUtility.HtmlEncode(url)}" />
            <meta property="og:site_name" content="{WebUtility.HtmlEncode(instance)}" />
            """;
    }

    private static string? Excerpt(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        return t.Length <= 160 ? t : t[..157].TrimEnd() + "…";
    }

    [GeneratedRegex(@"^/spaces/(?<key>[A-Za-z0-9]+)(?:/pages/(?<page>[0-9a-fA-F-]{36}))?/?$")]
    private static partial Regex PublicPath();

    [GeneratedRegex(@"<title>[^<]*</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<link\s+rel=""icon""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex IconLink();

    [GeneratedRegex(@"<html\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"</head>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadClose();
}
