using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// A throwaway database on a real PostgreSQL server, for the few tests that
/// depend on what SQLite cannot do: above all, enforcing the app role's
/// grants (the review's DATA-04, 2026-09-24). The server comes from
/// <c>TESRIA_TEST_POSTGRES</c>, a connection string for a role that may create
/// databases and roles (CI runs a postgres service for it); without it these
/// tests are skipped.
///
/// Each instance creates its own database and drops it afterwards. The app
/// role is shared, because roles belong to the server rather than a
/// database, and its grants are per database anyway.
/// </summary>
public sealed class PostgresTestDatabase : IDisposable
{
    public static string? Server => Environment.GetEnvironmentVariable("TESRIA_TEST_POSTGRES");

    private const string AppRole = "tesria_test_app";
    private const string AppPassword = "tesria-test-app";

    private readonly string _name = $"tesria_test_{Guid.NewGuid():N}";

    public string OwnerConnection { get; }
    public string AppConnection { get; }

    public PostgresTestDatabase()
    {
        var server = Server ?? throw new InvalidOperationException("TESRIA_TEST_POSTGRES is not set.");
        using (var admin = new NpgsqlConnection(server))
        {
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", admin);
            create.ExecuteNonQuery();
        }
        OwnerConnection = new NpgsqlConnectionStringBuilder(server) { Database = _name }.ConnectionString;
        AppConnection = new NpgsqlConnectionStringBuilder(server)
        {
            Database = _name, Username = AppRole, Password = AppPassword,
        }.ConnectionString;
    }

    /// <summary>
    /// A context on the owner's connection, for seeding what the app role may
    /// not write (the backup agents' tables) and for reading behind its back.
    /// </summary>
    public AppDbContext Owner() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(OwnerConnection).Options);

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        using var admin = new NpgsqlConnection(Server);
        admin.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", admin);
        drop.ExecuteNonQuery();
    }
}

/// <summary>A test that needs <see cref="PostgresTestDatabase"/>, skipped when no server is configured.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(PostgresTestDatabase.Server))
            Skip = "Needs PostgreSQL: set TESRIA_TEST_POSTGRES to a connection string for a server it may create databases on.";
    }
}
