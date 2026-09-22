using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Branding;

namespace Tesria.Api.Features.Export;

/// <summary>
/// The instance's branding, packed for an export (dev-plan 13.1, decision
/// 12). A site gets real files under <c>assets/</c>; a single HTML file gets
/// <c>data:</c> URIs, because it has nowhere else to keep them. Either way
/// the export stands on its own: nothing in it points back at the instance.
/// </summary>
public static class BrandExport
{
    /// <summary>The attributes an export's own theme script reads. The favicon painter's are the SPA's alone.</summary>
    private static readonly HashSet<string> ExportAttributes = ["data-theme-lock", "data-accent-lock", "data-accent-default"];

    public sealed record Packed(SiteChrome.Brand Brand, List<(string Path, byte[] Bytes)> Files);

    public static async Task<Packed> PackAsync(
        SiteSettings settings, IBrandAssets assets, IWebHostEnvironment env, bool inline, CancellationToken ct)
    {
        var view = BrandView.From(settings);
        var files = new List<(string, byte[])>();

        async Task<(string? Href, string Type)> Place(string name, string? format, string sitePath)
        {
            if (assets.Open(name, format) is not { } file) return (null, "");
            await using var stream = file.Stream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (inline) return ($"data:{file.ContentType};base64,{Convert.ToBase64String(bytes)}", file.ContentType);
            files.Add((sitePath, bytes));
            return (sitePath, file.ContentType);
        }

        string? logo = null, logoDark = null;
        if (view.Logo is { } l) logo = (await Place("logo", l.Format, $"assets/brand-logo.{l.Format}")).Href;
        if (view.LogoDark is { } d) logoDark = (await Place("logo-dark", d.Format, $"assets/brand-logo-dark.{d.Format}")).Href;

        // The brand's favicon, or Tesria's own. Until now an export carried
        // the page's icon link unchanged, which pointed at the instance and
        // showed nothing once the file was somewhere else.
        (string? Href, string Type) favicon = (null, "");
        if (view.FaviconHash is not null)
            favicon = view.FaviconHasSvg
                ? await Place("favicon.svg", null, "assets/favicon.svg")
                : await Place("favicon-32.png", null, "assets/favicon.png");
        else if (await TesriaFaviconAsync(env, ct) is { } tesria)
        {
            if (inline) favicon = ($"data:image/svg+xml;base64,{Convert.ToBase64String(tesria)}", "image/svg+xml");
            else
            {
                files.Add(("assets/favicon.svg", tesria));
                favicon = ("assets/favicon.svg", "image/svg+xml");
            }
        }

        var brand = new SiteChrome.Brand(view.Name, logo)
        {
            Display = view.Display,
            LogoDarkPath = logoDark,
            LogoWidth = view.Logo?.Width,
            LogoHeight = view.Logo?.Height,
            Instance = settings.InstanceName,
            FaviconHref = favicon.Href,
            FaviconType = favicon.Href is null ? "image/png" : favicon.Type,
            AccentCss = view.AccentStylesheet(),
            Attributes = view.HtmlAttributes().Where(a => ExportAttributes.Contains(a.Name)).ToList(),
        };
        return new Packed(brand, files);
    }

    private static async Task<byte[]?> TesriaFaviconAsync(IWebHostEnvironment env, CancellationToken ct)
    {
        var file = env.WebRootFileProvider?.GetFileInfo("favicon.svg");
        if (file is null || !file.Exists) return null;
        await using var stream = file.CreateReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
