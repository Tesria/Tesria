using ConfluenceClone.Api.Features.ApiTokens;
using ConfluenceClone.Api.Features.Attachments;
using ConfluenceClone.Api.Features.Auth;
using ConfluenceClone.Api.Features.Comments;
using ConfluenceClone.Api.Features.Audit;
using ConfluenceClone.Api.Features.Collab;
using ConfluenceClone.Api.Features.Export;
using ConfluenceClone.Api.Features.Groups;
using ConfluenceClone.Api.Features.Health;
using ConfluenceClone.Api.Features.Labels;
using ConfluenceClone.Api.Features.Pages;
using ConfluenceClone.Api.Features.Permissions;
using ConfluenceClone.Api.Features.Search;
using ConfluenceClone.Api.Features.Spaces;
using ConfluenceClone.Api.Features.Notifications;
using ConfluenceClone.Api.Features.Templates;
using ConfluenceClone.Api.Features.Watches;
using ConfluenceClone.Api.Features.Webhooks;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Audit;
using ConfluenceClone.Api.Infrastructure.Auth;
using ConfluenceClone.Api.Infrastructure.Collab;
using ConfluenceClone.Api.Infrastructure.Notifications;
using ConfluenceClone.Api.Infrastructure.Permissions;
using ConfluenceClone.Api.Infrastructure.Storage;
using ConfluenceClone.Api.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
builder.Services.AddSingleton<ICollabTokenService, CollabTokenService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IApiTokenService, ApiTokenService>();

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

// Persist Data Protection keys in the database (not the container filesystem),
// so signed auth cookies stay valid across redeploys and multiple app replicas.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("ConfluenceClone");

// Two ways in: the browser SPA uses the auth cookie; external scripts/
// integrations use a Bearer API token (Features/ApiTokens). A policy scheme
// picks between them per-request so every existing endpoint's
// RequireAuthorization() works unchanged for either caller.
const string SmartScheme = "Smart";
builder.Services
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
        options.Cookie.Name = "confluenceclone.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
    })
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationDefaults.AuthenticationScheme, _ => { });
builder.Services.AddAuthorization();

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

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

// Serve the built React SPA from wwwroot in production.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// API endpoints live under /api. Feature endpoints are registered via
// extension methods to keep Program.cs thin (vertical-slice style).
var api = app.MapGroup("/api");
api.MapHealthEndpoints();
api.MapAuthEndpoints();
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
// routing works. Guarded so it never swallows /api/* requests.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
