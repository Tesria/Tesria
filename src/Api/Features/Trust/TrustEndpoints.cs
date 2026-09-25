using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Features.Trust;

/// <summary>
/// "Trust this device" (dev-plan 15.5): a page that walks someone through
/// trusting this server's own certificate authority.
///
/// <para>Since 14.4 (the review's SEC-01) every route to trusting it goes
/// through a fingerprint the person gets from the server itself, never from
/// this page: the page reaches them over plain HTTP, so on a network someone
/// else controls, the page, a script it served and the certificate could all
/// be someone else's. It therefore serves no scripts (they come from the
/// GitHub release), shows no fingerprint, and points to the same steps in
/// the docs over HTTPS, which win if the two ever differ.</para>
///
/// <para>Served over plain HTTP as well as HTTPS, on purpose: a device that
/// trusts nothing yet can open <c>http://server/trust</c> without a warning,
/// the same reason Caddy serves <c>/ca.crt</c> on port 80. So the page is
/// complete in itself: no application script, no session, no API call that
/// would be redirected to an HTTPS address the browser does not trust yet.
/// Its script and stylesheet are separate same-origin files, which keeps the
/// Content Security Policy as it is.</para>
///
/// <para>The address it fills into commands is the one the page was
/// reached at, checked against <see cref="Address"/> first: letters, digits,
/// dots and hyphens, or an IPv4 address.</para>
/// </summary>
public static partial class TrustEndpoints
{
    /// <summary>The same steps over HTTPS, which the page defers to.</summary>
    public const string DocsUrl = "https://tesria.com/docs/installation-and-operations/trusting-the-local-certificate/";

    /// <summary>Where the scripts come from: the latest release, over HTTPS.</summary>
    public const string ReleaseDownload = "https://github.com/Tesria/Tesria/releases/latest/download/";

    public static IEndpointRouteBuilder MapTrustEndpoints(this IEndpointRouteBuilder routes)
    {
        var trust = routes.MapGroup("/trust").AllowAnonymous().ExcludeFromDescription();
        trust.MapGet("/", Page);
        trust.MapGet("/app.css", AppCss);
        trust.MapGet("/trust.css", () => Asset("trust/trust.css", "text/css"));
        trust.MapGet("/trust.js", () => Asset("trust/trust.js", "text/javascript"));
        return routes;
    }

    /// <summary>
    /// Whether this server uses its own certificate, which is the default
    /// Caddyfile. The public one gets a real certificate, and then there is
    /// nothing to trust. Compose passes the Caddyfile in use as
    /// <c>Tls:Caddyfile</c>, so this needs no setting of its own.
    /// </summary>
    public static bool OwnCertificate(IConfiguration config) =>
        !(config["Tls:Caddyfile"] ?? "").EndsWith(".public", StringComparison.OrdinalIgnoreCase);

    /// <summary>A host name, or an IPv4 address. No port, no scheme, no path.</summary>
    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$")]
    internal static partial Regex Address();

    private static IResult Asset(string resource, string contentType) =>
        Results.Text(Read(resource), contentType + "; charset=utf-8");

    private static async Task<IResult> AppCss(IWebHostEnvironment env, CancellationToken ct) =>
        Results.Text(await Export.SiteExportEndpoints.StylesheetAsync(env, ct), "text/css; charset=utf-8");

