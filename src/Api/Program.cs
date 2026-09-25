using Tesria.Api.Features.ApiTokens;
using Tesria.Api.Features.Attachments;
using Tesria.Api.Features.Admin;
using Tesria.Api.Features.Media;
using Tesria.Api.Features.Auth;
using Tesria.Api.Features.Comments;
using Tesria.Api.Features.Audit;
using Tesria.Api.Features.Collab;
using Tesria.Api.Features.Blocks;
using Tesria.Api.Features.Docs;
using Tesria.Api.Features.Embeds;
using Tesria.Api.Features.Export;
using Tesria.Api.Features.Groups;
using Tesria.Api.Features.Health;
using Tesria.Api.Features.Labels;
using Tesria.Api.Features.Pages;
using Tesria.Api.Features.Permissions;
using Tesria.Api.Features.Search;
using Tesria.Api.Features.Spaces;
using Tesria.Api.Features.Notifications;
using Tesria.Api.Features.Templates;
using Tesria.Api.Features.Watches;
using Tesria.Api.Features.Webhooks;
using Tesria.Api.Features.Public;
using Tesria.Api.Features.Setup;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Tesria.Api.Infrastructure.Telemetry;
using Tesria.Api.Infrastructure.Collab;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Storage;
using Tesria.Api.Infrastructure.Webhooks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

// Data layer: EF Core over PostgreSQL. Two connections (dev-plan 3.1):
// ConnectionStrings:Default is the owner, used at startup for migrations and
// to provision the least-privilege role; ConnectionStrings:App is that role,
// used by the running app. Under Compose only the `migrate` service has the
// owner (14.3). The fallback is for `dotnet run` against a local database.
var ownerConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=tesria;Username=tesria;Password=tesria";
var appConnectionString = builder.Configuration.GetConnectionString("App");

// The `migrate` service (dev-plan 14.3): the same image, run with --migrate,
// does the owner's work before the app starts. With --watch (14.4) it then
// stays up to serve restores and to repair a database that needs a pass.
if (args.Contains("--migrate"))
{
    using var migrateLogs = LoggerFactory.Create(l => l.AddConsole());
    var migrateLog = migrateLogs.CreateLogger("Migrate");
    if (args.Contains("--watch"))
    {
        using var stopping = new CancellationTokenSource();
        using var term = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stopping.Cancel(); });
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
        Environment.ExitCode = await MigrateCommand.WatchAsync(
            ownerConnectionString, appConnectionString, migrateLog, stopping.Token);
    }
    else
    {
        Environment.ExitCode = await MigrateCommand.RunAsync(ownerConnectionString, appConnectionString, migrateLog);
    }
    return;
}

// In production the app runs as the least-privilege role and nothing else
// (dev-plan 14.3): without APP_DB_PASSWORD it refuses to start rather than
// quietly running as the owner. Development and the tests keep the old
// fallback, so `dotnet ef` and a local run still work.
if (builder.Environment.IsProduction() && !DatabaseRoles.TryParseApp(appConnectionString, out _, out _))
    throw new InvalidOperationException(
        "APP_DB_PASSWORD is not set. Tesria runs as a database role that cannot alter its audit log, and needs "
        + "that role's password: add APP_DB_PASSWORD=<a long random string> to .env (openssl rand -hex 24 makes one), "
        + "then run docker compose up -d.");
var runtimeConnectionString = DatabaseRoles.ChooseRuntimeConnection(
    ownerConnectionString, appConnectionString,
    LoggerFactory.Create(l => l.AddConsole()).CreateLogger("Startup"));
// Live-editing connections end when access changes (dev-plan 14.3).
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Collab.CollabRevocationInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseNpgsql(runtimeConnectionString)
    .AddInterceptors(sp.GetRequiredService<Tesria.Api.Infrastructure.Collab.CollabRevocationInterceptor>()));

