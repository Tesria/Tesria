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

