using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Which proxies the app believes about the client's address and scheme
/// (dev-plan 3.0).
///
/// The app never sees the client directly: Caddy terminates TLS and proxies
/// to Kestrel over plain HTTP on the compose network. Without this, every
/// request's <c>RemoteIpAddress</c> is Caddy's container address and
/// <c>Request.Scheme</c> is <c>http</c>, so a per-IP rate limiter would
/// throttle the whole world as one caller, and a cookie marked "secure if the
/// request was" would never be marked secure.
///
/// Trust is by network, not by proxy address: Caddy's container IP is
/// reassigned on every <c>compose up</c>, but it is always inside the private
/// ranges Docker allocates from. The default therefore trusts loopback and the
/// RFC 1918 / ULA ranges, which is safe <em>only</em> because port 8080 is
/// exposed to the compose network and never published to the host. An
/// operator who publishes it, or who fronts the app with a proxy on a public
/// address, must set <c>Proxy:TrustedNetworks</c> to exactly that proxy.
/// </summary>
public static class ProxyTrust
{
    public const string DefaultTrustedNetworks =
        "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7";

    public static void Configure(ForwardedHeadersOptions options, IConfiguration config)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Only the nearest proxy's entry is honored. Caddy appends the real
        // client address as the last X-Forwarded-For value; anything a client
        // put in the header itself sits earlier and is ignored, so a caller
        // cannot pick its own address to dodge a rate limit.
        options.ForwardLimit = 1;

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in ParseNetworks(config["Proxy:TrustedNetworks"]))
            options.KnownIPNetworks.Add(network);
    }

    public static IEnumerable<IPNetwork> ParseNetworks(string? list)
    {
        var raw = string.IsNullOrWhiteSpace(list) ? DefaultTrustedNetworks : list;
        foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPNetwork.TryParse(part, out var network))
                yield return network;
            else if (IPAddress.TryParse(part, out var address))
                yield return new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
            else
                throw new InvalidOperationException(
                    $"Proxy:TrustedNetworks contains '{part}', which is neither a CIDR range nor an IP address.");
        }
    }
}