// Auth: cookie-based sessions for the same-origin SPA. Argon2id hashing.
builder.Services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IAuditChainVerifier, AuditChainVerifier>();
builder.Services.AddSingleton<AuditChainMonitor>();
builder.Services.AddSingleton<SecurityCounters>();
builder.Services.AddSingleton<DeniedRequestLog>();
builder.Services.AddSingleton<SharedClientAddresses>();
// Email sent after its request is answered (dev-plan 14.3).
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Email.EmailQueue>();
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Email.IEmailQueue>(sp => sp.GetRequiredService<Tesria.Api.Infrastructure.Email.EmailQueue>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Tesria.Api.Infrastructure.Email.EmailQueue>());
// Administration, About: OSV.dev, asked only when an administrator presses Check.
builder.Services.AddHttpClient(Tesria.Api.Features.Admin.OsvClient.HttpClientName, c =>
{
    c.BaseAddress = new Uri("https://api.osv.dev/");
    c.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<Tesria.Api.Features.Admin.IOsvClient, Tesria.Api.Features.Admin.OsvClient>();
// Export progress (dev-plan 20.1): in memory, like the counters above.
builder.Services.AddSingleton(new Tesria.Api.Features.Export.ExportProgress(TimeProvider.System));
builder.Services.AddSingleton<BlocklistCache>();
builder.Services.AddScoped<ISecurityDetector, SecurityDetector>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditChainMonitor>());
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Backups.BackupMonitor>();
// Whether a restore is running, held in this process (dev-plan 9.4): the one
// fact that has to stay readable while the database is being replaced.
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Backups.RestoreState>();
// The SPA's index.html, read once, which the shell endpoint writes the title
// and branding into (dev-plan 13.1).
builder.Services.AddSingleton<Tesria.Api.Features.Public.SpaShell>();
// Logos and favicons for instance branding (dev-plan 13.1).
builder.Services.AddScoped<Tesria.Api.Infrastructure.Branding.IBrandAssets, Tesria.Api.Infrastructure.Branding.BrandAssets>();
// Watches a restore, restarts into the restored database, and writes the
// lasting audit entry once it is up (dev-plan 9.4).
builder.Services.AddHostedService<Tesria.Api.Infrastructure.Backups.RestoreCompletion>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Tesria.Api.Infrastructure.Backups.BackupMonitor>());
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddSingleton<SiteSettingsCache>();
builder.Services.AddSingleton<LastSeenTracker>();
builder.Services.AddSingleton<RecoveryAttemptLimiter>();
builder.Services.AddScoped<IAccountRecoveryService, AccountRecoveryService>();
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<Tesria.Api.Infrastructure.Email.IEmailSender, Tesria.Api.Infrastructure.Email.SmtpEmailSender>();
// A week's warning before an API token expires (dev-plan 14.1).
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Auth.TokenExpiryNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Tesria.Api.Infrastructure.Auth.TokenExpiryNotifier>());
// Signing in to the mail server with Microsoft or Google (dev-plan 18.2, 18.3).
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Email.MailOAuthState>();
builder.Services.AddScoped<Tesria.Api.Infrastructure.Email.MailOAuthService>();
builder.Services.AddHttpClient(Tesria.Api.Infrastructure.Email.MailOAuthService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Email.NotificationEmailService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Tesria.Api.Infrastructure.Email.NotificationEmailService>());
builder.Services.AddScoped<IInviteService, InviteService>();
builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();
builder.Services.AddSingleton<ICollabTokenService, CollabTokenService>();
builder.Services.AddSingleton<Tesria.Api.Infrastructure.Export.IRenderTokens, Tesria.Api.Infrastructure.Export.RenderTokens>();
builder.Services.AddScoped<INotificationService, NotificationService>();
// Dynamic blocks (dev-plan Phase 7 Wave D): one service, one kind per class.
// Adding a kind is one class plus one line here: see architecture.md.
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockService, Tesria.Api.Features.Blocks.DynamicBlockService>();
builder.Services.AddScoped<Tesria.Api.Features.Embeds.ILinkPreviewService, Tesria.Api.Features.Embeds.LinkPreviewService>();
// PDF export (dev-plan 8.1) goes to the Playwright sidecar, which is handed
// a self-contained document and renders it with no network of its own.
// One page-write path for REST and MCP alike (dev-plan 8.4).
builder.Services.AddScoped<Tesria.Api.Features.Pages.IPageWriter, Tesria.Api.Features.Pages.PageWriter>();
builder.Services.AddScoped<Tesria.Api.Infrastructure.Collab.ICollabNotifier, Tesria.Api.Infrastructure.Collab.CollabNotifier>();
builder.Services.AddScoped<Tesria.Api.Features.Export.IPdfRenderer, Tesria.Api.Features.Export.PdfRenderer>();
builder.Services.AddHttpClient("pdf", c => c.Timeout = TimeSpan.FromSeconds(30));
// The collab sidecar is told about a page write *after* it has committed
// (dev-plan 8.6), so this timeout only bounds how long a save waits to tell
// it. Short on purpose: the reconciliation has a second, slower path (the
// document's next load), and a save should not sit behind a sick sidecar.
builder.Services.AddHttpClient("collab", c => c.Timeout = TimeSpan.FromSeconds(3));

