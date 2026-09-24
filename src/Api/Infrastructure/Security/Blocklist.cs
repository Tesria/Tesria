using System.Net;
using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;
using IPNetwork = System.Net.IPNetwork;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// The in-process copy of <see cref="BlockedNetwork"/> the middleware matches
/// against. Reloaded when the list changes and every minute regardless, so a
/// second replica (or a row edited by hand) is picked up within that bound.
/// </summary>
public sealed class BlocklistCache(IServiceScopeFactory scopes)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private List<(IPNetwork Network, DateTimeOffset? ExpiresAt)> _entries = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private long _blockedHits;

    public long BlockedHits => Interlocked.Read(ref _blockedHits);

    public bool IsBlocked(IPAddress address)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _loadedAt > Ttl) Reload();

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        List<(IPNetwork, DateTimeOffset?)> snapshot;
        lock (_gate) snapshot = _entries;

        foreach (var (network, expires) in snapshot)
        {
            if (expires is { } until && until <= now) continue;
            if (network.BaseAddress.AddressFamily != address.AddressFamily) continue;
            if (network.Contains(address))
            {
                Interlocked.Increment(ref _blockedHits);
                return true;
            }
        }
        return false;
    }

    public void Reload()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.BlockedNetworks.AsNoTracking().ToList();
        var now = DateTimeOffset.UtcNow;

        var parsed = new List<(IPNetwork, DateTimeOffset?)>();
        foreach (var row in rows)
        {
            if (row.ExpiresAt is { } until && until <= now) continue;
            if (TryParseCidr(row.Cidr, out var network)) parsed.Add((network, row.ExpiresAt));
        }

        lock (_gate)
        {
            _entries = parsed;
            _loadedAt = now;
        }
    }

    /// <summary>Accepts CIDR or a bare address (stored as a host route).</summary>
    public static bool TryParseCidr(string value, out IPNetwork network)
    {
        value = (value ?? "").Trim();
        if (IPNetwork.TryParse(value, out network)) return true;
        if (IPAddress.TryParse(value, out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            network = new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }
        network = default;
        return false;
    }
}

/// <summary>
/// Refuses blocked addresses before authentication runs: a blocked caller
/// gets 403 and costs nothing further, cookie or not. Sits right after
/// forwarded headers so it sees the real client address.
/// </summary>
public sealed class BlocklistMiddleware(RequestDelegate next, BlocklistCache blocklist)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is not null && blocklist.IsBlocked(address))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"title":"Forbidden","status":403,"detail":"This address is blocked."}""");
            return;
        }
        await next(context);
    }
}

/// <summary>
/// Counts 401/403 responses per address and raises one alert when the count
/// crosses the threshold, saying what was refused (<see cref="DeniedRequestLog"/>).
/// Runs its detector in its own scope because by the time the status is
/// known the request's own unit of work is finished.
/// </summary>
public sealed class DeniedResponseMiddleware(
    RequestDelegate next, SecurityCounters counters, DeniedRequestLog log, SharedClientAddresses shared,
    IServiceScopeFactory scopes)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        var status = context.Response.StatusCode;
        if (status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)) return;
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var ip = address.ToString();

        log.Record(ip, context, status);
        var count = counters.Hit("http.denied_spike", ip, SecurityThresholds.DeniedWindow);
        if (count != SecurityThresholds.DeniedResponsesPerAddress) return;

        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISecurityDetector>().DeniedSpikeAsync(
                ip, count, log.Summarize(ip, SecurityThresholds.DeniedWindow), shared.IsShared(address));
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        }
        catch (Exception)
        {
            // Detection must never fail a response that has already been sent.
        }
    }
}
