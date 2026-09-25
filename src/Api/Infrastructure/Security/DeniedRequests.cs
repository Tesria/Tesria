using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// What the refused requests from one address were, so a "spike of denied
/// requests" alert says what it saw (2026-09-24: an alert that
/// only said "100 denied" could not be explained from the app). Kept in
/// memory, bounded, and only for the alert's window, like the counters.
/// </summary>
public sealed partial class DeniedRequestLog
{
    private const int PerAddress = 200;
    private const int MaxAddresses = 5_000;

    public sealed record Entry(DateTimeOffset At, string Path, int Status, bool HadSession, bool HadToken, string Agent);

    public sealed record PathCount(string Path, int Count);
    public sealed record AgentCount(string Agent, int Count);

    /// <summary>The summary an alert carries: the most-refused paths, and who was asking.</summary>
    public sealed record Summary(
        IReadOnlyList<PathCount> TopPaths, int Unauthorized, int Forbidden,
        int WithSession, int WithToken, int Anonymous, IReadOnlyList<AgentCount> Browsers);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<Entry>> _byAddress = new();

    public void Record(string ip, HttpContext context, int status)
    {
        var queue = _byAddress.GetOrAdd(ip, _ => new ConcurrentQueue<Entry>());
        queue.Enqueue(new Entry(
            DateTimeOffset.UtcNow,
            Normalize(context.Request.Path.Value ?? "/"),
            status,
            HadSession: context.Request.Cookies.ContainsKey("tesria.auth"),
            HadToken: context.Request.Headers.Authorization.Count > 0,
            Agent: Browser(context.Request.Headers.UserAgent.ToString())));
        while (queue.Count > PerAddress && queue.TryDequeue(out _)) { }
        if (_byAddress.Count > MaxAddresses) Prune(SecurityThresholds.DeniedWindow);
    }

    public Summary Summarize(string ip, TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow - window;
        var entries = _byAddress.TryGetValue(ip, out var queue)
            ? queue.Where(e => e.At >= since).ToList()
            : [];
        return new Summary(
            [.. entries.GroupBy(e => e.Path).OrderByDescending(g => g.Count()).Take(5).Select(g => new PathCount(g.Key, g.Count()))],
            entries.Count(e => e.Status == StatusCodes.Status401Unauthorized),
            entries.Count(e => e.Status == StatusCodes.Status403Forbidden),
            entries.Count(e => e.HadSession),
            entries.Count(e => e.HadToken),
            entries.Count(e => !e.HadSession && !e.HadToken),
            [.. entries.GroupBy(e => e.Agent).OrderByDescending(g => g.Count()).Take(3).Select(g => new AgentCount(g.Key, g.Count()))]);
    }

    private void Prune(TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow - window;
        foreach (var (ip, queue) in _byAddress)
            if (!queue.TryPeek(out var newest) || queue.All(e => e.At < since)) _byAddress.TryRemove(ip, out _);
    }

    /// <summary>Ids and numbers in a path become placeholders, so one page's requests count as one path.</summary>
    public static string Normalize(string path)
    {
        path = Guid().Replace(path, "{id}");
        path = Number().Replace(path, "/{n}");
        return path.Length > 120 ? path[..120] : path;
    }

    /// <summary>A short, readable name for the browser or program: enough to tell devices apart, not a fingerprint.</summary>
    public static string Browser(string agent)
    {
        if (string.IsNullOrWhiteSpace(agent)) return "no browser named";
        var os = agent.Contains("Windows") ? "Windows"
            : agent.Contains("iPhone") ? "iPhone"
            : agent.Contains("iPad") ? "iPad"
            : agent.Contains("Android") ? "Android"
            : agent.Contains("Mac OS X") || agent.Contains("Macintosh") ? "macOS"
            : agent.Contains("Linux") ? "Linux"
            : null;
        var browser = agent.Contains("Edg/") ? "Edge"
            : agent.Contains("Firefox/") ? "Firefox"
            : agent.Contains("Chrome/") ? "Chrome"
            : agent.Contains("Safari/") ? "Safari"
            : null;
        if (browser is null)
        {
            var product = agent.Split(' ', 2)[0];
            return product.Length > 40 ? product[..40] : product;
        }
        return os is null ? browser : $"{browser} on {os}";
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex("/[0-9]+(?=/|$)")]
    private static partial Regex Number();
}

/// <summary>
/// Addresses that stand for many clients at once, because something in
/// front of Tesria hid the real one: by default Docker Desktop's gateway,
/// which is how every device reaches a container on a Mac or Windows
/// machine (<c>Proxy:SharedClientAddresses</c>, comma-separated, changes
/// it). An alert about such an address says so, and it cannot be blocked:
/// blocking it would lock out every device at once. Correcting the data is
/// the better answer, and <c>deploy/docker-desktop/</c> does that.
/// </summary>
public sealed class SharedClientAddresses(IConfiguration config)
{
    public const string DockerDesktopGateway = "192.168.65.1";

    private readonly IPAddress[] _addresses = (config["Proxy:SharedClientAddresses"] ?? DockerDesktopGateway)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(a => IPAddress.TryParse(a, out var ip) ? ip : null)
        .Where(ip => ip is not null)
        .Select(ip => ip!)
        .ToArray();

    public bool IsShared(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return _addresses.Any(a => a.Equals(address));
    }

    public bool IsShared(string? address) => IPAddress.TryParse(address, out var ip) && IsShared(ip);

    /// <summary>True when a block range covers a shared address.</summary>
    public bool Covers(IPNetwork network) => _addresses.Any(network.Contains);
}
