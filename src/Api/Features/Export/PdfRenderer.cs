using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Tesria.Api.Features.Export;

public interface IPdfRenderer
{
    /// <summary>Whether a renderer is configured at all — without one, PDF and single-file HTML are simply not offered.</summary>
    bool Available { get; }

    /// <summary>
    /// Captures a page of this app, as PDF bytes or as one HTML file, or null
    /// if the sidecar refused or failed (dev-plan 12.1). The url is one of the
    /// app's own render routes; the token authenticates the sidecar's browser
    /// as whoever asked for the export.
    /// </summary>
    Task<byte[]?> CaptureAsync(
        string url, string token, string format, string title, CancellationToken ct,
        bool inlineAssets = true);
}

/// <summary>
/// Calls the export sidecar (see <c>pdf/server.js</c>), which loads one of
/// this app's own render routes in a real browser and either prints it or
/// serialises its DOM (dev-plan 12.1).
///
/// The app tells it which url to load rather than handing it a document,
/// which is what lets an export be the same rendering a reader sees. The
/// sidecar will only load this app's origin, and reaches nothing else.
/// </summary>
public sealed class PdfRenderer(IHttpClientFactory http, IConfiguration config, ILogger<PdfRenderer> log) : IPdfRenderer
{
    private string? Endpoint => config["Pdf:Endpoint"];
    private string? Secret => config["Pdf:SharedSecret"];

    public bool Available => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Secret);

    public async Task<byte[]?> CaptureAsync(
        string url, string token, string format, string title, CancellationToken ct,
        bool inlineAssets = true)
    {
        if (!Available) return null;
        try
        {
            var client = http.CreateClient("pdf");
            var payload = JsonSerializer.Serialize(new { url, token, format, title, inlineAssets });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint!.TrimEnd('/')}/render")
            {
                Content = new StringContent(payload, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            };
            request.Headers.Add("X-Pdf-Secret", Secret);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Export sidecar returned {Status}", (int)response.StatusCode);
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A missing or slow sidecar must degrade to "not available",
            // never to a 500 on an export the user could have had as Markdown.
            log.LogWarning(ex, "Export sidecar unreachable");
            return null;
        }
    }
}
