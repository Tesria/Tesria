using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Public;

/// <summary>
/// Discovery for public spaces (dev-plan 5.2): robots.txt, sitemap.xml, and
/// the SPA shell with the page's title and Open Graph tags injected for a
/// public page URL. Everything here answers as the anonymous principal
/// whoever asks, so a private title never leaks into a shared link preview.
/// </summary>
public static partial class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/robots.txt", Robots).AllowAnonymous().ExcludeFromDescription();
        routes.MapGet("/sitemap.xml", Sitemap).AllowAnonymous().ExcludeFromDescription();
        return routes;
    }

    private static async Task<IResult> Robots(AppDbContext db, ISiteSettingsService settings, IConfiguration config)
    {
        var s = await settings.GetAsync();
        var sb = new StringBuilder("User-agent: *\n");
        var keys = s.AllowPublicSpaces
            ? await db.Spaces.AsNoTracking().Where(x => x.IsPublic && !x.Archived).Select(x => x.Key).OrderBy(k => k).ToListAsync()
            : [];
        foreach (var key in keys) sb.Append($"Allow: /spaces/{key}\n");
        sb.Append("Disallow: /api/\nDisallow: /\n");
        if (keys.Count > 0) sb.Append($"Sitemap: {SiteUrl.Resolve(s, config)}/sitemap.xml\n");
        return Results.Text(sb.ToString(), "text/plain");
    }

    private static async Task<IResult> Sitemap(AppDbContext db, IPermissionService perms, ISiteSettingsService settings, IConfiguration config)
    {
        var s = await settings.GetAsync();
        var baseUrl = SiteUrl.Resolve(s, config);
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        if (s.AllowPublicSpaces)
        {
            var pages = await db.Pages.AsNoTracking()
                .Where(p => p.Space!.IsPublic && !p.Space.Archived && p.Status == PageStatus.Current)
                .Select(p => new { p.Id, SpaceKey = p.Space!.Key, p.UpdatedAt })
                .ToListAsync();
            foreach (var key in pages.Select(p => p.SpaceKey).Distinct().OrderBy(k => k))
                sb.Append($"  <url><loc>{baseUrl}/spaces/{key}</loc></url>\n");
            foreach (var p in pages)
            {
                // Restrictions are per page; the query above cannot see them.
                if (!await perms.IsPubliclyViewablePageAsync(p.Id)) continue;
                sb.Append($"  <url><loc>{baseUrl}/spaces/{p.SpaceKey}/pages/{p.Id}</loc><lastmod>{p.UpdatedAt:yyyy-MM-dd}</lastmod></url>\n");
            }
        }
        sb.Append("</urlset>\n");
        return Results.Text(sb.ToString(), "application/xml");
    }
}

/// <summary>
/// Serves the SPA shell for a public page or space URL with its title,
/// description and Open Graph tags in place of the generic ones: enough
/// for link previews and search titles, without server-side rendering.
/// Decides through the anonymous check, whoever is asking.
/// </summary>
public sealed partial class PublicMetaMiddleware(RequestDelegate next, IWebHostEnvironment env, IServiceScopeFactory scopes, IConfiguration config)
{
    private string? _shell;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var m = PublicPath().Match(context.Request.Path.Value ?? "");
        if (!m.Success) { await next(context); return; }

        var shell = Shell();
        if (shell is null) { await next(context); return; }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var perms = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var settings = await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().GetAsync();

        string? title = null, description = null;
        var key = m.Groups["key"].Value.ToUpperInvariant();
        if (m.Groups["page"].Success && Guid.TryParse(m.Groups["page"].Value, out var pageId))
        {
            if (await perms.IsPubliclyViewablePageAsync(pageId))
            {
                var page = await db.Pages.AsNoTracking().Where(p => p.Id == pageId)
                    .Select(p => new { p.Title, p.SearchText, SpaceName = p.Space!.Name }).FirstOrDefaultAsync();
                if (page is not null)
                {
                    title = $"{page.Title} · {page.SpaceName}";
                    description = Excerpt(page.SearchText);
                }
            }
        }
        else
        {
            var space = await db.Spaces.AsNoTracking().Where(x => x.Key == key).Select(x => new { x.Id, x.Name, x.Description }).FirstOrDefaultAsync();
            if (space is not null && await perms.IsPubliclyViewableSpaceAsync(space.Id))
            {
                title = space.Name;
                description = space.Description;
            }
        }

        if (title is null) { await next(context); return; }

        var url = $"{SiteUrl.Resolve(settings, config)}{context.Request.Path}";
        var html = Inject(shell, settings.InstanceName, title, description, url);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(html);
    }

    /// <summary>The built SPA's index.html, read once. Null when there is no web root (tests, dev).</summary>
    private string? Shell()
    {
        if (_shell is not null) return _shell;
        var file = env.WebRootFileProvider?.GetFileInfo("index.html");
        if (file is null || !file.Exists) return null;
        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        return _shell = reader.ReadToEnd();
    }

    public static string Inject(string shell, string instance, string title, string? description, string url)
    {
        var t = WebUtility.HtmlEncode($"{title} · {instance}");
        var d = WebUtility.HtmlEncode(description ?? "");
        var tags = $"""
            <meta name="description" content="{d}" />
            <meta property="og:type" content="article" />
            <meta property="og:title" content="{WebUtility.HtmlEncode(title)}" />
            <meta property="og:description" content="{d}" />
            <meta property="og:url" content="{WebUtility.HtmlEncode(url)}" />
            <meta property="og:site_name" content="{WebUtility.HtmlEncode(instance)}" />
            """;
        var replaced = TitleTag().Replace(shell, $"<title>{t}</title>\n{tags}", 1);
        return ReferenceEquals(replaced, shell) ? shell : replaced;
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
}