// Machine-readable API description (dev-plan 8.3).
builder.Services.AddTesriaOpenApi();

// MCP server (dev-plan 8.4): in-process, on the official SDK, stateless so
// every request is authenticated by its own token and nothing is pinned to
// a session. Tools resolve their services from the request scope, which is
// what makes the permission service the same one every endpoint uses.
builder.Services.AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "tesria", Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0" };
        o.ServerInstructions =
            "Tesria is a self-hosted wiki. Pages live in spaces and form a tree; content is returned as Markdown. " +
            "'Not found' can mean the page does not exist or that this token's owner may not see it. " +
            "Write tools need a token minted with write access.";
    })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<Tesria.Api.Features.Mcp.TesriaTools>()
    // Every tool call counted and logged for the admin API tokens tab.
    .WithRequestFilters(f => f.AddCallToolFilter(Tesria.Api.Features.Mcp.McpActivity.Filter));
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.ChildrenBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.RecentlyUpdatedBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.ContentByLabelBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.AttachmentsBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.ChangeHistoryBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.ContributorsBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.IncludePageBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.ExcerptIncludeBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.PagePropertiesReportBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.LabelsBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.TaskReportBlock>();
builder.Services.AddScoped<Tesria.Api.Features.Blocks.IDynamicBlockKind, Tesria.Api.Features.Blocks.Kinds.PageTreeBlock>();
builder.Services.AddScoped<IApiTokenService, ApiTokenService>();
builder.Services.AddScoped<IOidcUserProvisioner, OidcUserProvisioner>();

// Webhooks: matching (which webhooks, for which event) is scoped/DB-backed;
// delivery is a channel-backed background service so a slow or unreachable
// receiving endpoint never blocks the request that triggered the webhook.
builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
builder.Services.AddSingleton<ChannelWebhookSender>();
builder.Services.AddSingleton<IWebhookSender>(sp => sp.GetRequiredService<ChannelWebhookSender>());
builder.Services.AddHostedService<WebhookDeliveryBackgroundService>();
// Outbound requests go through the egress guard (dev-plan 3.4): the address
// is checked again inside the connect, and redirects are followed by hand.
builder.Services.AddSingleton<EgressGuard>();
builder.Services.AddHttpClient(nameof(WebhookDeliveryBackgroundService), c => c.Timeout = EgressGuard.Timeout)
    .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<EgressGuard>().CreateHandler());

// Request bodies: Caddy caps at 100 MB; Kestrel says the same so the limit
// does not silently depend on which proxy is in front.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 100L * 1024 * 1024);

// Attachment file storage (local uploads volume; PLAN §3).
builder.Services.AddSingleton<IAttachmentStorage, LocalAttachmentStorage>();
builder.Services.AddSingleton<IProfileMediaService, ProfileMediaService>();

// Persist Data Protection keys in the database (not the container filesystem),
// so signed auth cookies stay valid across redeploys and multiple app replicas.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("Tesria");

// Two ways in: the browser SPA uses the auth cookie; external scripts/
// integrations use a Bearer API token (Features/ApiTokens). A policy scheme
// picks between them per-request so every existing endpoint's
// RequireAuthorization() works unchanged for either caller.
const string SmartScheme = "Smart";

