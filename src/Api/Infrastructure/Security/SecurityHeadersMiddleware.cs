using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// The response headers that tell a browser what this site will never do
/// (dev-plan 3.0).
///
/// Set here rather than in Caddy so they apply however the app is fronted,
/// and so the tests can assert on them. HSTS is the one exception: only the
/// TLS terminator knows whether the connection was actually HTTPS, and on a
/// LAN install with an untrusted internal CA an HSTS header would stop the
/// browser from letting anyone click past the certificate warning. It lives
/// in the public-hosting Caddyfile.
/// </summary>
public sealed partial class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _cspTemplate;
    private readonly string _cspHeaderName;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env, IConfiguration config)
    {
        _next = next;
        _cspTemplate = BuildPolicy(InlineScriptHashes(env));
        _cspHeaderName = config.GetValue("Security:CspReportOnly", false)
            ? "Content-Security-Policy-Report-Only"
            : "Content-Security-Policy";
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // The collaboration socket is same-origin, and CSP3 says 'self'
        // covers ws/wss on the same host — but not every browser agrees, so
        // the socket origin is spelled out per request rather than relying
        // on that.
        var socketScheme = context.Request.IsHttps ? "wss" : "ws";
        headers[_cspHeaderName] = _cspTemplate.Replace("{socket}", $"{socketScheme}://{context.Request.Host}");

        return _next(context);
    }

    private static string BuildPolicy(IEnumerable<string> scriptHashes)
    {
        var scripts = string.Join(' ', scriptHashes.Select(h => $"'sha256-{h}'"));
        return string.Join("; ", new[]
        {
            "default-src 'self'",
            // Every bundle is same-origin; the only inline script is the
            // theme bootstrap in index.html, allowed by hash rather than by
            // 'unsafe-inline' so that an injected <script> still cannot run.
            $"script-src 'self'{(scripts.Length > 0 ? " " + scripts : "")}",
            // The editor writes inline style attributes (text colour, cell
            // colours, alignment) into content it renders, and React sets
            // style attributes directly. Inline *styles* are the accepted
            // trade-off; inline *scripts* are not.
            "style-src 'self' 'unsafe-inline'",
            // Authors may paste an image by URL; data:/blob: cover pasted and
            // in-progress uploads. Plain http images are not allowed on an
            // https page anyway.
            "img-src 'self' data: blob: https:",
            "font-src 'self' data:",
            "connect-src 'self' {socket}",
            "media-src 'self' blob:",
            "worker-src 'self' blob:",
            "object-src 'none'",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
        });
    }

    /// <summary>
    /// SHA-256 of every inline script in the SPA's index.html, computed from the
    /// file this process actually serves so the hash and the markup cannot
    /// drift apart across a rebuild. No web root (tests, dev) means no hashes,
    /// which leaves script-src at 'self'.
    /// </summary>
    private static List<string> InlineScriptHashes(IWebHostEnvironment env)
    {
        var hashes = new List<string>();
        var file = env.WebRootFileProvider?.GetFileInfo("index.html");
        if (file is null || !file.Exists) return hashes;

        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        var html = reader.ReadToEnd();
        foreach (Match m in InlineScript().Matches(html))
        {
            // The hash covers the exact bytes between the tags, whitespace
            // included — that is what the browser hashes too.
            var body = Encoding.UTF8.GetBytes(m.Groups["body"].Value);
            hashes.Add(Convert.ToBase64String(SHA256.HashData(body)));
        }
        return hashes;
    }

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}
