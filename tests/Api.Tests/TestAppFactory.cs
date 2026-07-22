using ConfluenceClone.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConfluenceClone.Api.Tests;

/// <summary>
/// Boots the real API in-process but swaps PostgreSQL for a private SQLite
/// in-memory database, so endpoint and data-access behaviour is exercised
/// end-to-end without Docker. Each instance owns a fresh, isolated database;
/// create one per test for isolation.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    // A single kept-open connection keeps the in-memory database alive for the
    // lifetime of the factory (it is dropped when the last connection closes).
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Drop the production Npgsql registration entirely — both the built
            // options and the internal options-configuration action EF adds per
            // AddDbContext call — so SQLite is the only configured provider.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition().Name
                        .StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && d.ServiceType.GenericTypeArguments is [var arg] && arg == typeof(AppDbContext)))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
