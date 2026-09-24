using System.Text.Json;
using Tesria.Api.Infrastructure.Permissions;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// The optional Tailscale sidecar's status, for its card in Administration
/// (dev-plan 19.1). The sidecar writes <c>tailscale status --json
/// --peers=false</c> to a shared volume every thirty seconds as its health
/// check; the app only reads that file, and never holds Tailscale's control
/// socket, which would let it change the tailnet.
/// </summary>
public static class TailscaleEndpoints
{
    /// <summary>
    /// <c>Configured</c> is false when no status has ever been written: the
    /// sidecar is not in use. <c>Stale</c> means the file stopped being
    /// refreshed, so the sidecar has stopped. <c>KeyExpiry</c> is when the
    /// device must sign in again, null once key expiry is turned off for it.
    /// </summary>
    public record TailscaleStatus(
        bool Configured, string? State, bool Online, string? Address,
        DateTimeOffset? KeyExpiry, string? Version, DateTimeOffset? CheckedAt, bool Stale);

    /// <summary>Three missed reports.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    public static IEndpointRouteBuilder MapTailscaleEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/admin/tailscale", Get).WithTags("Admin").RequireAuthorization()
            .RequirePermission(InstancePermissions.SettingsInstance);
        return routes;
    }

    private static IResult Get(IConfiguration config) => Results.Ok(Read(config["Tailscale:StatusFile"]));

    public static TailscaleStatus Read(string? path)
    {
        var none = new TailscaleStatus(false, null, false, null, null, null, null, false);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return none;
        try
        {
            var checkedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var self = root.TryGetProperty("Self", out var s) ? s : default;
            string? Str(JsonElement e, string name) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var dns = Str(self, "DNSName")?.TrimEnd('.');
            var online = self.ValueKind == JsonValueKind.Object && self.TryGetProperty("Online", out var o) && o.ValueKind == JsonValueKind.True;
            DateTimeOffset? keyExpiry = DateTimeOffset.TryParse(Str(self, "KeyExpiry"), out var k) ? k : null;
            // "NeedsLogin" with an AuthURL means the auth key was missing,
            // used up or expired before the device first joined.
            var state = Str(root, "BackendState");
            return new TailscaleStatus(
                true, state, online,
                string.IsNullOrEmpty(dns) ? null : $"https://{dns}",
                keyExpiry, Str(root, "Version")?.Split('-')[0], checkedAt,
                DateTimeOffset.UtcNow - checkedAt > StaleAfter);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return none with { Configured = true, State = "Unreadable" };
        }
    }
}
