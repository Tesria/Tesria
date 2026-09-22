using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using Svg.Skia;
using Tesria.Api.Infrastructure.Storage;

namespace Tesria.Api.Infrastructure.Branding;

public sealed class BrandAssetException(string message) : Exception(message);

public sealed record StoredLogo(string Hash, string Format, int Width, int Height);
public sealed record StoredFavicon(string Hash, bool HasSvg);

/// <summary>The logo, dark logo and favicon files (dev-plan 13.1).</summary>
public interface IBrandAssets
{
    Task<StoredLogo> StoreLogoAsync(bool dark, byte[] bytes, CancellationToken ct = default);
    Task<StoredFavicon> StoreFaviconAsync(byte[] bytes, CancellationToken ct = default);

    /// <summary>
    /// A stored file by its public name (<c>logo</c>, <c>logo-dark</c>,
    /// <c>favicon.svg</c>, <c>favicon-32.png</c> …) and, for a logo, its
    /// format. Null when there is none.
    /// </summary>
    (Stream Stream, string ContentType)? Open(string name, string? format);

    void DeleteLogo(bool dark);
    void DeleteFavicon();
    void DeleteAll();
}

/// <summary>
/// Stores branding files through <see cref="IAttachmentStorage"/>, like
/// avatars, under <c>branding/</c> with fixed keys, so a replacement
/// overwrites rather than accumulating and the S3 slot that interface
/// reserves covers these too.
///
/// <para><b>Raster images are always re-encoded, SVGs always rebuilt.</b>
/// Nothing is stored as it arrived. For a raster image that strips metadata
/// (EXIF can carry a location), defeats polyglot files, and bounds decoded
/// size before a pixel is allocated, the same reasons as
/// <see cref="ProfileMediaService"/>. For an SVG it is the sanitiser's
/// rebuild. Unlike avatars the aspect ratio is kept: a logo is rarely
/// square.</para>
///
/// <para><b>The favicon set</b> is PNG at 32 (tabs), 180 (iOS home screen)
/// and 512 (install prompts). An SVG favicon is also kept as SVG and offered
/// first, because it stays sharp at every size; the PNGs are drawn from it by
/// Svg.Skia, in this process. No extra container is involved, which was the
/// owner's condition for this dependency (decision E).</para>
/// </summary>
public sealed class BrandAssets(IAttachmentStorage storage) : IBrandAssets
{
    /// <summary>Upload caps, before decoding. SVG has its own, lower one.</summary>
    public const long MaxLogoBytes = 2 * 1024 * 1024;
    public const long MaxFaviconBytes = 1024 * 1024;

    /// <summary>A stored raster logo fits within this box, keeping its shape.</summary>
    public const int MaxLogoWidth = 1024;
    public const int MaxLogoHeight = 256;

    public static readonly int[] FaviconSizes = [32, 180, 512];

    /// <summary>Same ceiling as avatars: refused from the header before any pixels are decoded.</summary>
    private const long MaxDecodedPixels = 40_000_000;

    public async Task<StoredLogo> StoreLogoAsync(bool dark, byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length == 0) throw new BrandAssetException("The file is empty.");
        var name = dark ? "logo-dark" : "logo";

        if (SvgSanitizer.LooksLikeSvg(bytes))
        {
            SvgSanitizer.Result svg;
            try { svg = SvgSanitizer.Sanitize(bytes); }
            catch (SvgRejectedException ex) { throw new BrandAssetException(ex.Message); }

            var encoded = Encoding.UTF8.GetBytes(svg.Svg);
            await SaveAsync($"branding/{name}.svg", encoded, ct);
            storage.Delete($"branding/{name}.webp");
            var (w, h) = Proportion(svg.Width, svg.Height);
            return new StoredLogo(Hash(encoded), "svg", w, h);
        }

        if (bytes.Length > MaxLogoBytes)
            throw new BrandAssetException($"A logo must be {MaxLogoBytes / (1024 * 1024)} MB or smaller.");

        using var source = Decode(bytes);
        var scale = Math.Min(1.0, Math.Min((double)MaxLogoWidth / source.Width, (double)MaxLogoHeight / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = scale < 1
            ? source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                ?? throw new BrandAssetException("The image could not be resized.")
            : source.Copy();
        var webp = EncodeWebpLossless(resized);

        await SaveAsync($"branding/{name}.webp", webp, ct);
        storage.Delete($"branding/{name}.svg");
        return new StoredLogo(Hash(webp), "webp", width, height);
    }

    public async Task<StoredFavicon> StoreFaviconAsync(byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length == 0) throw new BrandAssetException("The file is empty.");

        var pngs = new Dictionary<int, byte[]>();
        byte[]? svgBytes = null;

        if (SvgSanitizer.LooksLikeSvg(bytes))
        {
            SvgSanitizer.Result svg;
            try { svg = SvgSanitizer.Sanitize(bytes); }
            catch (SvgRejectedException ex) { throw new BrandAssetException(ex.Message); }
            svgBytes = Encoding.UTF8.GetBytes(svg.Svg);

            // Drawn from the *sanitised* document, never the upload, so the
            // renderer only ever sees markup that has already been rebuilt.
            using var renderer = new SKSvg();
            using var picture = renderer.FromSvg(svg.Svg)
                ?? throw new BrandAssetException("That SVG could not be drawn.");
            foreach (var size in FaviconSizes) pngs[size] = RenderSquare(picture, size);
        }
        else
        {
            if (bytes.Length > MaxFaviconBytes)
                throw new BrandAssetException($"A favicon must be {MaxFaviconBytes / 1024} KB or smaller.");
            using var source = Decode(bytes, allowIco: true);
            foreach (var size in FaviconSizes) pngs[size] = RenderSquare(source, size);
        }

        if (svgBytes is not null) await SaveAsync("branding/favicon.svg", svgBytes, ct);
        else storage.Delete("branding/favicon.svg");
        foreach (var (size, png) in pngs) await SaveAsync($"branding/favicon-{size}.png", png, ct);

        return new StoredFavicon(Hash(svgBytes ?? pngs[512]), svgBytes is not null);
    }

