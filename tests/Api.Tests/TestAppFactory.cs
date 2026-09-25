using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tesria.Api.Tests;

/// <summary>
/// Boots the real API in-process but swaps PostgreSQL for a private SQLite
/// in-memory database, so endpoint and data-access behavior is exercised
/// end-to-end without Docker. Each instance owns a fresh, isolated database;
/// create one per test for isolation.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    // A single kept-open connection keeps the in-memory database alive for the
    // lifetime of the factory (it is dropped when the last connection closes).
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    // Attachment uploads go to a throwaway directory unique to this factory.
    private readonly string _uploadsPath =
        Path.Combine(Path.GetTempPath(), "cc-tests", Guid.NewGuid().ToString("N"));

    private readonly Dictionary<string, string?> _settings = new();

    public TestAppFactory() { }

    /// <param name="freshLoginMinutes">
    /// Shrinks the window in which a recent sign-in stands in for a password.
    /// Pass 0 to make every session count as stale.
    /// </param>
    public TestAppFactory(int freshLoginMinutes) =>
        _settings["Auth:FreshLoginMinutes"] = freshLoginMinutes.ToString();

    /// <summary>Arbitrary configuration overrides, e.g. <c>Egress:AllowedNetworks</c>.</summary>
    public TestAppFactory(Dictionary<string, string?> settings) => _settings = settings;

    private readonly PostgresTestDatabase? _postgres;

    /// <summary>
    /// Real PostgreSQL instead of SQLite, with the production split: the app
    /// migrates as the owner and then runs as the least-privilege role, so its
    /// grants are enforced (the review's DATA-04). See <see cref="PostgresFactAttribute"/>.
    /// </summary>
    public TestAppFactory(PostgresTestDatabase postgres) => _postgres = postgres;

    /// <summary>
    /// Every client sends the CSRF marker the app requires on cookie-
    /// authenticated state changes (dev-plan 3.4), the way the SPA does. The
    /// one test that checks the requirement removes it again.
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Tesria");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        if (_postgres is not null)
        {
            // Settings rather than app configuration: Program reads the
            // connection strings before the host is built.
            builder.UseSetting("ConnectionStrings:Default", _postgres.OwnerConnection);
            builder.UseSetting("ConnectionStrings:App", _postgres.AppConnection);
        }

        // A minimal SPA shell, so the public-meta middleware (dev-plan 5.2) has
        // something to inject into. The real one is built into the image.
        Directory.CreateDirectory(_uploadsPath);
        var webRoot = Path.Combine(_uploadsPath, "wwwroot");
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(Path.Combine(webRoot, "index.html"),
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Tesria</title></head><body><div id=\"root\"></div></body></html>");
        builder.UseWebRoot(webRoot);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:UploadsPath"] = _uploadsPath,
            })
            .AddInMemoryCollection(_settings));
        builder.ConfigureServices(services =>
        {
            ReplaceTestDoubles(services);
            if (_postgres is not null) return;

            // Drop the production Npgsql registration entirely, both the built
            // options and the internal options-configuration action EF adds per
            // AddDbContext call, so SQLite is the only configured provider.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition().Name
                        .StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && d.ServiceType.GenericTypeArguments is [var arg] && arg == typeof(AppDbContext)))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlite(_connection)
                .AddInterceptors(sp.GetRequiredService<Tesria.Api.Infrastructure.Collab.CollabRevocationInterceptor>()));
        });
    }

    private static void ReplaceTestDoubles(IServiceCollection services)
    {
        // Runs before the app's pipeline, so forwarded headers behave as
        // they do behind Caddy: see the filter for how tests use it.
        services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();

        // Replace the real channel-backed sender with a recording fake, so
        // tests can assert on dispatched webhooks without any network I/O
        // or a running background delivery service.
        // Email is recorded, never sent (dev-plan 4.1).
        services.RemoveAll<Tesria.Api.Infrastructure.Email.IEmailSender>();
        services.AddSingleton<RecordingEmailSender>();
        services.AddSingleton<Tesria.Api.Infrastructure.Email.IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());

        // Queued email is sent at once in tests, so a test can read it
        // back as soon as the request returns (dev-plan 14.3).
        services.RemoveAll<Tesria.Api.Infrastructure.Email.IEmailQueue>();
        services.AddSingleton<Tesria.Api.Infrastructure.Email.IEmailQueue, ImmediateEmailQueue>();

        services.RemoveAll<IWebhookSender>();
        services.AddSingleton<RecordingWebhookSender>();
        services.AddSingleton<IWebhookSender>(sp => sp.GetRequiredService<RecordingWebhookSender>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            try { if (Directory.Exists(_uploadsPath)) Directory.Delete(_uploadsPath, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }
}

/// <summary>The email queue, sending at once through the recording sender.</summary>
public sealed class ImmediateEmailQueue(RecordingEmailSender sender) : Tesria.Api.Infrastructure.Email.IEmailQueue
{
    public void Enqueue(Tesria.Api.Infrastructure.Email.EmailMessage message) =>
        sender.SendAsync(message).GetAwaiter().GetResult();
}