// The session cookie is only ever sent over HTTPS in production. Caddy
// terminates TLS, so as the app sees it the request is plain HTTP, which is
// why "secure if the request was" used to mean "never". Forwarded headers
// (below) fix the scheme, but the cookie should not depend on the proxy
// being configured correctly. Security:AllowInsecureCookies is the escape
// hatch for a deliberately HTTP-only install; it is documented as unsafe.
var cookieSecurePolicy = builder.Environment.IsProduction()
    && !builder.Configuration.GetValue("Security:AllowInsecureCookies", false)
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = SmartScheme;
        options.DefaultChallengeScheme = SmartScheme;
    })
    .AddPolicyScheme(SmartScheme, "Cookie or API token", options =>
    {
        options.ForwardDefaultSelector = ctx => ctx.Request.Headers.ContainsKey("Authorization")
            ? ApiTokenAuthenticationDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "tesria.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        // Idle timeout: a cookie unused for two weeks expires. The absolute
        // lifetime is enforced below from the auth_time claim (dev-plan 3.5).
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // This is an API, not a server-rendered app: respond with status codes
        // instead of redirecting to a login/access-denied page.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        // What makes a stateless cookie revocable. The stamp is issued into the
        // cookie at sign-in and compared against the stored one here, on every
        // request, so a password change, a suspension or an admin force-logout
        // takes effect on the very next request rather than whenever the cookie
        // happens to expire.
        //
        // Cookies issued before this existed carry no stamp claim and are
        // rejected, which signs everyone in once. That is the safe direction:
        // treating a missing claim as valid would mean a pre-existing cookie
        // outlived the password change meant to kill it.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            if (ctx.Principal?.Identity?.IsAuthenticated != true) return;

            var presented = ctx.Principal.FindFirstValue(AuthEndpoints.SecurityStampClaim);
            var userId = ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (presented is null || !Guid.TryParse(userId, out var id))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var current = await db.Users.AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new { u.SecurityStamp, u.Status })
                .FirstOrDefaultAsync();

            // A deleted or suspended account's cookie stops working here too,
            // which is what dev-plan 2.2's suspend action will rely on.
            var reject = current is null
                || current.Status != UserStatus.Active
                || !string.Equals(current.SecurityStamp, presented, StringComparison.Ordinal);

            // Absolute lifetime (dev-plan 3.5): however active, a session ends
            // some weeks after it began. Sliding expiry alone would let a
            // cookie live forever.
            var absoluteDays = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()
                .GetValue("Auth:SessionAbsoluteDays", AuthEndpoints.DefaultSessionAbsoluteDays);
            if (!reject && AuthEndpoints.AuthTimeOf(ctx.Principal) is { } authTime
                && DateTimeOffset.UtcNow - authTime > TimeSpan.FromDays(absoluteDays))
                reject = true;

            // Per-session revocation (dev-plan 3.5): a cookie whose session row
            // is revoked (by its owner from the sessions list, by sign-out,
            // or by an admin) is dead even though the stamp still matches.
            // A cookie with no session claim predates sessions and is rejected,
            // which signs everyone in once; the safe direction, as above.
            if (!reject)
            {
                var sessionId = AuthEndpoints.SessionIdOf(ctx.Principal);
                reject = sessionId is null || await db.UserSessions.AsNoTracking()
                    .AnyAsync(s => s.Id == sessionId && s.RevokedAt == null) == false;
            }

            if (reject)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationDefaults.AuthenticationScheme, _ => { });

