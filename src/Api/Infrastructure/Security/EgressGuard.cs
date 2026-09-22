using System.Net;
using System.Net.Sockets;
using IPNetwork = System.Net.IPNetwork;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>Thrown when an outbound request would reach an address this server must not contact.</summary>
public sealed class EgressBlockedException(string message) : Exception(message);

/// <summary>
/// The rule for every outbound HTTP request the server makes on a user's
/// behalf (dev-plan 3.4): webhooks today, link previews later.
///
/// Server-side request forgery is the attack: an editor points a webhook at
/// <c>http://169.254.169.254/</c> or <c>http://db:5432/</c> and the server,
/// which can reach those, fetches them. The defence has to hold at two
/// moments (when the URL is saved, and when the connection is made)
/// because a hostname can resolve to a public address at save time and a
/// private one at delivery time (DNS rebinding). So <see cref="ValidateAsync"/>
/// checks the URL and its current resolution, and <see cref="CreateHandler"/>
/// checks again inside the socket connect, on the exact address about to be
/// dialled. Redirects are followed by hand so each hop gets the same check.
///
/// <c>Egress:AllowedNetworks</c> lists private ranges an operator has
/// deliberately opened (a LAN automation server, say). The 3.3 detector
/// still records an event for those.
/// </summary>
public sealed class EgressGuard
{
    public const int MaxRedirects = 3;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly List<IPNetwork> _allowed;

    public EgressGuard(IConfiguration config)
    {
        var raw = config["Egress:AllowedNetworks"];
        _allowed = string.IsNullOrWhiteSpace(raw) ? [] : ProxyTrust.ParseNetworks(raw).ToList();
    }

    /// <summary>Whether an address may be contacted: public, or explicitly allowed.</summary>
    public bool IsAllowed(IPAddress address)
    {
        var a = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (_allowed.Any(n => n.BaseAddress.AddressFamily == a.AddressFamily && n.Contains(a))) return true;
        return !PrivateNetworks.IsPrivateOrLocal(a);
    }

    /// <summary>
    /// Rejects a URL that is not plain http(s), carries credentials, names a
    /// local host, or resolves (now) to an address this server must not reach.
    /// Returns the reason, or null when it is acceptable.
    /// </summary>
    public async Task<string?> ValidateAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "Not a valid absolute URL.";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "Only http:// and https:// are allowed.";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "Credentials in the URL are not allowed.";
        if (uri.HostNameType == UriHostNameType.Basic || uri.Host.Length == 0) return "A host name is required.";
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return "Local host names are not allowed.";

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
            return IsAllowed(literal) ? null : "That address is inside a private or reserved range.";

        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (SocketException)
        {
            return "The host name does not resolve.";
        }
        if (resolved.Length == 0) return "The host name does not resolve.";
        // Every address must be acceptable: a name with one public and one
        // private record would otherwise be a coin toss at connect time.
        return resolved.All(IsAllowed) ? null : "The host name resolves to a private or reserved address.";
    }

    /// <summary>
    /// An HTTP handler that re-checks the destination address inside the
    /// connect, so a name that changed its mind since validation is refused
    /// on the wire. No automatic redirects: <see cref="SendAsync"/> follows
    /// them with the same check per hop.
    /// </summary>
    public SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = Timeout,
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);

            var target = addresses.FirstOrDefault()
                ?? throw new EgressBlockedException($"{host} does not resolve.");
            if (!addresses.All(IsAllowed))
                throw new EgressBlockedException($"{host} resolves to a private or reserved address.");

            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>Sends, following at most <see cref="MaxRedirects"/> redirects, validating each destination.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpClient client, Func<Uri, HttpRequestMessage> build, Uri target, CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            if (await ValidateAsync(target.ToString(), ct) is { } reason)
                throw new EgressBlockedException($"{target}: {reason}");

            using var request = build(target);
            var response = await client.SendAsync(request, ct);
            if ((int)response.StatusCode is < 300 or > 399 || response.Headers.Location is null)
                return response;

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(target, response.Headers.Location);
            response.Dispose();

            if (hop >= MaxRedirects)
                throw new EgressBlockedException($"{target}: too many redirects.");
            target = next;
        }
    }
}
