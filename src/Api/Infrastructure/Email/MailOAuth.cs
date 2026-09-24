using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Infrastructure.Email;

/// <summary>A provider refused, or could not be asked. The message is for the administrator, in words.</summary>
public sealed class MailOAuthException(string message) : Exception(message);

/// <summary>Where one provider's sign-in happens, and what it asks for.</summary>
public sealed record MailOAuthEndpoints(string Name, string Authorize, string Token, string Scope);

/// <summary>
/// Signing in to the mail server with Microsoft or Google instead of a
/// password (dev-plan 18.2, 18.3): the authorization code flow with PKCE,
/// against the administrator's own app registration, ending in a refresh
/// token that is stored protected and turned into a short-lived access token
/// for each send (SASL XOAUTH2).
///
/// The redirect normally comes back to this instance's callback. Google
/// accepts only public domains there, so an instance on a LAN name uses a
/// loopback address instead: the browser fails to load it, and the
/// administrator pastes the address from its address bar back into Tesria.
/// </summary>
public sealed class MailOAuthService(
    ISiteSettingsService settings, IHttpClientFactory http, IConfiguration config,
    MailOAuthState state, IDataProtectionProvider dataProtection, IAuditLogger audit,
    ISecurityDetector detector, ILogger<MailOAuthService> logger)
{
    public const string HttpClientName = "mail-oauth";
    public const string CallbackPath = "/api/email/oauth/callback";
    /// <summary>Where Google sends a LAN instance's sign-in: nothing listens, which is the point.</summary>
    public const string LoopbackRedirect = "http://127.0.0.1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Tesria.SiteSettings.MailOAuth.v1");

    public string Protect(string value) => _protector.Protect(value);

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return _protector.Unprotect(value); }
        catch (CryptographicException) { return null; }
    }

    public MailOAuthEndpoints EndpointsFor(MailSignIn kind, SiteSettings s) => kind switch
    {
        MailSignIn.Microsoft => new(
            "Microsoft",
            $"{MicrosoftAuthority}/{Tenant(s)}/oauth2/v2.0/authorize",
            $"{MicrosoftAuthority}/{Tenant(s)}/oauth2/v2.0/token",
            // openid and email name the mailbox that signed in; offline_access
            // is what returns a refresh token at all.
            "openid email offline_access https://outlook.office.com/SMTP.Send"),
        MailSignIn.Google => new(
            "Google",
            config["MailOAuth:GoogleAuthorize"] ?? "https://accounts.google.com/o/oauth2/v2/auth",
            config["MailOAuth:GoogleToken"] ?? "https://oauth2.googleapis.com/token",
            // The only Google scope that allows SMTP.
            "openid email https://mail.google.com/"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private string MicrosoftAuthority => (config["MailOAuth:MicrosoftAuthority"] ?? "https://login.microsoftonline.com").TrimEnd('/');

    private static string Tenant(SiteSettings s) =>
        string.IsNullOrWhiteSpace(s.MicrosoftTenant) ? "common" : s.MicrosoftTenant.Trim();

    /// <summary>
    /// Where the provider sends the browser back to: this instance's callback,
    /// or, for Google on an address that is not a public domain, the loopback
    /// address the administrator pastes back from.
    /// </summary>
    public static (string Uri, bool PasteBack) RedirectFor(MailSignIn kind, SiteSettings s, IConfiguration config)
    {
        var site = SiteUrl.Resolve(s, config);
        if (kind == MailSignIn.Google && !IsPublicDomain(site)) return (LoopbackRedirect, true);
        return (site + CallbackPath, false);
    }

    /// <summary>
    /// Whether Google would accept this address for a redirect: a name under a
    /// real top-level domain. A bare name, a number, localhost and the
    /// suffixes people use on home and office networks are not.
    /// </summary>
    public static bool IsPublicDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.HostNameType != UriHostNameType.Dns) return false;
        var host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
        var dot = host.LastIndexOf('.');
        if (dot < 0) return false;
        return !PrivateSuffixes.Contains(host[(dot + 1)..]);
    }

    private static readonly HashSet<string> PrivateSuffixes =
    [
        "local", "lan", "home", "internal", "localdomain", "localhost", "corp", "intranet",
        "private", "test", "example", "invalid", "arpa", "home.arpa", "domain",
    ];

    /// <summary>The address to open to sign in, remembered with its PKCE verifier for ten minutes.</summary>
    public (string Url, string RedirectUri, bool PasteBack) Start(MailSignIn kind, SiteSettings s, Guid userId)
    {
        var clientId = ClientIdFor(kind, s)
            ?? throw new MailOAuthException($"Enter the {EndpointsFor(kind, s).Name} client ID first.");
        if (ClientSecretFor(kind, s) is null)
            throw new MailOAuthException($"Enter the {EndpointsFor(kind, s).Name} client secret first.");

        var (redirect, pasteBack) = RedirectFor(kind, s, config);
        if (kind == MailSignIn.Microsoft && !redirect.StartsWith("https://", StringComparison.Ordinal))
            throw new MailOAuthException("Microsoft only returns to an https address. Set this instance’s address, under Instance, to its https address.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var key = Base64Url(RandomNumberGenerator.GetBytes(24));
        state.Add(key, new MailOAuthPending(userId, kind, verifier, redirect, DateTimeOffset.UtcNow.AddMinutes(10)));

        var ep = EndpointsFor(kind, s);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirect,
            ["scope"] = ep.Scope,
            ["state"] = key,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        if (kind == MailSignIn.Google)
        {
            // Without both, Google returns a refresh token only the first time.
            query["access_type"] = "offline";
            query["prompt"] = "consent";
        }
        else
        {
            query["prompt"] = "select_account";
        }
        var url = ep.Authorize + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return (url, redirect, pasteBack);
    }

    /// <summary>The pending sign-in this state names, taken so it cannot be used twice.</summary>
    public MailOAuthPending? Take(string? key) => string.IsNullOrEmpty(key) ? null : state.Take(key);

    /// <summary>
    /// Trades the code for tokens, stores the refresh token and the mailbox,
    /// and points the mail settings at the provider. Returns the mailbox.
    /// </summary>
    public async Task<string> CompleteAsync(MailOAuthPending pending, string code, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var ep = EndpointsFor(pending.Kind, s);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = pending.RedirectUri,
            ["client_id"] = ClientIdFor(pending.Kind, s) ?? "",
            ["client_secret"] = ClientSecretFor(pending.Kind, s) ?? "",
            ["code_verifier"] = pending.Verifier,
        };
        var tokens = await PostAsync(ep, form, ct);
        var refresh = tokens.RefreshToken
            ?? throw new MailOAuthException($"{ep.Name} signed in but gave no lasting permission (no refresh token). Check the app registration’s permissions, then try again.");
        var account = AccountFrom(tokens.IdToken)
            ?? throw new MailOAuthException($"{ep.Name} did not say which mailbox signed in. Check that the app may read the email address, then try again.");

        var provider = pending.Kind == MailSignIn.Microsoft ? MailProviders.Find("microsoft")! : MailProviders.Find("gmail")!;
        // A personal Microsoft account (Outlook.com, Hotmail, Live) sends
        // through its own server, not Microsoft 365's; the ID token's tenant
        // says which kind signed in.
        var host = pending.Kind == MailSignIn.Microsoft && TenantFrom(tokens.IdToken) == MicrosoftConsumerTenant
            ? OutlookComHost : provider.Host;
        await settings.UpdateAsync(x =>
        {
            x.SmtpSignIn = pending.Kind;
            x.SmtpProvider = provider.Id;
            x.SmtpHost = host;
            x.SmtpPort = provider.Port;
            x.SmtpTls = provider.Tls;
            // The provider sends only as the mailbox that signed in.
            x.SmtpUsername = account;
            x.SmtpFromAddress = account;
            x.MailOAuthRefreshTokenProtected = Protect(refresh);
            x.MailOAuthAccount = account;
            x.MailOAuthConnectedAt = DateTimeOffset.UtcNow;
            x.MailOAuthError = null;
        }, pending.UserId);
        if (tokens.AccessToken is { } access)
            state.CacheToken(refresh, access, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn));
        audit.RecordAs(pending.UserId, "mail.signin_connected", "instance", null, new { Provider = ep.Name, Account = account });
        return account;
    }

    /// <summary>Goes back to signing in with a password; the stored permission is forgotten.</summary>
    public async Task DisconnectAsync(Guid actorId, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var was = s.SmtpSignIn;
        await settings.UpdateAsync(x =>
        {
            x.SmtpSignIn = MailSignIn.Password;
            x.MailOAuthRefreshTokenProtected = null;
            x.MailOAuthAccount = null;
            x.MailOAuthConnectedAt = null;
            x.MailOAuthError = null;
        }, actorId);
        state.ClearToken();
        audit.RecordAs(actorId, "mail.signin_disconnected", "instance", null, new { Provider = was.ToString() });
    }

    /// <summary>
    /// An access token for the next send, renewed from the refresh token when
    /// the last one is within two minutes of expiring. A refusal is recorded
    /// on the settings, raised as an alert, and thrown in words.
    /// </summary>
    public async Task<string> AccessTokenAsync(SiteSettings s, CancellationToken ct)
    {
        var refresh = Unprotect(s.MailOAuthRefreshTokenProtected)
            ?? throw new MailOAuthException("Tesria is set to sign in to the mail server, but has no sign-in stored. Sign in again under Administration, Settings, Email.");
        if (state.CachedToken(refresh) is { } cached) return cached;

        var ep = EndpointsFor(s.SmtpSignIn, s);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
            ["client_id"] = ClientIdFor(s.SmtpSignIn, s) ?? "",
            ["client_secret"] = ClientSecretFor(s.SmtpSignIn, s) ?? "",
        };
        if (s.SmtpSignIn == MailSignIn.Microsoft) form["scope"] = "offline_access https://outlook.office.com/SMTP.Send";

        TokenResponse tokens;
        try
        {
            tokens = await PostAsync(ep, form, ct);
        }
        catch (MailOAuthException ex)
        {
            await settings.UpdateAsync(x => x.MailOAuthError = ex.Message, null);
            await detector.MailSignInFailedAsync(ep.Name, ex.Message);
            throw;
        }
        var access = tokens.AccessToken ?? throw new MailOAuthException($"{ep.Name} renewed the sign-in but sent no access token.");
        // Microsoft hands out a new refresh token with each renewal; keeping
        // the newest keeps the sign-in alive for as long as mail is sent.
        if ((tokens.RefreshToken is { } rotated && rotated != refresh) || s.MailOAuthError is not null)
        {
            var keep = tokens.RefreshToken ?? refresh;
            await settings.UpdateAsync(x =>
            {
                x.MailOAuthRefreshTokenProtected = Protect(keep);
                x.MailOAuthError = null;
            }, null);
            refresh = keep;
        }
        state.CacheToken(refresh, access, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn));
        return access;
    }

    private string? ClientIdFor(MailSignIn kind, SiteSettings s) => Blank(kind switch
    {
        MailSignIn.Microsoft => s.MicrosoftClientId,
        MailSignIn.Google => s.GoogleClientId,
        _ => null,
    });

    private string? ClientSecretFor(MailSignIn kind, SiteSettings s) => Blank(Unprotect(kind switch
    {
        MailSignIn.Microsoft => s.MicrosoftClientSecretProtected,
        MailSignIn.Google => s.GoogleClientSecretProtected,
        _ => null,
    }));

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private sealed record TokenResponse(string? AccessToken, string? RefreshToken, string? IdToken, int ExpiresIn);

    private async Task<TokenResponse> PostAsync(MailOAuthEndpoints ep, Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage res;
        string body;
        try
        {
            res = await http.CreateClient(HttpClientName).PostAsync(ep.Token, new FormUrlEncodedContent(form), ct);
            body = await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach {Provider} to sign in to the mail server", ep.Name);
            throw new MailOAuthException($"Could not reach {ep.Name}: {ex.Message}");
        }

        JsonElement json;
        try { json = JsonDocument.Parse(body).RootElement; }
        catch (JsonException) { throw new MailOAuthException($"{ep.Name} answered with something that was not a sign-in response (HTTP {(int)res.StatusCode})."); }

        if (!res.IsSuccessStatusCode)
        {
            var error = Str(json, "error") ?? $"HTTP {(int)res.StatusCode}";
            var detail = Str(json, "error_description");
            throw new MailOAuthException(Explain(ep.Name, error, detail));
        }
        return new TokenResponse(Str(json, "access_token"), Str(json, "refresh_token"), Str(json, "id_token"),
            json.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var secs) ? secs : 3600);
    }

    /// <summary>The provider's refusal, with what to do about the common ones.</summary>
    private static string Explain(string provider, string error, string? detail)
    {
        // Microsoft's description starts with an AADSTS code and runs to
        // several lines of trace ids; its first sentence is the useful part.
        var first = detail?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (detail?.Contains("AADSTS7000222", StringComparison.Ordinal) == true)
            return "The Microsoft client secret has expired. Make a new one in Microsoft Entra, enter it here, and sign in again.";
        if (detail?.Contains("AADSTS7000215", StringComparison.Ordinal) == true)
            return "Microsoft refused the client secret. Enter the secret’s Value (not its ID), then sign in again.";
        if (error == "invalid_client")
            return $"{provider} did not accept the client ID or secret. Check both, then sign in again.";
        if (error == "invalid_grant")
            return $"{provider} no longer accepts this sign-in: it was revoked, the account’s password changed, or it expired. Sign in again. ({first ?? error})";
        return $"{provider} refused: {first ?? error}";
    }

    private static string? Str(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The directory every personal Microsoft account belongs to.</summary>
    public const string MicrosoftConsumerTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";
    /// <summary>Outlook.com's own SMTP server, for personal Microsoft accounts.</summary>
    public const string OutlookComHost = "smtp-mail.outlook.com";

    private static JsonElement? Claims(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 }) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }

    public static string? TenantFrom(string? idToken) => Claims(idToken) is { } c ? Str(c, "tid") : null;

    /// <summary>
    /// The mailbox from the ID token's claims. It came straight from the
    /// provider's token endpoint over TLS in answer to this request, which is
    /// what makes reading it without checking its signature sound (OpenID
    /// Connect Core, 3.1.3.7).
    /// </summary>
    public static string? AccountFrom(string? idToken)
    {
        if (Claims(idToken) is not { } json) return null;
        // Microsoft may leave out email (it is optional there); the sign-in
        // name of a mailbox is its address.
        var address = Str(json, "email") ?? Str(json, "preferred_username");
        return address is not null && address.Contains('@') ? address.Trim().ToLowerInvariant() : null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record MailOAuthPending(Guid UserId, MailSignIn Kind, string Verifier, string RedirectUri, DateTimeOffset Expires);

/// <summary>
/// What the sign-in keeps in memory: pending sign-ins by state, and the
/// current access token. Neither needs to survive a restart: a sign-in in
/// progress is started again, and a token is renewed.
/// </summary>
public sealed class MailOAuthState
{
    private readonly ConcurrentDictionary<string, MailOAuthPending> _pending = new();
    private (string RefreshHash, string Token, DateTimeOffset Expires)? _token;
    private readonly Lock _lock = new();

    public void Add(string key, MailOAuthPending pending)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (k, p) in _pending)
            if (p.Expires < now) _pending.TryRemove(k, out _);
        _pending[key] = pending;
    }

    public MailOAuthPending? Take(string key) =>
        _pending.TryRemove(key, out var p) && p.Expires > DateTimeOffset.UtcNow ? p : null;

    public string? CachedToken(string refresh)
    {
        lock (_lock)
            return _token is { } t && t.RefreshHash == Hash(refresh) && t.Expires > DateTimeOffset.UtcNow.AddMinutes(2) ? t.Token : null;
    }

    public void CacheToken(string refresh, string token, DateTimeOffset expires)
    {
        lock (_lock) _token = (Hash(refresh), token, expires);
    }

    public void ClearToken()
    {
        lock (_lock) _token = null;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