// OIDC/SSO (PLAN §1: "architected for OIDC/SSO later", pluggable for
// Keycloak/Authentik/Google, etc.): entirely optional. With no Authority
// configured, this scheme is never registered and the app behaves exactly as
// it did with local accounts only.
var oidcAuthority = builder.Configuration["Oidc:Authority"];
if (!string.IsNullOrWhiteSpace(oidcAuthority))
{
    authBuilder.AddOpenIdConnect(OidcAuthenticationDefaults.Scheme, options =>
    {
        options.Authority = oidcAuthority;
        options.ClientId = builder.Configuration["Oidc:ClientId"];
        options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
        // Defaults to true; only disable for a same-network/dev IdP served over
        // plain http (never appropriate across a real network boundary).
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);

        options.ResponseType = "code";
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        // Some providers omit email/email_verified from the id_token itself.
        options.GetClaimsFromUserInfoEndpoint = true;
        // We only need the resolved local identity, not the provider's tokens.
        options.SaveTokens = false;

        // Complete sign-in on our normal cookie scheme using OUR internal user
        // id, not the provider's own claim shape, so every other endpoint
        // (CurrentUser, permissions, audit, ...) keeps working unchanged
        // regardless of which auth method the caller used.
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Same policy as the session cookie, for the same reasons.
        options.CorrelationCookie.SecurePolicy = cookieSecurePolicy;
        options.NonceCookie.SecurePolicy = cookieSecurePolicy;

        options.Events = new OpenIdConnectEvents
        {
            OnTicketReceived = async ctx =>
            {
                var principal = ctx.Principal!;
                var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub");
                var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email");
                var emailVerifiedClaim = principal.FindFirstValue("email_verified");
                var emailVerified = string.Equals(emailVerifiedClaim, "true", StringComparison.OrdinalIgnoreCase)
                    || emailVerifiedClaim == "1";
                var displayName = principal.FindFirstValue(ClaimTypes.Name)
                    ?? principal.FindFirstValue("name")
                    ?? principal.FindFirstValue("preferred_username");

                var provisioner = ctx.HttpContext.RequestServices.GetRequiredService<IOidcUserProvisioner>();
                User user;
                try
                {
                    user = await provisioner.ResolveOrProvisionAsync(subject!, email, emailVerified, displayName);
                }
                catch (Exception ex)
                {
                    // ctx.Fail() alone does not reliably stop TicketReceived from
                    // completing sign-in with the provider's own (unmapped)
                    // principal: write the rejection ourselves and mark the
                    // response handled, the same explicit pattern OnRemoteFailure
                    // uses below, so no session is ever established on failure.
                    ctx.HttpContext.Response.Redirect("/login?ssoError=" + Uri.EscapeDataString(ex.Message));
                    ctx.HandleResponse();
                    return;
                }

                // Replace the provider's claims with our own internal shape,
                // exactly what local login produces (security stamp, sign-in
                // time and a session row), before the handler signs into the
                // cookie scheme. Anything less is rejected on the next request.
                ctx.Principal = await AuthEndpoints.SessionPrincipalAsync(
                    ctx.HttpContext, ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>(), user);
            },
            // Land back on the login page with a readable error instead of an
            // unhandled-exception page if the provider or provisioning fails.
            OnRemoteFailure = ctx =>
            {
                ctx.Response.Redirect("/login?ssoError=" + Uri.EscapeDataString(ctx.Failure?.Message ?? "Sign-in failed."));
                ctx.HandleResponse();
                return Task.CompletedTask;
            },
        };
    });
}

// Rate limits (dev-plan 3.2). Policies are attached to endpoints by name;
// the global limiter covers anonymous callers. See RateLimits.
builder.Services.AddRateLimiter(o => { });
builder.Services.AddOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>()
    .Configure<SiteSettingsCache>((o, cache) => RateLimits.Configure(o, cache));

// Believe X-Forwarded-For / X-Forwarded-Proto from the reverse proxy, and
// nothing else: see ProxyTrust for what "the proxy" means here.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    ProxyTrust.Configure(o, builder.Configuration));

builder.Services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
builder.Services.AddScoped<IAuthorizationHandler, OwnerRequirementHandler>();
// Instance rights (dev-plan 11.1): perm:<key> policies, built on demand.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
builder.Services.AddScoped<IInstancePermissions, InstancePermissionService>();
builder.Services.AddSingleton<PermissionCache>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.RequireAdmin, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new AdminRequirement()));
    options.AddPolicy(AuthPolicies.RequireOwner, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new OwnerRequirement()));
});

