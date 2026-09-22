using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tesria.Api.Infrastructure.Auth;

namespace Tesria.Api.Infrastructure.Collab;

/// <summary>
/// Where a page write came from, as far as somebody reading a highlight in
/// the editor cares (dev-plan 8.6).
/// </summary>
public enum WriteSource
{
    /// <summary>A browser session: the editor itself published. Nothing to show anyone.</summary>
    Editor,
    /// <summary>A REST call with an API token.</summary>
    Api,
    /// <summary>An MCP tool, which is to say an assistant.</summary>
    Mcp,
}

public interface ICollabNotifier
{
    /// <summary>
    /// Tells the collaboration sidecar that a page has been written, so
    /// anyone with it open sees the change as tracked edits rather than
    /// losing it on their next Update.
    ///
    /// Best effort by design: see the implementation for why a failure here
    /// must never fail the write.
    /// </summary>
    Task NotifyAsync(Guid pageId, string contentJson, WriteSource source, int version, CancellationToken ct = default);

    /// <summary>
    /// Tells the sidecar that the wiki is going read-only for a restore, or
    /// that it is over (dev-plan 9.4).
    ///
    /// An open editor holds its page's document in memory and writes it back
    /// on the next keystroke, which after a restore would put content from
    /// after the backup straight back into the restored wiki. On
    /// <c>true</c> the sidecar closes every connection, drops every document
    /// and refuses new ones; the app posts <c>false</c> at every startup,
    /// which is how a restore ends.
    ///
    /// Best effort like the write notification, and with a backstop that does
    /// not depend on it: the sidecar also watches <c>LastRestoredAt</c> and
    /// exits when it is newer than its own start.
    /// </summary>
    Task MaintenanceAsync(bool on, CancellationToken ct = default);
}

/// <summary>
/// Posts to the collaboration sidecar after a page write (dev-plan 8.6).
///
/// <para><b>Why the API tells the sidecar rather than the sidecar watching the
/// database.</b> The sidecar would have to poll, and a poll interval is a
/// window in which a human can press Update over an assistant's work. The
/// write already knows the moment it happened.</para>
///
/// <para><b>Best effort, and that is a decision, not an oversight.</b> The page
/// write is already committed by the time this runs. If the sidecar is down,
/// slow, or restarting, the correct outcome is that the write stands and the
/// reconciliation happens later: the sidecar does it on the document's next
/// load (step 3), which is the same path that covers a page nobody had open.
/// Failing the write instead would mean a healthy API refusing to save
/// because an optional live-editing service is unwell.</para>
/// </summary>
public sealed class CollabNotifier(
    IHttpClientFactory http, IConfiguration config, ILogger<CollabNotifier> log) : ICollabNotifier
{
    private string? Endpoint => config["Collab:Endpoint"];
    private string? Secret => config["Collab:Secret"];

    private bool Available => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Secret);

    public async Task NotifyAsync(
        Guid pageId, string contentJson, WriteSource source, int version, CancellationToken ct = default)
    {
        if (!Available) return;
        try
        {
            var client = http.CreateClient("collab");
            var payload = JsonSerializer.Serialize(new
            {
                contentJson,
                source = source switch
                {
                    WriteSource.Mcp => "mcp",
                    WriteSource.Api => "api",
                    _ => "editor",
                },
                version,
            });
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{Endpoint!.TrimEnd('/')}/pages/{pageId}/reconcile")
            {
                Content = new StringContent(payload, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            };
            request.Headers.Add("X-Collab-Secret", Secret);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                log.LogWarning("Collab sidecar returned {Status} for page {PageId}", (int)response.StatusCode, pageId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Deliberately swallowed. The page is saved; this only decides
            // whether an open editor finds out now or on its next load.
            log.LogWarning(ex, "Collab sidecar unreachable; page {PageId} will reconcile on next load", pageId);
        }
    }

    public async Task MaintenanceAsync(bool on, CancellationToken ct = default)
    {
        if (!Available) return;
        try
        {
            var client = http.CreateClient("collab");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint!.TrimEnd('/')}/maintenance")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { maintenance = on }),
                    Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            };
            request.Headers.Add("X-Collab-Secret", Secret);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                log.LogWarning("Collab sidecar returned {Status} for maintenance={On}", (int)response.StatusCode, on);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Swallowed for the same reason, and with a real backstop: the
            // sidecar's own sweep sees LastRestoredAt move and restarts
            // itself, so an unreachable sidecar delays this rather than
            // losing it.
            log.LogWarning(ex, "Collab sidecar unreachable; could not set maintenance={On}", on);
        }
    }
}

/// <summary>
/// Which door a write came through, decided by how the caller authenticated
/// rather than by anything the caller says about itself.
/// </summary>
public static class WriteSources
{
    /// <summary>
    /// A browser session is the editor; an API token is the API, unless it is
    /// being used against the MCP endpoint, which is an assistant.
    ///
    /// The path check is what separates the last two, because MCP
    /// authenticates with the same API tokens REST does (8.4, decision 2:
    /// a browser session is never accepted at <c>/mcp</c>). It is the
    /// endpoint that makes it MCP, so the endpoint is what is asked.
    /// </summary>
    public static WriteSource Of(HttpContext? http)
    {
        if (http is null) return WriteSource.Editor;
        var isToken = string.Equals(
            http.User.Identity?.AuthenticationType,
            ApiTokenAuthenticationDefaults.AuthenticationScheme,
            StringComparison.Ordinal);
        if (!isToken) return WriteSource.Editor;
        return http.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
            ? WriteSource.Mcp
            : WriteSource.Api;
    }

    /// <inheritdoc cref="Of(HttpContext?)"/>
    public static WriteSource Of(IHttpContextAccessor accessor) => Of(accessor.HttpContext);
}
