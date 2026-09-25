using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tesria.Api.Infrastructure.Audit;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// The `migrate` service (dev-plan 14.3, long-running since 14.4): everything
/// that needs the database owner, and nothing else. Docker Compose runs it
/// (the app image, with <c>--migrate</c>) before the app starts; the owner's
/// password is then in no running container but this one and the backup
/// services, which need it to dump and archive. A pass applies the
/// migrations, links any audit rows from before the audit chain existed (an
/// UPDATE the app role may not make), and creates or updates the app's
/// least-privilege role and grants. The seeds stay in the app: they write
/// only what the app role may write.
///
/// With <c>--watch</c> it stays up after the first pass (the review's
/// DATA-01, 2026-09-24). A restore used to rely on the app re-granting
/// itself at startup, which 14.3 took away, so a restored database came back
/// with no grants for the app and, from an older version, missing
/// migrations. Now:
/// <list type="bullet">
/// <item>A logical restore asks for a pass on the restored copy before it
/// replaces anything, and waits for the answer. The request and the answer
/// are the copy's database comment (<see cref="Requested"/>): both sides
/// already hold the owner's connection, and the signal lives on the very
/// database it is about.</item>
/// <item>Every thirty seconds it checks the live database (migrations
/// pending, or the app role unable to read <c>Pages</c>) and runs a pass
/// when either is so. That covers a point-in-time restore, a restore run by
/// hand, and a request that was lost.</item>
/// </list>
/// Its only inputs are a database comment only the owner can set and the
/// live database's own state; its only action is the pass it runs at start.
/// </summary>
public static class MigrateCommand
{
    /// <summary>The restore script's request, followed by the job id (or <c>manual</c>).</summary>
    public const string Requested = "tesria-maintenance requested ";
    public const string Done = "tesria-maintenance done ";
    public const string Failed = "tesria-maintenance failed ";

    /// <summary>Marker the compose health check looks for: the first pass succeeded.</summary>
    public const string ReadyMarker = "/tmp/tesria-migrated";

    private static readonly TimeSpan RequestPoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SelfCheck = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(string ownerConnection, string? appConnection, ILogger log, CancellationToken ct = default)
    {
        try
        {
            await PassAsync(ownerConnection, appConnection, log, ct);
            return 0;
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Migrating the database failed; the app will not start until this succeeds.");
            return 1;
        }
    }

