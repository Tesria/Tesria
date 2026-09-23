using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tesria.Api.Infrastructure.Audit;

/// <summary>
/// The audit log's hash chain (dev-plan 3.1).
///
/// Each row carries <c>Hash = SHA-256(PrevHash ‖ canonical row)</c>, where
/// <c>PrevHash</c> is the previous row's hash. Editing a row changes its hash;
/// deleting one leaves a gap in the sequence and a successor whose
/// <c>PrevHash</c> no longer matches. Neither can be hidden without rewriting
/// every later row, and the same rows are streamed to stdout as they commit
/// (<see cref="EmitToLog"/>), so a copy exists that never touched the database.
///
/// This makes tampering <em>detectable</em>. The role split in
/// <see cref="Security.DatabaseRoles"/> makes it <em>hard</em> (the runtime
/// role cannot UPDATE or DELETE these rows at all), and point-in-time recovery
/// makes it <em>recoverable</em>. The three are meant to be read together.
/// </summary>
public static class AuditChain
{
    /// <summary>Sixty-four zeros: what row 1 links back to.</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// A transaction-scoped advisory lock serializing appenders. The number is
    /// arbitrary and only has to be unique among advisory locks this app takes.
    /// </summary>
    public const string LockSql = "SELECT pg_advisory_xact_lock(731129)";

    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    public static List<AuditLog> PendingRows(DbContext db) =>
        db.ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

    /// <summary>Assigns sequence numbers and hashes to new rows, continuing from the tail.</summary>
    public static void Link(IEnumerable<AuditLog> rows, long? tailSequence, string? tailHash)
    {
        var sequence = tailSequence ?? 0;
        var prev = tailHash ?? GenesisHash;

        // A stable order within one save: by time, then id, the same order the
        // backfill uses, so the chain is reproducible from the rows alone.
        foreach (var row in rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id))
        {
            // Postgres keeps microseconds; .NET keeps 100 ns ticks. Truncate at
            // write time so the value hashed is the value that comes back.
            row.CreatedAt = TruncateToMilliseconds(row.CreatedAt);
            row.Sequence = ++sequence;
            row.PrevHash = prev;
            row.Hash = Hash(prev, Canonical(row));
            prev = row.Hash;
        }
    }

    public static string Hash(string prevHash, string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prevHash + "\n" + canonical)));

    /// <summary>
    /// The row as a newline-joined string of fixed-format fields. Every field
    /// that identifies what happened is included; the hash columns themselves
    /// are not.
    /// </summary>
    public static string Canonical(AuditLog a) => string.Join('\n',
        a.Sequence?.ToString(CultureInfo.InvariantCulture) ?? "",
        a.Id.ToString("D"),
        a.ActorId?.ToString("D") ?? "",
        a.Action,
        a.TargetType,
        a.TargetId?.ToString("D") ?? "",
        CanonicalJson(a.MetadataJson),
        TruncateToMilliseconds(a.CreatedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Metadata as it will read back, not as it was written. The column is
    /// jsonb, and Postgres re-orders keys, strips whitespace and normalizes
    /// numbers on the way in, so both writing and verifying hash this form:
    /// keys sorted, no whitespace, numbers via <see cref="decimal"/>.
    /// </summary>
    public static string CanonicalJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var node = JsonNode.Parse(json);
        return Sorted(node)?.ToJsonString(CompactJson) ?? "null";
    }

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => KeyValuePair.Create(kv.Key, Sorted(kv.Value)))),
        JsonArray arr => new JsonArray(arr.Select(Sorted).ToArray()),
        JsonValue value when value.TryGetValue<decimal>(out var number) => JsonValue.Create(number),
        JsonValue value => value.DeepClone(),
        _ => null,
    };

    public static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);

    /// <summary>
    /// Writes each committed row to the <c>Tesria.Audit</c> log category as one
    /// JSON line. Whatever collects the container's stdout now holds a copy an
    /// attacker with the database password cannot reach.
    /// </summary>
    public static void EmitToLog(DbContext db, IEnumerable<AuditLog> rows)
    {
        ILogger logger;
        try
        {
            logger = db.GetService<ILoggerFactory>().CreateLogger("Tesria.Audit");
        }
        catch (InvalidOperationException)
        {
            return; // a context built outside the host (startup) has no logger
        }
        if (!logger.IsEnabled(LogLevel.Information)) return;

        foreach (var a in rows)
        {
            var line = JsonSerializer.Serialize(new
            {
                seq = a.Sequence,
                id = a.Id,
                actor = a.ActorId,
                action = a.Action,
                target = a.TargetType,
                targetId = a.TargetId,
                metadata = a.MetadataJson is null ? null : JsonNode.Parse(a.MetadataJson),
                at = a.CreatedAt,
                prev = a.PrevHash,
                hash = a.Hash,
            }, CompactJson);
            logger.LogInformation("{AuditJson}", line);
        }
    }

    /// <summary>
    /// Chains rows written before the chain existed, in time order, from the
    /// current tail. Runs at startup on the owner connection, the only one
    /// allowed to UPDATE this table, and does nothing once every row is linked.
    /// </summary>
    public static async Task<int> BackfillAsync(AppDbContext db, CancellationToken ct = default)
    {
        var legacy = await db.AuditLogs.Where(a => a.Sequence == null).ToListAsync(ct);
        if (legacy.Count == 0) return 0;

        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync(LockSql, ct);

        var tail = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Sequence != null)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.Sequence, a.Hash })
            .FirstOrDefaultAsync(ct);

        Link(legacy, tail?.Sequence, tail?.Hash);
        await db.SaveChangesAsync(ct);
        return legacy.Count;
    }
}