// Health checks, incl. a database probe now that EF Core is wired in.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// CORS: only needed in development, where the Vite dev server runs on a
// different origin (http://localhost:5173) than the API. In production the
// SPA is served from the same origin, so CORS is not used.
const string DevCorsPolicy = "dev-spa";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Startup: apply EF Core migrations (safe to re-run; PLAN §6).
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var startupLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql())
    {
        if (app.Environment.IsProduction())
        {
            // The owner's work was done by the `migrate` service before this
            // process started (dev-plan 14.3); this one has no owner password.
            // A schema that is behind means that step failed, and serving
            // requests against it would fail later and less clearly.
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
                throw new InvalidOperationException(
                    $"The database is {pending.Count} migration(s) behind this version of Tesria ({pending[0]} first). "
                    + "The migrate step did not finish: see docker compose logs migrate.");
        }
        else if (await MigrateCommand.RunAsync(ownerConnectionString, appConnectionString, startupLog) != 0)
        {
            // Development: migrate inline, as before, so a local run needs no
            // second step.
            throw new InvalidOperationException("Migrating the database failed; see the log above.");
        }
    }
    else
    {
        // Tests swap in SQLite and build the schema from the model. SQLite has
        // no roles, so the split is a no-op; the chain still applies.
        db.Database.EnsureCreated();
        await AuditChain.BackfillAsync(db);
    }

    // Load settings once so the rate limiter, which cannot await, has real
    // values from the first request rather than defaults until someone
    // happens to sign in.
    await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().GetAsync();

    // The first start after dev-plan 9.1 turns BACKUP_RETENTION_DAYS into a policy.
    await Tesria.Api.Infrastructure.Backups.BackupPolicySeed.EnsureAsync(
        scope.ServiceProvider.GetRequiredService<ISiteSettingsService>(), app.Configuration, startupLog);

    // The first start after dev-plan 11.1 creates the built-in roles and
    // attaches every account to one. Before the owner seed, so the account it
    // promotes already has a role to be moved between.
    await Tesria.Api.Infrastructure.Permissions.RoleSeed.EnsureAsync(
        db,
        scope.ServiceProvider.GetRequiredService<Tesria.Api.Infrastructure.Permissions.PermissionCache>(),
        scope.ServiceProvider.GetRequiredService<ISiteSettingsService>(),
        startupLog);

    // Administration rights leave user-tier roles (dev-plan 15.1).
    await Tesria.Api.Infrastructure.Permissions.RoleSeed.StripAdministrationRightsFromUserTierAsync(
        db, scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
        scope.ServiceProvider.GetRequiredService<Tesria.Api.Infrastructure.Permissions.PermissionCache>(), startupLog);

    // The built-in groups, Owner, Admins and Users (dev-plan 15.1).
    await Tesria.Api.Infrastructure.Permissions.BuiltInGroups.EnsureAsync(
        db, scope.ServiceProvider.GetRequiredService<IAuditLogger>(), startupLog);

    // Which version is running, and an audit entry when that changed (dev-plan 16.1).
    await Tesria.Api.Infrastructure.Versioning.VersionSeed.EnsureAsync(
        scope.ServiceProvider.GetRequiredService<ISiteSettingsService>(),
        scope.ServiceProvider.GetRequiredService<IAuditLogger>(), db, startupLog);

    // The first start after dev-plan 10.1 gives an existing instance its owner.
    await Tesria.Api.Infrastructure.Auth.OwnerSeed.EnsureAsync(
        db,
        scope.ServiceProvider.GetRequiredService<IAuditLogger>(),
        scope.ServiceProvider.GetRequiredService<ISiteSettingsService>(),
        startupLog);
}

// A broken audit chain is a security alert, not just a log line.
app.Services.GetRequiredService<AuditChainMonitor>().OnBroken = async (services, report) =>
{
    await services.GetRequiredService<ISecurityDetector>().AuditChainBrokenAsync(report);
    await services.GetRequiredService<AppDbContext>().SaveChangesAsync();
};

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------

// First, so that everything after it (rate limiting, audit metadata, the
// cookie's secure flag) sees the client's address and scheme, not Caddy's.
app.UseForwardedHeaders();
// A blocked address is turned away here, before anything else runs.
app.UseMiddleware<BlocklistMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<DeniedResponseMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

// Serve the built React SPA from wwwroot in production. No Cache-Control was
// set here before, so browsers applied heuristic caching to index.html itself
// (not just the content-hashed /assets/* bundles it references): a client
// could keep rendering a stale index.html referencing assets from a prior
// deploy for an unpredictable, browser-chosen length of time. index.html must
// always be revalidated; the hashed bundles it points at are safe to cache
// forever since a content change gives them a new filename.
var spaStaticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        headers.CacheControl = ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache"
            : "public, max-age=31536000, immutable";
    },
};
// index.html is never served as a file (dev-plan 13.1). Every application
// route, "/" included, goes to the SPA shell endpoint at the bottom of this
// file, which writes the tab title, the link-preview tags and the branding
// into it. So there is no UseDefaultFiles, and a direct request for the file
// itself is treated as a request for "/".
app.Use((context, next) =>
{
    if (string.Equals(context.Request.Path.Value, "/index.html", StringComparison.OrdinalIgnoreCase))
        context.Request.Path = "/";
    return next(context);
});
app.UseStaticFiles(spaStaticFileOptions);

