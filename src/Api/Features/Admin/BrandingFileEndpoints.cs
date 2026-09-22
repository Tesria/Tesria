using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Branding;
using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Uploading and serving the branding files (dev-plan 13.1): the logo, the
/// dark-mode logo and the favicon.
/// </summary>
public static partial class BrandingEndpoints
{
    /// <summary>
    /// The policy an SVG is served under, replacing the application's own for
    /// that one response. Opened directly in a tab, an SVG is a document, and
    /// a document can run script; this makes it inert as a document too, so
    /// the file is harmless however it is reached (decision 6).
    /// </summary>
    public const string SvgPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    internal static RouteGroupBuilder MapBrandingFileEndpoints(this RouteGroupBuilder group)
    {
        group.MapPut("/logo", (IFormFile? file, HttpContext http, IConfiguration config, ISiteSettingsService s,
                IBrandAssets a, AppDbContext db, CurrentUser c, IAuditLogger audit)
                => UploadLogo(false, file, http, config, s, a, db, c, audit))
            .DisableAntiforgery();
        group.MapPut("/logo-dark", (IFormFile? file, HttpContext http, IConfiguration config, ISiteSettingsService s,
                IBrandAssets a, AppDbContext db, CurrentUser c, IAuditLogger audit)
                => UploadLogo(true, file, http, config, s, a, db, c, audit))
            .DisableAntiforgery();
        group.MapDelete("/logo", (HttpContext http, IConfiguration config, ISiteSettingsService s,
                IBrandAssets a, AppDbContext db, CurrentUser c, IAuditLogger audit)
                => RemoveLogo(false, http, config, s, a, db, c, audit));
        group.MapDelete("/logo-dark", (HttpContext http, IConfiguration config, ISiteSettingsService s,
                IBrandAssets a, AppDbContext db, CurrentUser c, IAuditLogger audit)
                => RemoveLogo(true, http, config, s, a, db, c, audit));
        group.MapPut("/favicon", UploadFavicon).DisableAntiforgery();
        group.MapDelete("/favicon", RemoveFavicon);
        return group;
    }

