using System.Net.Http.Headers;
using System.Text;

namespace Tesria.Api.Features.Export;

public interface IPdfRenderer
{
    /// <summary>Whether a renderer is configured at all — without one, PDF is simply not an offered format.</summary>
    bool Available { get; }

    /// <summary>Renders a complete, self-contained HTML document to PDF bytes, or null if the sidecar refused or failed.</summary>
    Task<byte[]?> RenderAsync(string html, CancellationToken ct);
}

/// <summary>
/// Calls the PDF sidecar (see <c>pdf/server.js</c>). The sidecar runs a real
/// browser with its network switched off, so the HTML it is handed must
/// already be self-contained — which is exactly what the HTML export is
/// (images inlined by <see cref="InlineAssets"/>, diagrams carrying their
/// own renderer).
/// </summary>
public sealed class PdfRenderer(IHttpClientFactory http, IConfiguration config, ILogger<PdfRenderer> log) : IPdfRenderer
{
    private string? Endpoint => config["Pdf:Endpoint"];
    private string? Secret => config["Pdf:SharedSecret"];

    public bool Available => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Secret);

    public async Task<byte[]?> RenderAsync(string html, CancellationToken ct)
    {
        if (!Available) return null;
        try
        {
            var client = http.CreateClient("pdf");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint!.TrimEnd('/')}/render")
            {
                Content = new StringContent(html, Encoding.UTF8, new MediaTypeHeaderValue("text/html")),
            };
            request.Headers.Add("X-Pdf-Secret", Secret);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("PDF sidecar returned {Status}", (int)response.StatusCode);
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A missing or slow sidecar must degrade to "PDF unavailable",
            // never to a 500 on an export the user could have had as HTML.
            log.LogWarning(ex, "PDF sidecar unreachable");
            return null;
        }
    }
}
