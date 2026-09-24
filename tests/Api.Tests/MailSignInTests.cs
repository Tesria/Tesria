using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Settings;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Mail provider presets and signing in to the mail server with Microsoft or
/// Google (dev-plan 18.1 to 18.3). The providers' token endpoints are a fake
/// handler; the send is against a fake SMTP server that records its AUTH.
/// </summary>
public class MailSignInTests
{
    private record ProviderDto(string Id, string Name, string Host, int Port, int Tls, string SupportPage, int? SignIn);
    private record MailDto(string? Provider, int SignIn, string? MicrosoftClientId, bool MicrosoftClientSecretSet,
        string? GoogleClientId, bool GoogleClientSecretSet, string? Account, string? Error,
        string MicrosoftRedirectUri, string GoogleRedirectUri, bool GooglePasteBack);
    private record SettingsDto(string? SmtpHost, int SmtpPort, int SmtpTls, string? SmtpUsername, string? SmtpFromAddress, MailDto Mail);
    private record StartDto(string Url, string RedirectUri, bool PasteBack);

    /// <summary>Answers the token endpoint with whatever the test queued, and remembers what it was sent.</summary>
    private sealed class FakeTokenEndpoint : HttpMessageHandler
    {
        public ConcurrentQueue<(HttpStatusCode Status, object Body)> Replies { get; } = new();
        public ConcurrentQueue<Dictionary<string, string>> Forms { get; } = new();
        public ConcurrentQueue<Uri> Urls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Enqueue(request.RequestUri!);
            var form = await request.Content!.ReadAsStringAsync(ct);
            Forms.Enqueue(QueryHelpers.ParseQuery(form).ToDictionary(kv => kv.Key, kv => kv.Value.ToString()));
            var (status, body) = Replies.TryDequeue(out var r) ? r : (HttpStatusCode.InternalServerError, new { error = "nothing queued" });
            return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
        }
    }

    private static string IdToken(string email, string? tid = null) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email, tid })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    [Fact]
    public async Task A_personal_Microsoft_account_sends_through_Outlook_com()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        var start = await (await admin.PostAsync("/api/admin/settings/email/oauth/microsoft/start", null)).Content.ReadFromJsonAsync<StartDto>();
        var state = QueryHelpers.ParseQuery(new Uri(start!.Url).Query)["state"];
        tokens.Replies.Enqueue((HttpStatusCode.OK, new
        {
            access_token = "a", refresh_token = "r", expires_in = 3600,
            id_token = IdToken("sam@outlook.com", MailOAuthService.MicrosoftConsumerTenant),
        }));
        await app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync($"/api/email/oauth/callback?code=c&state={state}");
        var s = (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!;
        Assert.Equal(("smtp-mail.outlook.com", "sam@outlook.com"), (s.SmtpHost, s.Mail.Account));
    }

    private static (WebApplicationFactory<Program> App, FakeTokenEndpoint Tokens, TestAppFactory Root) NewApp(int freshLoginMinutes = 10)
    {
        var root = new TestAppFactory(new Dictionary<string, string?>
        {
            ["Auth:FreshLoginMinutes"] = freshLoginMinutes.ToString(),
            ["Auth:SudoMinutes"] = freshLoginMinutes.ToString(),
        });
        var tokens = new FakeTokenEndpoint();
        var app = root.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHttpClient(MailOAuthService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => tokens)));
        return (app, tokens, root);
    }

    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> app, string baseUrl = "https://wiki.example.com")
    {
        var c = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await c.RegisterAndSignInAsync();
        (await c.PutAsJsonAsync("/api/admin/settings", new
        {
            BaseUrl = baseUrl, EmailEnabled = true,
            MicrosoftClientId = "ms-client", MicrosoftClientSecret = "ms-secret",
            GoogleClientId = "g-client", GoogleClientSecret = "g-secret",
        })).EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task The_presets_are_listed_for_whoever_may_change_the_email_server()
    {
        var (app, _, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        var providers = await admin.GetFromJsonAsync<List<ProviderDto>>("/api/admin/settings/email/providers");
        var gmail = providers!.Single(p => p.Id == "gmail");
        Assert.Equal(("smtp.gmail.com", 587, "Sending with Gmail"), (gmail.Host, gmail.Port, gmail.SupportPage));
        Assert.Equal((int)MailSignIn.Google, gmail.SignIn);
        Assert.Contains(providers!, p => p.Id == "icloud");

        var member = app.CreateClient();
        await member.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/settings/email/providers")).StatusCode);
    }

    [Fact]
    public async Task Choosing_a_preset_that_is_not_listed_is_refused()
    {
        var (app, _, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/settings", new { SmtpProvider = "carrier-pigeon" })).StatusCode);
        (await admin.PutAsJsonAsync("/api/admin/settings", new { SmtpProvider = "fastmail" })).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("https://wiki.example.com", true)]
    [InlineData("https://docs.acme.co.uk", true)]
    [InlineData("https://wiki.local", false)]
    [InlineData("https://studio.lan", false)]
    [InlineData("https://nas", false)]
    [InlineData("https://192.168.1.50", false)]
    [InlineData("https://localhost", false)]
    public void Google_returns_only_to_a_public_domain(string url, bool expected) =>
        Assert.Equal(expected, MailOAuthService.IsPublicDomain(url));

    [Fact]
    public async Task The_settings_never_show_a_secret_and_name_the_addresses_to_register()
    {
        var (app, _, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        var raw = await admin.GetStringAsync("/api/admin/settings");
        Assert.DoesNotContain("ms-secret", raw);
        Assert.DoesNotContain("g-secret", raw);
        var mail = (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!.Mail;
        Assert.True(mail.MicrosoftClientSecretSet && mail.GoogleClientSecretSet);
        Assert.Equal("https://wiki.example.com/api/email/oauth/callback", mail.MicrosoftRedirectUri);
        Assert.False(mail.GooglePasteBack);

        (await admin.PutAsJsonAsync("/api/admin/settings", new { BaseUrl = "https://wiki.local" })).EnsureSuccessStatusCode();
        mail = (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!.Mail;
        Assert.True(mail.GooglePasteBack);
        Assert.Equal("http://127.0.0.1", mail.GoogleRedirectUri);
    }

    [Fact]
    public async Task Starting_a_sign_in_needs_a_recent_password()
    {
        var (app, _, root) = NewApp(freshLoginMinutes: 0);
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        var res = await admin.PostAsync("/api/admin/settings/email/oauth/microsoft/start", null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("reauth_required", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_Microsoft_sign_in_comes_back_to_the_callback_and_points_the_mail_settings_at_it()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);

        var start = await (await admin.PostAsync("/api/admin/settings/email/oauth/microsoft/start", null))
            .Content.ReadFromJsonAsync<StartDto>();
        var authorize = new Uri(start!.Url);
        Assert.Equal("login.microsoftonline.com", authorize.Host);
        Assert.Contains("/common/oauth2/v2.0/authorize", authorize.AbsolutePath);
        var q = QueryHelpers.ParseQuery(authorize.Query);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Contains("https://outlook.office.com/SMTP.Send", q["scope"].ToString());
        Assert.Contains("offline_access", q["scope"].ToString());
        Assert.Equal(start.RedirectUri, q["redirect_uri"]);

        tokens.Replies.Enqueue((HttpStatusCode.OK, new
        {
            access_token = "access-1", refresh_token = "refresh-1", expires_in = 3600, id_token = IdToken("Alex@Contoso.com"),
        }));
        // The provider's redirect, followed by the browser without the session.
        var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var back = await anonymous.GetAsync($"/api/email/oauth/callback?code=the-code&state={q["state"]}");
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.StartsWith("/admin/settings?mail=connected", back.Headers.Location!.OriginalString);

        Assert.True(tokens.Forms.TryDequeue(out var form));
        Assert.Equal(("authorization_code", "the-code", "ms-secret"), (form["grant_type"], form["code"], form["client_secret"]));
        Assert.False(string.IsNullOrEmpty(form["code_verifier"]));

        var s = (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!;
        Assert.Equal((int)MailSignIn.Microsoft, s.Mail.SignIn);
        Assert.Equal("alex@contoso.com", s.Mail.Account);
        Assert.Equal(("smtp.office365.com", 587, "alex@contoso.com"), (s.SmtpHost, s.SmtpPort, s.SmtpFromAddress));

        // The state is single-use.
        var again = await anonymous.GetAsync($"/api/email/oauth/callback?code=the-code&state={q["state"]}");
        Assert.Contains("mail=error", again.Headers.Location!.OriginalString);

        // The refresh token is stored, never shown.
        Assert.DoesNotContain("refresh-1", await admin.GetStringAsync("/api/admin/settings"));
    }

    [Fact]
    public async Task A_refused_sign_in_comes_back_as_an_error_and_stores_nothing()
    {
        var (app, _, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        var start = await (await admin.PostAsync("/api/admin/settings/email/oauth/microsoft/start", null)).Content.ReadFromJsonAsync<StartDto>();
        var state = QueryHelpers.ParseQuery(new Uri(start!.Url).Query)["state"];

        var back = await app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync($"/api/email/oauth/callback?error=access_denied&state={state}");
        Assert.Contains("mail=error", back.Headers.Location!.OriginalString);
        Assert.Equal((int)MailSignIn.Password, (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!.Mail.SignIn);
    }

    [Fact]
    public async Task Google_on_a_LAN_name_is_finished_by_pasting_the_address_back()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app, baseUrl: "https://wiki.local");

        var start = await (await admin.PostAsync("/api/admin/settings/email/oauth/google/start", null)).Content.ReadFromJsonAsync<StartDto>();
        Assert.True(start!.PasteBack);
        var q = QueryHelpers.ParseQuery(new Uri(start.Url).Query);
        Assert.Equal(("offline", "consent", "http://127.0.0.1"), (q["access_type"].ToString(), q["prompt"].ToString(), q["redirect_uri"].ToString()));
        Assert.Contains("https://mail.google.com/", q["scope"].ToString());

        // Something that is not an address is refused before the sign-in is used up.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/settings/email/oauth/complete",
            new { Address = "not an address" })).StatusCode);

        tokens.Replies.Enqueue((HttpStatusCode.OK, new
        {
            access_token = "g-access", refresh_token = "g-refresh", expires_in = 3599, id_token = IdToken("team.wiki@gmail.com"),
        }));
        var done = await admin.PostAsJsonAsync("/api/admin/settings/email/oauth/complete",
            new { Address = $"http://127.0.0.1/?state={q["state"]}&code=g-code&scope=email" });
        done.EnsureSuccessStatusCode();
        var mail = await done.Content.ReadFromJsonAsync<MailDto>();
        Assert.Equal((int)MailSignIn.Google, mail!.SignIn);
        Assert.Equal("team.wiki@gmail.com", mail.Account);
        Assert.True(tokens.Forms.TryDequeue(out var form));
        Assert.Equal(("http://127.0.0.1", "g-secret"), (form["redirect_uri"], form["client_secret"]));
    }

    [Fact]
    public async Task A_new_client_ID_or_signing_out_forgets_the_sign_in()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        var admin = await AdminAsync(app);
        await SignInWithMicrosoftAsync(app, admin, tokens);

        // A new secret for the same client keeps it: that is how an expired one is replaced.
        (await admin.PutAsJsonAsync("/api/admin/settings", new { MicrosoftClientSecret = "ms-secret-2" })).EnsureSuccessStatusCode();
        Assert.Equal((int)MailSignIn.Microsoft, (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!.Mail.SignIn);

        (await admin.PutAsJsonAsync("/api/admin/settings", new { MicrosoftClientId = "another-app" })).EnsureSuccessStatusCode();
        var mail = (await admin.GetFromJsonAsync<SettingsDto>("/api/admin/settings"))!.Mail;
        Assert.Equal(((int)MailSignIn.Password, (string?)null), (mail.SignIn, mail.Account));

        await SignInWithMicrosoftAsync(app, admin, tokens);
        var after = await (await admin.PostAsync("/api/admin/settings/email/oauth/disconnect", null)).Content.ReadFromJsonAsync<MailDto>();
        Assert.Equal(((int)MailSignIn.Password, (string?)null), (after!.SignIn, after.Account));
    }

    private static async Task SignInWithMicrosoftAsync(WebApplicationFactory<Program> app, HttpClient admin, FakeTokenEndpoint tokens)
    {
        var start = await (await admin.PostAsync("/api/admin/settings/email/oauth/microsoft/start", null)).Content.ReadFromJsonAsync<StartDto>();
        var state = QueryHelpers.ParseQuery(new Uri(start!.Url).Query)["state"];
        tokens.Replies.Enqueue((HttpStatusCode.OK, new { access_token = "a", refresh_token = "r", expires_in = 3600, id_token = IdToken("alex@contoso.com") }));
        var back = await app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync($"/api/email/oauth/callback?code=c&state={state}");
        Assert.Contains("mail=connected", back.Headers.Location!.OriginalString);
        tokens.Forms.Clear();
    }

    [Fact]
    public async Task A_refused_renewal_is_recorded_raised_as_an_alert_and_a_rotated_token_is_kept()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        await AdminAsync(app);
        using var scope = app.Services.CreateScope();
        var oauth = scope.ServiceProvider.GetRequiredService<MailOAuthService>();
        var settings = scope.ServiceProvider.GetRequiredService<ISiteSettingsService>();
        await settings.UpdateAsync(x =>
        {
            x.SmtpSignIn = MailSignIn.Microsoft;
            x.MailOAuthRefreshTokenProtected = oauth.Protect("old-refresh");
            x.MailOAuthAccount = "alex@contoso.com";
        }, null);

        // Microsoft rotates the refresh token with each renewal.
        tokens.Replies.Enqueue((HttpStatusCode.OK, new { access_token = "fresh", refresh_token = "new-refresh", expires_in = 3600 }));
        Assert.Equal("fresh", await oauth.AccessTokenAsync(await settings.GetAsync(), default));
        Assert.True(tokens.Forms.TryDequeue(out var form));
        Assert.Equal(("refresh_token", "old-refresh"), (form["grant_type"], form["refresh_token"]));
        // Cached: no second request until it nears expiry.
        Assert.Equal("fresh", await oauth.AccessTokenAsync(await settings.GetAsync(), default));
        Assert.Empty(tokens.Forms);

        // The next renewal is refused (the new token is the one sent).
        scope.ServiceProvider.GetRequiredService<MailOAuthState>().ClearToken();
        tokens.Replies.Enqueue((HttpStatusCode.BadRequest, new { error = "invalid_grant", error_description = "AADSTS70000: revoked\r\nTrace ID: x" }));
        var ex = await Assert.ThrowsAsync<MailOAuthException>(async () => await oauth.AccessTokenAsync(await settings.GetAsync(), default));
        Assert.Contains("Sign in again", ex.Message);
        Assert.True(tokens.Forms.TryDequeue(out form));
        Assert.Equal("new-refresh", form["refresh_token"]);

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        Assert.Contains("Sign in again", (await settings.GetAsync()).MailOAuthError);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.SecurityAlerts.AnyAsync(a => a.Kind == "mail.signin_failed"));
    }

    [Fact]
    public async Task An_expired_Microsoft_secret_is_said_in_words()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        await AdminAsync(app);
        using var scope = app.Services.CreateScope();
        var oauth = scope.ServiceProvider.GetRequiredService<MailOAuthService>();
        var settings = scope.ServiceProvider.GetRequiredService<ISiteSettingsService>();
        await settings.UpdateAsync(x =>
        {
            x.SmtpSignIn = MailSignIn.Microsoft;
            x.MailOAuthRefreshTokenProtected = oauth.Protect("r");
        }, null);
        tokens.Replies.Enqueue((HttpStatusCode.Unauthorized, new { error = "invalid_client", error_description = "AADSTS7000222: The provided client secret keys for app are expired." }));
        var ex = await Assert.ThrowsAsync<MailOAuthException>(async () => await oauth.AccessTokenAsync(await settings.GetAsync(), default));
        Assert.Contains("client secret has expired", ex.Message);
    }

    [Fact]
    public async Task A_signed_in_send_authenticates_with_XOAUTH2_as_the_mailbox()
    {
        var (app, tokens, root) = NewApp();
        using var _r = root; using var _a = app;
        await AdminAsync(app);
        using var smtp = new FakeSmtpServer();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var oauth = sp.GetRequiredService<MailOAuthService>();
        var settings = sp.GetRequiredService<ISiteSettingsService>();
        await settings.UpdateAsync(x =>
        {
            x.EmailEnabled = true;
            x.SmtpHost = "127.0.0.1";
            x.SmtpPort = smtp.Port;
            x.SmtpTls = SmtpTlsMode.None;
            x.SmtpSignIn = MailSignIn.Google;
            x.SmtpFromAddress = "team.wiki@gmail.com";
            x.SmtpUsername = "team.wiki@gmail.com";
            x.MailOAuthAccount = "team.wiki@gmail.com";
            x.MailOAuthRefreshTokenProtected = oauth.Protect("g-refresh");
        }, null);
        tokens.Replies.Enqueue((HttpStatusCode.OK, new { access_token = "ya29.token", expires_in = 3599 }));

        var sender = new SmtpEmailSender(settings, sp.GetRequiredService<IAuditLogger>(), sp.GetRequiredService<AppDbContext>(),
            NullLogger<SmtpEmailSender>.Instance, oauth);
        var result = await sender.SendAsync(new EmailMessage("dana@example.com", "Hello", "Hi Dana"));
        Assert.True(result.Sent, result.Error);

        var auth = Assert.Single(smtp.Lines, l => l.StartsWith("AUTH ", StringComparison.Ordinal));
        Assert.StartsWith("AUTH XOAUTH2 ", auth);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth["AUTH XOAUTH2 ".Length..]));
        Assert.Equal("user=team.wiki@gmail.com\u0001auth=Bearer ya29.token\u0001\u0001", decoded);
    }

    /// <summary>Just enough SMTP to take one message, offering XOAUTH2, and remember every command line.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;
        public ConcurrentQueue<string> Lines { get; } = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeSmtpServer()
        {
            _listener.Start();
            _serve = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                Lines.Enqueue(line);
                var verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO": await writer.WriteAsync("250-fake\r\n250 AUTH XOAUTH2 PLAIN\r\n"); break;
                    case "AUTH": await writer.WriteLineAsync("235 2.7.0 Accepted"); break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go ahead");
                        while (await reader.ReadLineAsync() is { } data && data != ".") { }
                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT": await writer.WriteLineAsync("221 bye"); return;
                    default: await writer.WriteLineAsync("250 ok"); break;
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            try { _serve.Wait(TimeSpan.FromSeconds(2)); } catch { /* the listener stopping ends it */ }
        }
    }
}