public record AuditChainReport(
    bool Ok, long Checked, long Unchained, long? BrokenAtSequence, string? Problem, DateTimeOffset VerifiedAt);

public interface IAuditChainVerifier
{
    /// <summary>Walks the whole chain and reports the first link that does not hold.</summary>
    Task<AuditChainReport> VerifyAsync(CancellationToken ct = default);
}

public sealed class AuditChainVerifier(AppDbContext db) : IAuditChainVerifier
{
    private const int Batch = 500;

    public async Task<AuditChainReport> VerifyAsync(CancellationToken ct = default)
    {
        var unchained = await db.AuditLogs.LongCountAsync(a => a.Sequence == null, ct);

        long checkedRows = 0;
        long cursor = 0;
        var prev = AuditChain.GenesisHash;

        while (true)
        {
            var rows = await db.AuditLogs.AsNoTracking()
                .Where(a => a.Sequence != null && a.Sequence > cursor)
                .OrderBy(a => a.Sequence)
                .Take(Batch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                var seq = row.Sequence!.Value;
                if (seq != cursor + 1)
                    return Broken(checkedRows, unchained, seq,
                        $"Sequence jumps from {cursor} to {seq}: {seq - cursor - 1} row(s) missing.");
                if (!string.Equals(row.PrevHash, prev, StringComparison.Ordinal))
                    return Broken(checkedRows, unchained, seq, "PrevHash does not match the previous row's hash.");
                var expected = AuditChain.Hash(prev, AuditChain.Canonical(row));
                if (!string.Equals(row.Hash, expected, StringComparison.Ordinal))
                    return Broken(checkedRows, unchained, seq, "Hash does not match the row's content: the row was altered.");

                prev = row.Hash!;
                cursor = seq;
                checkedRows++;
            }
        }

        return new AuditChainReport(true, checkedRows, unchained, null, null, DateTimeOffset.UtcNow);
    }

    private static AuditChainReport Broken(long checkedRows, long unchained, long at, string problem) =>
        new(false, checkedRows, unchained, at, problem, DateTimeOffset.UtcNow);
}

/// <summary>
/// Verifies the chain once shortly after start and then daily. A failure is
/// logged as critical; dev-plan 3.3 also raises a security event from it.
/// </summary>
public sealed class AuditChainMonitor(IServiceScopeFactory scopes, ILogger<AuditChainMonitor> logger)
    : BackgroundService
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Set by 3.3 to turn a broken chain into an alert. Null means log only.</summary>
    public Func<IServiceProvider, AuditChainReport, Task>? OnBroken { get; set; }

    /// <summary>
    /// The chain proves rows were not edited or removed from the middle; it
    /// cannot prove rows were not cut off the end, because a shorter chain is
    /// still a valid one. Remembering how long it was last time closes that
    /// gap for as long as this process lives: a restart forgets, which is
    /// why the count is also in the log line every run.
    /// </summary>
    private long _lastChecked = -1;

    /// <summary>
    /// When the last verification ran, so a restore that happened since then
    /// explains a shorter chain exactly once (dev-plan 9.4).
    /// </summary>
    private DateTimeOffset _lastCheckedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var report = await scope.ServiceProvider.GetRequiredService<IAuditChainVerifier>().VerifyAsync(ct);

            // A restore legitimately shortens the chain: the wiki was replaced
            // with an older copy, and the entries after the backup went with
            // it (dev-plan 9.4). Explained rather than warned about, and only
            // for a restore this monitor has not already seen, so the excuse
            // covers exactly one verification and never stands in for a real
            // truncation. The restore itself is audited and alerted on, so
            // nothing here is being waved through unrecorded.
            var restoredAt = (await scope.ServiceProvider
                .GetRequiredService<Settings.ISiteSettingsService>().GetAsync(ct)).LastRestoredAt;
            var restored = restoredAt is { } at && at > _lastCheckedAt;

            if (report.Ok && report.Checked < _lastChecked && !restored)
                report = report with
                {
                    Ok = false,
                    Problem = $"The chain is shorter than at the last verification ({report.Checked} rows, was {_lastChecked}): rows were removed from the end.",
                };
            else if (report.Ok && report.Checked < _lastChecked)
                logger.LogInformation(
                    "Audit chain is shorter than at the last verification ({Checked} rows, was {Was}): the wiki was restored at {RestoredAt}, which removes the entries after that backup",
                    report.Checked, _lastChecked, restoredAt);

            if (report.Ok)
            {
                _lastChecked = report.Checked;
                _lastCheckedAt = DateTimeOffset.UtcNow;
                logger.LogInformation("Audit chain verified: {Checked} rows intact", report.Checked);
                return;
            }

            logger.LogCritical("AUDIT CHAIN BROKEN at sequence {Sequence}: {Problem}",
                report.BrokenAtSequence, report.Problem);
            if (OnBroken is { } onBroken) await onBroken(scope.ServiceProvider, report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Audit chain verification failed to run");
        }
    }
}
