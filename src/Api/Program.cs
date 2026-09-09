using Tesria.Api.Features.ApiTokens;
using Tesria.Api.Features.Attachments;
using Tesria.Api.Features.Admin;
using Tesria.Api.Features.Media;
using Tesria.Api.Features.Auth;
using Tesria.Api.Features.Comments;
using Tesria.Api.Features.Audit;
using Tesria.Api.Features.Collab;
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

// Data layer: EF Core over PostgreSQL. Connection string comes from
// ConnectionStrings:Default (set by docker-compose in production).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=confluence;Username=confluence;Password=confluence";
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Auth: cookie-based sessions for the same-origin SPA. Argon2id hashing.
builder.Services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddSingleton<SiteSettingsCache>();
builder.Services.AddSingleton<LastSeenTracker>();
builder.Services.AddSingleton<RecoveryAttemptLimiter>();
builder.Services.AddScoped<IAccountRecoveryService, AccountRecoveryService>();
builder.Services.AddScoped<IInviteService, InviteService>();
builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();
builder.Services.AddSingleton<ICollabTokenService, CollabTokenService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IApiTokenService, ApiTokenService>();
builder.Services.AddScoped<IOidcUserProvisioner, OidcUserProvisioner>();

// Webhooks: matching (which webhooks, for which event) is scoped/DB-backed;
// delivery is a channel-backed background service so a slow or unreachable
// receiving endpoint never blocks the request that triggered the webhook.
builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
builder.Services.AddSingleton<ChannelWebhookSender>();
builder.Services.AddSingleton<IWebhookSender>(sp => sp.GetRequiredService<ChannelWebhookSender>());
builder.Services.AddHostedService<WebhookDeliveryBackgroundService>();
builder.Services.AddHttpClient(nameof(WebhookDeliveryBackgroundService), c => c.Timeout = TimeSpan.FromSeconds(10));

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
// terminates TLS, so as the app sees it the request is plain HTTP — which is
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
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
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
        // request — so a password change, a suspension or an admin force-logout
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
            if (current is null
                || current.Status != UserStatus.Active
                || !string.Equals(current.SecurityStamp, presented, StringComparison.Ordinal))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationDefaults.AuthenticationScheme, _ => { });

// OIDC/SSO (PLAN §1: "architected for OIDC/SSO later", pluggable for
// Keycloak/Authentik/Google, etc.) — entirely optional. With no Authority
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
        // id — not the provider's own claim shape — so every other endpoint
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
                    // principal — write the rejection ourselves and mark the
                    // response handled, the same explicit pattern OnRemoteFailure
                    // uses below, so no session is ever established on failure.
                    ctx.HttpContext.Response.Redirect("/login?ssoError=" + Uri.EscapeDataString(ex.Message));
                    ctx.HandleResponse();
                    return;
                }

                // Replace the provider's claims with our own internal shape —
                // the same one local login produces — before the handler signs
                // into the cookie scheme.
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Email, user.Email),
                    new(ClaimTypes.Name, user.DisplayName),
                };
                ctx.Principal = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
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

// Believe X-Forwarded-For / X-Forwarded-Proto from the reverse proxy, and
// nothing else — see ProxyTrust for what "the proxy" means here.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    ProxyTrust.Configure(o, builder.Configuration));

builder.Services.AddScoped<IAuthorizationHandler, AdminRequirementHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.RequireAdmin, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new AdminRequirement()));
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
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Production runs on PostgreSQL and applies versioned migrations. Tests swap
    // in a non-Npgsql provider (SQLite) and build the schema from the model.
    if (db.Database.IsNpgsql())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
}

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------

// First, so that everything after it — rate limiting, audit metadata, the
// cookie's secure flag — sees the client's address and scheme, not Caddy's.
app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

// Serve the built React SPA from wwwroot in production. No Cache-Control was
// set here before, so browsers applied heuristic caching to index.html itself
// (not just the content-hashed /assets/* bundles it references) — a client
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
app.UseDefaultFiles();
app.UseStaticFiles(spaStaticFileOptions);

app.UseAuthentication();
app.UseAuthorization();
// After authorization so it only ever stamps callers who got through it.
app.UseMiddleware<LastSeenMiddleware>();

// API endpoints live under /api. Feature endpoints are registered via
// extension methods to keep Program.cs thin (vertical-slice style).
var api = app.MapGroup("/api");
api.MapHealthEndpoints();
api.MapAuthEndpoints();
api.MapAdminEndpoints();
api.MapDashboardEndpoints();
api.MapMediaEndpoints();
api.MapSpaceEndpoints();
api.MapPageEndpoints();
api.MapAttachmentEndpoints();
api.MapCommentEndpoints();
api.MapSearchEndpoints();
api.MapLabelEndpoints();
api.MapExportEndpoints();
api.MapAuditEndpoints();
api.MapGroupEndpoints();
api.MapPermissionEndpoints();
api.MapCollabEndpoints();
api.MapTemplateEndpoints();
api.MapWatchEndpoints();
api.MapNotificationEndpoints();
api.MapApiTokenEndpoints();
api.MapWebhookEndpoints();

// SPA fallback: any non-API, non-file route returns index.html so client-side
// routing works. Guarded so it never swallows /api/* requests. Reuses the same
// options so this path also gets the no-cache header above, not just direct
// hits on "/" or "/index.html".
app.MapFallbackToFile("index.html", spaStaticFileOptions);

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