    /// <summary>
    /// The files themselves, anonymous: the sign-in page shows the logo, and
    /// every tab shows the favicon, before anyone has signed in. The URL
    /// carries the content hash, so the response can be cached for good.
    /// </summary>
    public static IEndpointRouteBuilder MapBrandingAssetEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/branding").AllowAnonymous().ExcludeFromDescription();
        group.MapGet("/logo", (HttpContext http, ISiteSettingsService s, IBrandAssets a) => Serve(http, s, a, "logo"));
        group.MapGet("/logo-dark", (HttpContext http, ISiteSettingsService s, IBrandAssets a) => Serve(http, s, a, "logo-dark"));
        group.MapGet("/favicon.svg", (HttpContext http, ISiteSettingsService s, IBrandAssets a) => Serve(http, s, a, "favicon.svg"));
        group.MapGet("/favicon-{size:int}.png", (int size, HttpContext http, ISiteSettingsService s, IBrandAssets a) =>
            BrandAssets.FaviconSizes.Contains(size) ? Serve(http, s, a, $"favicon-{size}.png") : Task.FromResult(Results.NotFound()));
        return routes;
    }

    private static async Task<IResult> Serve(HttpContext http, ISiteSettingsService settings, IBrandAssets assets, string name)
    {
        var s = await settings.GetAsync();
        var format = name switch
        {
            "logo" => s.BrandLogoFormat,
            "logo-dark" => s.BrandLogoDarkFormat,
            _ => null,
        };
        var present = name switch
        {
            "logo" => s.BrandLogoHash is not null,
            "logo-dark" => s.BrandLogoDarkHash is not null,
            "favicon.svg" => s.BrandFaviconHash is not null && s.BrandFaviconHasSvg,
            _ => s.BrandFaviconHash is not null,
        };
        if (!present || assets.Open(name, format) is not { } file) return Results.NotFound();

        var headers = http.Response.Headers;
        headers.CacheControl = "public, max-age=31536000, immutable";
        if (file.ContentType == "image/svg+xml")
        {
            headers.ContentSecurityPolicy = SvgPolicy;
            headers.XContentTypeOptions = "nosniff";
        }
        return Results.Stream(file.Stream, file.ContentType, enableRangeProcessing: false);
    }

    private static async Task<byte[]?> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;
        // One ceiling for every branding upload, checked before it is read:
        // the per-kind limits in BrandAssets are all below it.
        if (file.Length > BrandAssets.MaxLogoBytes) return [];
        using var buffer = new MemoryStream();
        await using var source = file.OpenReadStream();
        await source.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static IResult FileError(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

    private static async Task<IResult> UploadLogo(
        bool dark, IFormFile? file, HttpContext http, IConfiguration config, ISiteSettingsService settings,
        IBrandAssets assets, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        var bytes = await ReadAsync(file, http.RequestAborted);
        if (bytes is null) return FileError("Choose an image file.");
        if (bytes.Length == 0) return FileError($"A logo must be {BrandAssets.MaxLogoBytes / (1024 * 1024)} MB or smaller.");

        StoredLogo stored;
        try { stored = await assets.StoreLogoAsync(dark, bytes, http.RequestAborted); }
        catch (BrandAssetException ex) { return FileError(ex.Message); }

        var actorId = current.RequireId();
        var saved = await settings.UpdateAsync(s =>
        {
            if (dark)
            {
                s.BrandLogoDarkHash = stored.Hash; s.BrandLogoDarkFormat = stored.Format;
                s.BrandLogoDarkWidth = stored.Width; s.BrandLogoDarkHeight = stored.Height;
            }
            else
            {
                s.BrandLogoHash = stored.Hash; s.BrandLogoFormat = stored.Format;
                s.BrandLogoWidth = stored.Width; s.BrandLogoHeight = stored.Height;
            }
            s.BrandChangedAt = DateTimeOffset.UtcNow;
            s.BrandChangedById = actorId;
        }, actorId);

        audit.Record("branding.logo_uploaded", "instance", null,
            new { Dark = dark, stored.Hash, stored.Format, stored.Width, stored.Height });
        await db.SaveChangesAsync();
        return Results.Ok(await ViewAsync(saved, db));
    }

    private static async Task<IResult> RemoveLogo(
        bool dark, HttpContext http, IConfiguration config, ISiteSettingsService settings,
        IBrandAssets assets, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        var actorId = current.RequireId();
        var saved = await settings.UpdateAsync(s =>
        {
            if (dark) { s.BrandLogoDarkHash = null; s.BrandLogoDarkFormat = null; s.BrandLogoDarkWidth = null; s.BrandLogoDarkHeight = null; }
            else { s.BrandLogoHash = null; s.BrandLogoFormat = null; s.BrandLogoWidth = null; s.BrandLogoHeight = null; }
            s.BrandChangedAt = DateTimeOffset.UtcNow;
            s.BrandChangedById = actorId;
        }, actorId);
        assets.DeleteLogo(dark);
        audit.Record("branding.logo_removed", "instance", null, new { Dark = dark });
        await db.SaveChangesAsync();
        return Results.Ok(await ViewAsync(saved, db));
    }

    private static async Task<IResult> UploadFavicon(
        IFormFile? file, HttpContext http, IConfiguration config, ISiteSettingsService settings,
        IBrandAssets assets, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        var bytes = await ReadAsync(file, http.RequestAborted);
        if (bytes is null) return FileError("Choose an image file.");
        if (bytes.Length == 0) return FileError($"A favicon must be {BrandAssets.MaxFaviconBytes / 1024} KB or smaller.");

        StoredFavicon stored;
        try { stored = await assets.StoreFaviconAsync(bytes, http.RequestAborted); }
        catch (BrandAssetException ex) { return FileError(ex.Message); }

        var actorId = current.RequireId();
        var saved = await settings.UpdateAsync(s =>
        {
            s.BrandFaviconHash = stored.Hash;
            s.BrandFaviconHasSvg = stored.HasSvg;
            s.BrandChangedAt = DateTimeOffset.UtcNow;
            s.BrandChangedById = actorId;
        }, actorId);
        audit.Record("branding.favicon_uploaded", "instance", null, new { stored.Hash, stored.HasSvg });
        await db.SaveChangesAsync();
        return Results.Ok(await ViewAsync(saved, db));
    }

    private static async Task<IResult> RemoveFavicon(
        HttpContext http, IConfiguration config, ISiteSettingsService settings,
        IBrandAssets assets, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        var actorId = current.RequireId();
        var saved = await settings.UpdateAsync(s =>
        {
            s.BrandFaviconHash = null;
            s.BrandFaviconHasSvg = false;
            s.BrandChangedAt = DateTimeOffset.UtcNow;
            s.BrandChangedById = actorId;
        }, actorId);
        assets.DeleteFavicon();
        audit.Record("branding.favicon_removed", "instance", null);
        await db.SaveChangesAsync();
        return Results.Ok(await ViewAsync(saved, db));
    }
}
