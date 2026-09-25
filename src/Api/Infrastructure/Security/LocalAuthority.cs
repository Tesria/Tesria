using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// The fingerprint of the certificate authority Caddy made for this server
/// (the review's SEC-01, 2026-09-24). A device about to trust that authority
/// compares its fingerprint with this one first, so the value has to reach
/// people by a route nobody on the network can alter: the app's own log on
/// the server, and the admin card on a connection that cannot be intercepted
/// (<see cref="TrustedChannel"/>). Never the setup page, which is plain HTTP.
///
/// <para>The certificate is read from Caddy over the compose network
/// (<c>Tls:AuthorityCertificateUrl</c>, <c>http://caddy/ca.crt</c> under
/// Compose), which no device on the LAN can reach, and it is only the public
/// root: the app never sees the authority's key. Without the setting (tests,
/// <c>dotnet run</c>) nothing is fetched.</para>
/// </summary>
public sealed class LocalAuthority(
    IHttpClientFactory http, IConfiguration config, ILogger<LocalAuthority> logger) : BackgroundService
{
    public const string HttpClientName = "caddy-ca";

    /// <summary>Colon-separated upper-case hex, as openssl and most certificate viewers print it.</summary>
    public sealed record Fingerprints(string Sha256, string Sha1, string Subject);

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Recheck = TimeSpan.FromHours(1);

    private volatile Fingerprints? _current;

    /// <summary>The authority's fingerprints, or null until Caddy has answered once.</summary>
    public Fingerprints? Current => _current;

    /// <summary>For tests, which have no Caddy to ask.</summary>
    public void Set(Fingerprints? value) => _current = value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = config["Tls:AuthorityCertificateUrl"];
        if (string.IsNullOrWhiteSpace(url) || !Features.Trust.TrustEndpoints.OwnCertificate(config)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = Retry;
            try
            {
                var pem = await http.CreateClient(HttpClientName).GetStringAsync(url, stoppingToken);
                var read = Read(pem);
                if (read != _current)
                {
                    _current = read;
                    // Information, and worded for someone reading the log to
                    // find it: `docker compose logs app | grep -i fingerprint`.
                    logger.LogInformation(
                        "Local certificate authority ({Subject}) SHA-256 fingerprint: {Sha256} (SHA-1: {Sha1}). "
                        + "Devices compare this with the certificate before trusting it; see /trust.",
                        read.Subject, read.Sha256, read.Sha1);
                }
                wait = Recheck;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException or ArgumentException)
            {
                // Caddy makes its authority when it first starts; until then
                // there is nothing to fetch.
                logger.LogDebug("Could not read the local certificate authority yet: {Error}", ex.Message);
            }
            try { await Task.Delay(wait, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public static Fingerprints Read(string pem)
    {
        using var cert = X509Certificate2.CreateFromPem(pem);
        return new Fingerprints(
            Format(cert.GetCertHash(HashAlgorithmName.SHA256)),
            Format(cert.GetCertHash(HashAlgorithmName.SHA1)),
            cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
    }

    public static string Format(byte[] hash) => Convert.ToHexString(hash).Chunk(2).Select(p => new string(p)).Aggregate((a, b) => a + ":" + b);
}

/// <summary>
/// Whether a request reached the app by a route nobody on the network could
/// have altered, for showing the certificate authority's fingerprint (SEC-01):
/// from the server computer itself (loopback, which under Docker Desktop
/// needs the real-address forwarder, and otherwise reads as the shared
/// gateway and does not count), or through Tailscale at its <c>ts.net</c>
/// name, whose certificate the browser has already checked. A LAN connection
/// does not count however it looks: on it, a page claiming to be this server
/// could be anyone's.
/// </summary>
public static class TrustedChannel
{
    public const string Server = "server";
    public const string Tailnet = "tailnet";

    private static readonly IPNetwork TailnetRange = IPNetwork.Parse("100.64.0.0/10");

    public static string? Of(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return Server;
        var host = context.Request.Host.Host;
        if (TailnetRange.Contains(ip) && host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)) return Tailnet;
        return null;
    }
}