app.UseAuthentication();
// Knows who authenticated and how; must therefore follow authentication.
app.UseMiddleware<CsrfHeaderMiddleware>();
// After authentication so the global limiter can tell a session from a
// stranger; before authorization so a rejected request does no more work.
app.UseRateLimiter();
app.UseAuthorization();
// After authorization so it only ever stamps callers who got through it.
app.UseMiddleware<LastSeenMiddleware>();
// Counts token requests once answered (the admin API tokens tab); outside the
// scope checks, so a refused change is counted as a request, not a change.
app.UseMiddleware<Tesria.Api.Infrastructure.Auth.TokenUsageMiddleware>();
// Read-only API tokens may not change anything over REST (dev-plan 8.4).
app.UseMiddleware<Tesria.Api.Infrastructure.Security.TokenScopeMiddleware>();
app.UseMiddleware<Tesria.Api.Infrastructure.Security.TokenAccountGuardMiddleware>();
// A render token may only read the page or space it was minted for (12.1).
app.UseMiddleware<Tesria.Api.Infrastructure.Security.RenderScopeMiddleware>();
// While a restore is running the wiki is read-only (dev-plan 9.4). Last, so
// an unauthenticated or rate-limited request is still turned away as it
// normally would be rather than being told the instance is in maintenance.
app.UseMiddleware<Tesria.Api.Infrastructure.Security.MaintenanceMiddleware>();

// API endpoints live under /api. Feature endpoints are registered via
// extension methods to keep Program.cs thin (vertical-slice style).
var api = app.MapGroup("/api");
api.MapSetupEndpoints();
api.MapHealthEndpoints();
api.MapAuthEndpoints();
api.MapAdminEndpoints();
api.MapMailSignInEndpoints();
api.MapTailscaleEndpoints();
api.MapAdminTokenEndpoints();
api.MapAboutEndpoints();
api.MapSecurityEndpoints();
api.MapBackupEndpoints();
api.MapBrandingEndpoints();
api.MapRoleEndpoints();
api.MapDashboardEndpoints();
api.MapMediaEndpoints();
api.MapSpaceEndpoints();
api.MapPageEndpoints();
api.MapAttachmentEndpoints();
api.MapCommentEndpoints();
api.MapSearchEndpoints();
api.MapLabelEndpoints();
api.MapExportEndpoints();
api.MapSiteExportEndpoints();
api.MapPackExportEndpoints();
api.MapExportProgressEndpoints();
api.MapPackImportEndpoints();
api.MapBlockEndpoints();
api.MapEmbedEndpoints();
api.MapAuditEndpoints();
api.MapGroupEndpoints();
api.MapPermissionEndpoints();
api.MapCollabEndpoints();
// The collaboration service asking about its connections (14.4, SEC-02).
app.MapCollabInternalEndpoints();
api.MapTemplateEndpoints();
api.MapWatchEndpoints();
api.MapNotificationEndpoints();
api.MapApiTokenEndpoints();
api.MapWebhookEndpoints();

// The spec and its reader (dev-plan 8.3).
app.MapTesriaApiDocs();

// /mcp: API tokens only: a browser session is never accepted here, so a
// page in someone's tab cannot drive the assistant surface (8.4, decision 2).
app.MapMcp("/mcp").RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
{
    AuthenticationSchemes = ApiTokenAuthenticationDefaults.AuthenticationScheme,
});

// Not under /api: robots.txt and sitemap.xml live at the root (dev-plan 5.2).
app.MapPublicEndpoints();
// /trust: trusting this server's own certificate, over plain HTTP too (15.5).
Tesria.Api.Features.Trust.TrustEndpoints.MapTrustEndpoints(app);
app.MapInstanceEndpoints();
// The logo and favicon files, anonymous because the sign-in page needs them.
Tesria.Api.Features.Admin.BrandingEndpoints.MapBrandingAssetEndpoints(app);

// SPA fallback: any non-API, non-file route gets the shell, so client-side
// routing works. The shell carries the title and the branding, written in by
// the server (dev-plan 13.1; see SpaShell). Always no-cache, so a change to
// the branding reaches everyone on their next load.
app.MapFallback(Tesria.Api.Features.Public.SpaShell.Handle);
// The fallback's catch-all does not match the root itself, which the static
// files middleware used to answer; with no default document any more, "/"
// is mapped to the same shell explicitly.
app.MapGet("/", Tesria.Api.Features.Public.SpaShell.Handle).ExcludeFromDescription();
// Routing runs before the static files middleware, so the rewrite near the
// top of this file (which keeps static files from serving the raw file) is
// not enough on its own: the route has to exist for the request to land here.
app.MapGet("/index.html", Tesria.Api.Features.Public.SpaShell.Handle).ExcludeFromDescription();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