    /// <summary>One pass: migrations, the audit chain backfill, the app role and its grants.</summary>
    public static async Task PassAsync(string ownerConnection, string? appConnection, ILogger log, CancellationToken ct = default)
    {
        // Unpooled: nothing of the owner's connection outlives this step. A
        // pooled connection left open on a database would also stop the
        // restore renaming it.
        await using var owner = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Unpooled(ownerConnection)).Options);
        var pending = (await owner.Database.GetPendingMigrationsAsync(ct)).ToList();
        await owner.Database.MigrateAsync(ct);
        log.LogInformation("Migrations applied: {Count}", pending.Count);
        var chained = await AuditChain.BackfillAsync(owner);
        if (chained > 0) log.LogInformation("Audit chain: linked {Count} pre-existing rows", chained);
        if (appConnection is not null)
            await DatabaseRoles.EnsureAppRoleAsync(owner, appConnection, log, ct);
    }

    /// <summary>
    /// The first pass, then the watch, until the process is told to stop. A
    /// first pass that fails ends the process with a failure, so
    /// <c>docker compose up</c> reports it rather than the app waiting on it.
    /// </summary>
    public static async Task<int> WatchAsync(
        string ownerConnection, string? appConnection, ILogger log, CancellationToken ct,
        TimeSpan? selfCheck = null, string readyMarker = ReadyMarker)
    {
        var every = selfCheck ?? SelfCheck;
        if (await RunAsync(ownerConnection, appConnection, log, ct) != 0) return 1;
        await File.WriteAllTextAsync(readyMarker, DateTimeOffset.UtcNow.ToString("O"), ct);
        log.LogInformation("Watching for restores and for a database that needs a pass");

        var live = new NpgsqlConnectionStringBuilder(ownerConnection).Database
            ?? throw new InvalidOperationException("ConnectionStrings:Default names no database.");
        var restoreDb = $"{live}_restore";
        var nextCheck = DateTimeOffset.UtcNow + every;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ServeRequestAsync(ownerConnection, appConnection, restoreDb, log, ct);
                if (DateTimeOffset.UtcNow >= nextCheck)
                {
                    nextCheck = DateTimeOffset.UtcNow + every;
                    if (await NeedsPassAsync(ownerConnection, appConnection, ct) is { } why)
                    {
                        log.LogWarning("The live database needs a pass ({Why}); running it", why);
                        await PassAsync(ownerConnection, appConnection, log, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Expected for a few seconds during a restore's swap, when the
                // live database refuses connections. Anything lasting shows up
                // as the same line every thirty seconds.
                log.LogWarning("Database check failed: {Error}", ex.Message);
            }
            try { await Task.Delay(RequestPoll, ct); } catch (OperationCanceledException) { break; }
        }
        return 0;
    }

    /// <summary>Runs the pass a restore asked for on its restored copy, and answers in the same comment.</summary>
    private static async Task ServeRequestAsync(
        string ownerConnection, string? appConnection, string restoreDb, ILogger log, CancellationToken ct)
    {
        var comment = await CommentAsync(ownerConnection, restoreDb, ct);
        if (comment is null || !comment.StartsWith(Requested, StringComparison.Ordinal)) return;
        var job = comment[Requested.Length..].Trim();

        log.LogInformation("Restore {Job} asked for a pass on {Database}", job, restoreDb);
        string answer;
        try
        {
            await PassAsync(On(ownerConnection, restoreDb), appConnection is null ? null : On(appConnection, restoreDb), log, ct);
            answer = Done + job;
            log.LogInformation("Restore {Job}: {Database} is up to date and granted", job, restoreDb);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Restore {Job}: the pass on {Database} failed", job, restoreDb);
            var message = ex.GetBaseException().Message.ReplaceLineEndings(" ");
            answer = $"{Failed}{job}: {(message.Length > 400 ? message[..400] : message)}";
        }
        await SetCommentAsync(ownerConnection, restoreDb, answer, ct);
    }

    /// <summary>Why the live database needs a pass, or null when it does not.</summary>
    private static async Task<string?> NeedsPassAsync(string ownerConnection, string? appConnection, CancellationToken ct)
    {
        await using (var owner = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Unpooled(ownerConnection)).Options))
        {
            var pending = (await owner.Database.GetPendingMigrationsAsync(ct)).Count();
            if (pending > 0) return $"{pending} migration(s) pending";
        }
        if (!DatabaseRoles.TryParseApp(appConnection, out var role, out _)) return null;

        await using var conn = new NpgsqlConnection(Unpooled(ownerConnection));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            // CASE, not AND: SQL does not promise to stop at the first false,
            // and has_table_privilege throws for a role or table that is not there.
            "SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role) THEN false " +
            "WHEN to_regclass('public.\"Pages\"') IS NULL THEN false " +
            "ELSE has_table_privilege(@role, 'public.\"Pages\"', 'SELECT') END", conn);
        cmd.Parameters.AddWithValue("role", role);
        return await cmd.ExecuteScalarAsync(ct) is true ? null : $"the {role} role cannot read Pages";
    }

    private static async Task<string?> CommentAsync(string ownerConnection, string database, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(On(ownerConnection, "postgres"));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = @db", conn);
        cmd.Parameters.AddWithValue("db", database);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task SetCommentAsync(string ownerConnection, string database, string comment, CancellationToken ct)
    {
        // COMMENT takes no parameters. The name is ours (the live database's
        // plus "_restore") and the text has its quotes doubled.
        await using var conn = new NpgsqlConnection(On(ownerConnection, "postgres"));
        await conn.OpenAsync(ct);
        var name = database.Replace("\"", "\"\"");
        await using var cmd = new NpgsqlCommand(
            $"COMMENT ON DATABASE \"{name}\" IS '{comment.Replace("'", "''")}'", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Unpooled(string connection) =>
        new NpgsqlConnectionStringBuilder(connection) { Pooling = false }.ConnectionString;

    private static string On(string connection, string database) =>
        new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString;
}
