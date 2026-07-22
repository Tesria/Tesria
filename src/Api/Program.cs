using ConfluenceClone.Api.Features.Health;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

// Built-in health checks (part of the ASP.NET Core shared framework — no NuGet).
// Phase 2 will add a database health check here once EF Core is wired in.
builder.Services.AddHealthChecks();

// CORS: only needed in development, where the Vite dev server runs on a
// different origin (http://localhost:5173) than the API. In production the
// SPA is served from the same origin, so CORS is not used.
const string DevCorsPolicy = "dev-spa";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

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

// API endpoints live under /api. Feature endpoints are registered via
// extension methods to keep Program.cs thin (vertical-slice style).
var api = app.MapGroup("/api");
api.MapHealthEndpoints();

// SPA fallback: any non-API, non-file route returns index.html so client-side
// routing works. Guarded so it never swallows /api/* requests.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