    private static string Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"missing embedded resource {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<IResult> Page(HttpContext context, ISiteSettingsService settings, IConfiguration config)
    {
        var s = await settings.GetAsync(context.RequestAborted);
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s.InstanceName) ? "Tesria" : s.InstanceName);
        var host = context.Request.Host.Host.ToLowerInvariant();
        var address = Address().IsMatch(host) ? host : "";
        context.Response.Headers.CacheControl = "no-cache";
        var body = OwnCertificate(config) ? TrustBody(address, ServerName(s, config)) : NothingToDo(address);
        return Results.Content(Shell(name, body), "text/html; charset=utf-8");
    }

    private static string Shell(string name, string body) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <meta name="robots" content="noindex" />
        <title>Trust this device · {{name}}</title>
        <link rel="stylesheet" href="/trust/app.css" />
        <link rel="stylesheet" href="/trust/trust.css" />
        <script src="/trust/trust.js" defer></script>
        </head>
        <body>
        <header class="trust-top"><span class="brand__mark">{{BrandMark}}</span><span class="trust-top__name">{{name}}</span></header>
        <main class="trust">
        {{body}}
        </main>
        </body>
        </html>
        """;

    private const string BrandMark = """<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3l8 4.5-8 4.5-8-4.5L12 3z" /><path d="M4 12l8 4.5 8-4.5M4 16.5L12 21l8-4.5" /></svg>""";

    /// <summary>
    /// The commands the page shows, as templates its script fills in as the
    /// address and fingerprint are typed: <c>{address}</c>, <c>{fingerprint}</c>
    /// (colon pairs) and <c>{hex}</c> (bare). Rendered once here, with
    /// placeholders, for a page read without its script.
    /// </summary>
    internal const string MacLinuxCommand =
        "curl -fsSLO " + ReleaseDownload + "trust-ca.sh && bash trust-ca.sh --fingerprint {fingerprint} {address}";

    /// <summary>
    /// Windows, as one typed line: typed commands are not subject to
    /// PowerShell's execution policy, which blocks a downloaded .ps1 by
    /// default and can be locked by an employer (2026-09-23), and
    /// CurrentUser\Root needs no administrator. It computes the certificate's
    /// SHA-256 itself (Windows PowerShell 5.1 only offers SHA-1) and imports
    /// nothing unless it matches.
    /// </summary>
    internal const string WindowsLine =
        "$c = \"$env:TEMP\\tesria-ca.crt\"; Invoke-WebRequest -UseBasicParsing -Uri \"http://{address}/ca.crt\" -OutFile $c; "
        + "$x = New-Object Security.Cryptography.X509Certificates.X509Certificate2($c); "
        + "$h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($x.RawData)) -replace '-', ''; "
        + "if ($h -eq \"{hex}\") { Import-Certificate -FilePath $c -CertStoreLocation Cert:\\CurrentUser\\Root } "
        + "else { Write-Host \"The certificate does not match the fingerprint. Nothing was trusted.\" -ForegroundColor Red }";

    /// <summary>Windows, for everyone on the computer: the script, from the release.</summary>
    internal const string WindowsScript =
        "Invoke-WebRequest -UseBasicParsing " + ReleaseDownload + "trust-ca.ps1 -OutFile \"$env:TEMP\\trust-ca.ps1\"; "
        + "powershell -ExecutionPolicy Bypass -File \"$env:TEMP\\trust-ca.ps1\" {address} -Fingerprint {hex}";

    /// <summary>A command box: the template for the script, and a first rendering with placeholders.</summary>
    internal static string Command(string template, string address) =>
        $$"""<div class="trust-cmd"><code data-template="{{WebUtility.HtmlEncode(template)}}">{{WebUtility.HtmlEncode(Fill(template, address))}}</code><button type="button" class="btn btn--sm" data-copy>Copy</button></div>""";

    internal static string Fill(string template, string address) => template
        .Replace("{address}", address.Length == 0 ? "your-server" : address)
        .Replace("{fingerprint}", "PASTE-THE-FINGERPRINT")
        .Replace("{hex}", "PASTE-THE-FINGERPRINT");

    /// <summary>
    /// The server's own name, from Admin → Settings → Public address, when it
    /// is one a certificate can be issued for: not an IP address and not
    /// localhost, which no other device can reach.
    /// </summary>
    private static string? ServerName(Domain.SiteSettings s, IConfiguration config)
    {
        if (!Uri.TryCreate(Infrastructure.Email.SiteUrl.Resolve(s, config), UriKind.Absolute, out var url)) return null;
        var host = url.Host.ToLowerInvariant();
        return Address().IsMatch(host) && !IPAddress.TryParse(host, out _) && host != "localhost" ? host : null;
    }

    /// <summary>Where to find the name to use instead of a number.</summary>
    private static string NameHelp(string? serverName) => serverName is not null
        ? $$"""<p>This server's name is <strong>{{WebUtility.HtmlEncode(serverName)}}</strong>. When you have finished, open <a href="https://{{WebUtility.HtmlEncode(serverName)}}/">https://{{WebUtility.HtmlEncode(serverName)}}</a>, and bookmark it.</p>"""
        : """
          <p>The name is the server computer's name followed by <code>.local</code>, on most home and office networks. On the server computer:</p>
          <ul>
            <li><strong>Mac:</strong> System Settings, General, Sharing. It is under "Local hostname", for example <code>studio.local</code>.</li>
            <li><strong>Windows:</strong> Settings, System, About. The "Device name", followed by <code>.local</code>.</li>
            <li><strong>Linux:</strong> run <code>hostname</code> in a terminal, and add <code>.local</code>.</li>
          </ul>
          <p>An administrator can put it in Admin, Settings, Public address, and this page will name it for everyone.</p>
          """;

    private static string NothingToDo(string address) => $$"""
        <h1>Nothing to set up</h1>
        <p class="trust-lead">This server uses a certificate from a public certificate authority, the same kind every website uses, so every device already trusts it.</p>
        <p><a class="btn btn--primary" href="https://{{address}}/">Open Tesria</a></p>
        """;

    private static string TrustBody(string address, string? serverName) => $$"""
        <h1>Trust this server on your device</h1>
        <p class="trust-lead">A few minutes, once per device, and your browser stops warning you about this server.</p>

        <div class="alert alert--warning trust-channel">
        <p><strong>This page reached you over a connection that is not protected yet,</strong> so on a network someone else controls, it could have been changed on the way. The same steps are in the <a href="{{DocsUrl}}">Tesria docs</a>, over a protected connection. If this page and the docs ever differ, follow the docs.</p>
        </div>

        <details class="trust-why">
        <summary>Why am I seeing a security warning?</summary>
        <p>Browsers keep web traffic private with <strong>certificates</strong>: a certificate proves to your browser that it is talking to the real server and not an impostor. Public websites get theirs from certificate authorities every browser already trusts.</p>
        <p>A Tesria server on your own network cannot get one of those, so it becomes its own certificate authority. Your browser has never heard of it, so it warns you. The connection is still encrypted; the browser just cannot vouch for who is on the other end.</p>
        <p>The fix is to tell your device, once, "this server's certificate authority is mine, trust it". This page walks you through that. You will need to be an administrator of the device.</p>
        </details>

        <details class="trust-why">
        <summary>What does trusting it mean?</summary>
        <p>A device that trusts a certificate authority believes it about <strong>every</strong> website, not only Tesria. That is fine while the authority's key stays on your server, where Tesria keeps it. It is also why each device checks the fingerprint in step 3 first: so that what it trusts is your server's, and nobody else's.</p>
        <p>Two ways need nothing trusted on each device: reaching Tesria through <strong>Tailscale</strong>, whose addresses have public certificates, or giving it a <strong>real domain</strong> with a public certificate. Both are in the <a href="{{DocsUrl}}">docs</a>.</p>
        </details>

        <section class="trust-step" data-step="1">
        <h2><span class="trust-step__num">1</span> Which device are you on?</h2>
        <p class="muted">We guessed from your browser. Choose another if it is wrong.</p>
        <div class="trust-devices" role="radiogroup" aria-label="Your device">
          <button type="button" class="trust-device" data-device="windows" role="radio">Windows</button>
          <button type="button" class="trust-device" data-device="mac" role="radio">Mac</button>
          <button type="button" class="trust-device" data-device="linux" role="radio">Linux</button>
          <button type="button" class="trust-device" data-device="ios" role="radio">iPhone or iPad</button>
          <button type="button" class="trust-device" data-device="android" role="radio">Android</button>
        </div>
        </section>

        <section class="trust-step" data-step="2">
        <h2><span class="trust-step__num">2</span> What address do you open Tesria at?</h2>
        <p class="muted">What you type into the browser's address bar, without <code>https://</code>. We filled in the address you used to reach this page.</p>
        <label class="trust-address">
          <span class="trust-address__prefix">https://</span>
          <input id="trust-address" type="text" aria-label="Your Tesria address" value="{{address}}" autocomplete="off" autocapitalize="off" spellcheck="false" inputmode="url" placeholder="wiki-server.local" />
        </label>
        <p class="trust-address__error alert alert--error" id="trust-address-error" hidden>That does not look like an address. Use letters, numbers, dots and hyphens, like <code>wiki-server.local</code>, with no <code>https://</code>, port or slash.</p>
        <div class="trust-ip alert alert--warning" id="trust-ip" hidden>
          <p><strong>Open Tesria by its name, not by this number.</strong> A certificate is issued for a name, such as <code>studio.local</code>. A browser keeps warning about an address like 192.168.1.50 even after the device trusts the server, so the address bar keeps saying "Not secure".</p>
          <p>The steps below still work from here: the certificate they install covers every name the server answers on. Afterwards, open Tesria by its name.</p>
          {{NameHelp(serverName)}}
        </div>
        </section>

        <section class="trust-step" data-step="3">
        <h2><span class="trust-step__num">3</span> Get the server's fingerprint</h2>
        <p>The <strong>fingerprint</strong> is a long code that only your server's certificate has, like <code>4B:1E:09:…</code> (32 pairs). Your device checks the certificate against it before trusting anything. Get it from the server itself, <strong>not from this page</strong>, which could have been changed:</p>
        <ul>
          <li><strong>Ask whoever runs your Tesria.</strong> They can read it on the server.</li>
          <li><strong>On the server computer,</strong> open <code>https://localhost</code>, then Administration, Settings, <strong>Certificate</strong>.</li>
          <li><strong>In a terminal on the server,</strong> in the Tesria folder: <code>docker compose logs app | grep -i fingerprint</code> <span class="muted">(on Windows, <code>| Select-String fingerprint</code>)</span>.</li>
          <li><strong>Through Tailscale,</strong> if your Tesria has it: Administration, Settings, Certificate, at its <code>ts.net</code> address.</li>
        </ul>
        <label class="trust-address trust-fingerprint">
          <span class="trust-address__prefix">SHA-256</span>
          <input id="trust-fingerprint" type="text" aria-label="The server's SHA-256 fingerprint" autocomplete="off" autocapitalize="off" spellcheck="false" placeholder="Paste the fingerprint here" />
        </label>
        <p class="alert alert--error" id="trust-fingerprint-error" hidden>That is not a SHA-256 fingerprint: it should be 64 letters and numbers, usually in pairs like <code>4B:1E:09</code>. Copy the SHA-256 one, not the SHA-1.</p>
        <p class="muted">The commands below fill it in. On a phone you compare it by eye instead, so keep it at hand.</p>
        </section>

        <section class="trust-step" data-step="4">
        <h2><span class="trust-step__num">4</span> Trust the certificate</h2>

        <div class="trust-guide" data-for="mac">
          <p>One line in Terminal downloads Tesria's script from GitHub, and the script trusts your server's certificate only if it matches the fingerprint.</p>
          <ol class="trust-list">
            <li>Open <strong>Terminal</strong>: press <kbd>⌘</kbd> <kbd>Space</kbd>, type <em>Terminal</em>, and press <kbd>Return</kbd>.</li>
            <li>Copy this line, paste it into Terminal, and press <kbd>Return</kbd>:
              {{Command(MacLinuxCommand, address)}}</li>
            <li>Type your Mac's login password when asked, and press <kbd>Return</kbd>. <span class="muted">Nothing appears while you type; that is normal.</span></li>
            <li>If it says the certificate <strong>does not match</strong>, stop: nothing was trusted. Check the fingerprint and the address.</li>
            <li>Quit your browser completely (<kbd>⌘</kbd> <kbd>Q</kbd>) and open it again.</li>
          </ol>
          <details class="trust-manual"><summary>Rather do it by hand?</summary>
            <ol>
              <li><a data-cert href="/ca.crt">Download the certificate</a>, then double-click it in your Downloads folder. <strong>Keychain Access</strong> opens and adds it to your login keychain.</li>
              <li>In Keychain Access, find the certificate named <em>Caddy Local Authority</em> and double-click it.</li>
              <li>Open <strong>Details</strong> and find <strong>SHA-256</strong> under Fingerprints. Compare it with the server's, every pair. If it differs, delete the certificate and stop.</li>
              <li>If it matches, open the <strong>Trust</strong> section and set "When using this certificate" to <strong>Always Trust</strong>. Close the window and enter your password to save.</li>
            </ol>
          </details>
        </div>

        <div class="trust-guide" data-for="windows">
          <p>One line in PowerShell downloads the certificate, checks it against the fingerprint, and adds it to the certificates your Windows account trusts only if it matches. Nothing to install, no administrator needed, and it works even where Windows blocks running script files.</p>
          <ol class="trust-list">
            <li>Open <strong>PowerShell</strong>: press the <kbd>Windows</kbd> key, type <em>PowerShell</em>, and press <kbd>Enter</kbd>.</li>
            <li>Copy this line, paste it into PowerShell (right-click pastes), and press <kbd>Enter</kbd>:
              {{Command(WindowsLine, address)}}</li>
            <li>If it says the certificate <strong>does not match</strong>, stop: nothing was trusted. Otherwise Windows shows a <strong>Security Warning</strong> about a certificate from <em>Caddy Local Authority</em>: that is your server's. Choose <strong>Yes</strong>.</li>
            <li>Close every browser window and open your browser again.</li>
          </ol>
          <details class="trust-manual"><summary>Setting it up for every user on this computer?</summary>
            <p>The line above trusts the server for your Windows account. To trust it for everyone who signs in to this computer, use Tesria's script from GitHub instead. It needs an administrator, and asks.</p>
            <ol>
              <li>In PowerShell, run:
                {{Command(WindowsScript, address)}}</li>
              <li>Choose <strong>Yes</strong> when Windows asks to allow changes.</li>
            </ol>
            <p class="muted">On a work computer your IT department may block scripts completely, and then this does not run at all. The line at the top still works, or ask IT to add the certificate for you.</p>
          </details>
          <details class="trust-manual"><summary>Rather do it by hand?</summary>
            <p>Windows' certificate window shows only the <strong>SHA-1</strong> fingerprint, which it calls the Thumbprint. The server shows that one too, beside the SHA-256.</p>
            <ol>
              <li><a data-cert href="/ca.crt">Download the certificate</a>, then double-click <code>tesria-ca.crt</code> in your Downloads folder.</li>
              <li>On the <strong>Details</strong> tab, choose <strong>Thumbprint</strong> and compare it with the server's SHA-1 fingerprint. If it differs, close the window and stop.</li>
              <li>If it matches, go back to <strong>General</strong> and choose <strong>Install Certificate…</strong>, then <strong>Current User</strong>, and <strong>Next</strong>.</li>
              <li>Choose <strong>Place all certificates in the following store</strong>, then <strong>Browse…</strong>, and pick <strong>Trusted Root Certification Authorities</strong>.</li>
              <li>Choose <strong>Next</strong>, <strong>Finish</strong>, and <strong>Yes</strong> on the Security Warning.</li>
            </ol>
          </details>
        </div>

        <div class="trust-guide" data-for="linux">
          <p>One line in a terminal downloads Tesria's script from GitHub, and the script trusts your server's certificate only if it matches the fingerprint. It works on Ubuntu, Debian, Fedora and their relatives.</p>
          <ol class="trust-list">
            <li>Open a terminal. On most systems, press <kbd>Ctrl</kbd> <kbd>Alt</kbd> <kbd>T</kbd>.</li>
            <li>Copy this line, paste it into the terminal, and press <kbd>Enter</kbd>:
              {{Command(MacLinuxCommand, address)}}</li>
            <li>Type your password when asked. <span class="muted">Nothing appears while you type; that is normal.</span></li>
            <li>If it says the certificate <strong>does not match</strong>, stop: nothing was trusted. Otherwise close your browser completely and open it again.</li>
          </ol>
        </div>

        <div class="trust-guide" data-for="ios">
          <p>On an iPhone or iPad this takes three parts: install the certificate, compare its fingerprint, then switch on full trust for it. Use <strong>Safari</strong> for the first part; other browsers cannot install certificates.</p>
          <ol class="trust-list">
            <li><a class="btn btn--primary" data-cert href="/ca.crt">Download the certificate</a> and tap <strong>Allow</strong>. <span class="muted">It says a configuration profile was downloaded; that is the certificate.</span></li>
            <li>Open the <strong>Settings</strong> app. Tap <strong>Profile Downloaded</strong> near the top, then <strong>Install</strong>, enter your passcode, and tap <strong>Install</strong> twice more.</li>
            <li>Compare the fingerprint: in Settings, <strong>General</strong>, <strong>VPN &amp; Device Management</strong>, tap the <em>Caddy Local Authority</em> profile, then <strong>More Details</strong> and the certificate. Scroll to <strong>SHA-256</strong> and compare it with the server's, every pair. If it differs, go back and tap <strong>Remove Profile</strong>, and stop.</li>
            <li>If it matches: in Settings, <strong>General</strong>, <strong>About</strong>, <strong>Certificate Trust Settings</strong> (at the very bottom), switch on <em>Caddy Local Authority</em> and tap <strong>Continue</strong>.</li>
          </ol>
        </div>

        <div class="trust-guide" data-for="android">
          <p>Android trusts a certificate as soon as it is installed, so compare its fingerprint straight afterwards. The exact menu names vary a little between phone makers.</p>
          <ol class="trust-list">
            <li><a class="btn btn--primary" data-cert href="/ca.crt">Download the certificate</a>. <span class="muted">It goes to your Downloads.</span></li>
            <li>Open <strong>Settings</strong> and search for <strong>CA certificate</strong>. <span class="muted">It is usually under Security and privacy, More security settings, Encryption and credentials, Install a certificate.</span> Choose <strong>CA certificate</strong>, then <strong>Install anyway</strong>, confirm with your screen lock, and pick <code>tesria-ca.crt</code>.</li>
            <li>Compare the fingerprint: in the same Encryption and credentials screen, open <strong>Trusted credentials</strong>, the <strong>User</strong> tab, and tap <em>Caddy Local Authority</em>. Compare its <strong>SHA-256</strong> fingerprint with the server's, every pair.</li>
            <li>If it differs, tap <strong>Remove</strong> at the bottom of that screen straight away.</li>
          </ol>
        </div>

        <details class="trust-manual trust-firefox"><summary>Using Firefox?</summary>
          <p>Firefox keeps its own list of trusted certificates, separate from your device's. After the steps above:</p>
          <ol>
            <li><a data-cert href="/ca.crt">Download the certificate</a>.</li>
            <li>In Firefox, open <strong>Settings</strong>, <strong>Privacy &amp; Security</strong>, scroll to <strong>Certificates</strong>, and choose <strong>View Certificates…</strong>.</li>
            <li>On the <strong>Authorities</strong> tab choose <strong>Import…</strong> and pick <code>tesria-ca.crt</code>. Before ticking anything, choose <strong>View</strong> and compare its <strong>SHA-256</strong> fingerprint with the server's. If it differs, cancel.</li>
            <li>If it matches, tick <strong>Trust this CA to identify websites</strong> and choose OK.</li>
          </ol>
        </details>
        </section>

        <section class="trust-step" data-step="5">
        <h2><span class="trust-step__num">5</span> Check it worked</h2>
        <p>Open Tesria with this link. If it opens with no warning, and your browser shows the usual padlock or "secure" sign by the address, you are done on this device.</p>
        <p><a class="btn btn--primary" id="trust-open" href="https://{{address}}/">Open Tesria securely</a></p>
        <details class="trust-manual" open><summary>Still says "Not secure"?</summary>
          <ul>
            <li><strong>Quit the browser completely.</strong> Closing its windows is not always enough: Chrome can keep running in the background. In Chrome or Edge, type <code>chrome://restart</code> (or <code>edge://restart</code>) into the address bar and press Enter.</li>
            <li><strong>Open Tesria by its name,</strong> not a number like 192.168.1.50. A number can never match the certificate, however it is trusted.</li>
            <li><strong>Ask the browser why.</strong> Click "Not secure" beside the address, then the certificate. <em>NET::ERR_CERT_COMMON_NAME_INVALID</em> means the address is not the name the certificate is for: use the name. <em>NET::ERR_CERT_AUTHORITY_INVALID</em> means this device does not trust the server yet: do step 4 again, then restart the browser.</li>
            <li><strong>Was the server reinstalled from scratch?</strong> Then it made a new certificate authority, with a new fingerprint, and each device needs these steps again.</li>
          </ul>
        </details>
        </section>
        """;
}
