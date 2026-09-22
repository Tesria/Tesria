using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// The least-privilege runtime database role (dev-plan 3.1).
///
/// Two connections: the <em>owner</em> (<c>ConnectionStrings:Default</c>, the
/// instance superuser) is used once at startup to apply migrations and to
/// provision the <em>app</em> role (<c>ConnectionStrings:App</c>), which the
/// running app then uses for everything else. The app role can read and write
/// every table but cannot UPDATE or DELETE the append-only ones, so a
/// compromised app can add audit rows but never remove or alter them.
///
/// Provisioned by the app itself rather than a database init script because
/// init scripts only run on a fresh volume: every existing install would have
/// been left on the superuser. Re-run on every start, the grants also cover
/// tables that later migrations add. Rotation is "change the password in
/// .env and restart".
/// </summary>
public static partial class DatabaseRoles
{
    /// <summary>Tables the runtime role may append to but never change.</summary>
    public static readonly string[] AppendOnlyTables = ["AuditLogs", "PageViews", "SecurityEvents", "BackupJobs"];

    /// <summary>
    /// Tables the runtime role may read but never write (dev-plan 9.1). The
    /// backup sidecars own them; a compromised app must not be able to make
    /// a failed backup look healthy.
    /// </summary>
    public static readonly string[] ReadOnlyTables = ["BackupAgents", "Backups", "BackupTargets"];

    /// <summary>
    /// Which connection the running app should use. The app connection wins
    /// when it names a role and a password; otherwise the owner connection is
    /// used and a warning says so, because a half-configured split must not
    /// brick startup on an install that worked yesterday.
    /// </summary>
    public static string ChooseRuntimeConnection(string owner, string? app, ILogger logger)
    {
        if (TryParseApp(app, out _, out _)) return app!;

        logger.LogWarning(
            "No app database role configured (ConnectionStrings:App / APP_DB_PASSWORD): " +
            "running as the database owner. Set APP_DB_PASSWORD before exposing this instance.");
        return owner;
    }

    public static bool TryParseApp(string? app, out string role, out string password)
    {
        role = ""; password = "";
        if (string.IsNullOrWhiteSpace(app)) return false;
        NpgsqlConnectionStringBuilder b;
        try { b = new NpgsqlConnectionStringBuilder(app); }
        catch (ArgumentException) { return false; }
        if (string.IsNullOrEmpty(b.Username) || string.IsNullOrEmpty(b.Password)) return false;
        role = b.Username; password = b.Password;
        return true;
    }

    /// <summary>
    /// Creates or updates the app role and its grants. Runs on the owner
    /// connection, after migrations, so every table exists.
    /// </summary>
    public static async Task EnsureAppRoleAsync(DbContext ownerDb, string appConnection, ILogger logger, CancellationToken ct = default)
    {
        if (!TryParseApp(appConnection, out var role, out var password)) return;
        if (!RoleName().IsMatch(role))
            throw new InvalidOperationException($"App database role '{role}' must match [a-z_][a-z0-9_]*.");

        var database = new NpgsqlConnectionStringBuilder(appConnection).Database
            ?? throw new InvalidOperationException("ConnectionStrings:App names no database.");

        // Role names are validated above; the password is a literal and is
        // escaped by doubling quotes (Postgres has no bind parameters for
        // ALTER ROLE). Both go through the owner connection only.
        var literalPassword = password.Replace("'", "''");

        var sql = new List<string>
        {
            $"""
             DO $$ BEGIN
               IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                 CREATE ROLE "{role}";
               END IF;
             END $$
             """,
            $"ALTER ROLE \"{role}\" WITH LOGIN PASSWORD '{literalPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS",
            $"GRANT CONNECT ON DATABASE \"{database}\" TO \"{role}\"",
            $"GRANT USAGE ON SCHEMA public TO \"{role}\"",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \"{role}\"",
            $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO \"{role}\"",
            // Migrations are the owner's job; the app only needs to read the history.
            $"REVOKE ALL ON \"__EFMigrationsHistory\" FROM \"{role}\"",
            $"GRANT SELECT ON \"__EFMigrationsHistory\" TO \"{role}\"",
        };
        foreach (var table in AppendOnlyTables)
            sql.Add($"REVOKE UPDATE, DELETE, TRUNCATE ON \"{table}\" FROM \"{role}\"");
        foreach (var table in ReadOnlyTables)
            sql.Add($"REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON \"{table}\" FROM \"{role}\"");

        foreach (var statement in sql)
            await ownerDb.Database.ExecuteSqlRawAsync(statement, ct);

        logger.LogInformation("Database role {Role} provisioned; append-only: {Tables}; read-only: {ReadOnly}",
            role, string.Join(", ", AppendOnlyTables), string.Join(", ", ReadOnlyTables));
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex RoleName();
}