    public (Stream Stream, string ContentType)? Open(string name, string? format)
    {
        var (key, type) = name switch
        {
            "logo" or "logo-dark" when format == "svg" => ($"branding/{name}.svg", "image/svg+xml"),
            "logo" or "logo-dark" when format == "webp" => ($"branding/{name}.webp", "image/webp"),
            "favicon.svg" => ("branding/favicon.svg", "image/svg+xml"),
            "favicon-32.png" or "favicon-180.png" or "favicon-512.png" => ($"branding/{name}", "image/png"),
            _ => (null, null),
        };
        if (key is null) return null;
        return storage.OpenRead(key) is { } stream ? (stream, type!) : null;
    }

    public void DeleteLogo(bool dark)
    {
        var name = dark ? "logo-dark" : "logo";
        storage.Delete($"branding/{name}.svg");
        storage.Delete($"branding/{name}.webp");
    }

    public void DeleteFavicon()
    {
        storage.Delete("branding/favicon.svg");
        foreach (var size in FaviconSizes) storage.Delete($"branding/favicon-{size}.png");
    }

    public void DeleteAll()
    {
        DeleteLogo(dark: false);
        DeleteLogo(dark: true);
        DeleteFavicon();
    }

    private async Task SaveAsync(string key, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        await storage.SaveAsync(key, stream, ct);
    }

    /// <summary>
    /// Decodes a raster image after checking its header. GIF is refused: an
    /// animated logo is not a feature, and a still GIF has a better format.
    /// </summary>
    private static SKBitmap Decode(byte[] bytes, bool allowIco = false)
    {
        using var data = SKData.CreateCopy(bytes);
        using (var codec = SKCodec.Create(data))
        {
            if (codec is null)
                throw new BrandAssetException(allowIco
                    ? "Unrecognised image. Use SVG, PNG, ICO, JPEG or WebP."
                    : "Unrecognised image. Use SVG, PNG, JPEG or WebP.");
            if (codec.EncodedFormat == SKEncodedImageFormat.Gif)
                throw new BrandAssetException("GIF is not accepted. Use SVG, PNG or WebP.");
            if (codec.EncodedFormat == SKEncodedImageFormat.Ico && !allowIco)
                throw new BrandAssetException("An ICO file is for favicons. Use SVG, PNG, JPEG or WebP for a logo.");
            if ((long)codec.Info.Width * codec.Info.Height > MaxDecodedPixels)
                throw new BrandAssetException("The image's dimensions are too large.");
        }
        return SKBitmap.Decode(data) ?? throw new BrandAssetException("The image could not be decoded.");
    }

    private static byte[] EncodeWebpLossless(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var pixmap = image.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))
            ?? throw new BrandAssetException("The image could not be encoded.");
        return data.ToArray();
    }

    /// <summary>A raster image centred on a transparent square, fitted without distortion.</summary>
    private static byte[] RenderSquare(SKBitmap source, int size) =>
        Square(size, canvas =>
        {
            var scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            var w = source.Width * scale;
            var h = source.Height * scale;
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, SKRect.Create((size - w) / 2, (size - h) / 2, w, h),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        });

    /// <summary>A vector picture centred on a transparent square, fitted without distortion.</summary>
    private static byte[] RenderSquare(SKPicture picture, int size) =>
        Square(size, canvas =>
        {
            var bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var scale = Math.Min(size / bounds.Width, size / bounds.Height);
            canvas.Translate((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        });

    private static byte[] Square(int size, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new BrandAssetException("The favicon could not be drawn.");
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new BrandAssetException("The favicon could not be encoded.");
        return png.ToArray();
    }

    /// <summary>
    /// An SVG's intrinsic size as whole pixels, scaled into the same box a
    /// raster logo is, so the page lays either out the same way.
    /// </summary>
    private static (int W, int H) Proportion(double width, double height)
    {
        var scale = Math.Min((double)MaxLogoWidth / width, (double)MaxLogoHeight / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content))[..16];
}
