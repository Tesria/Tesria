using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The audit log's hash chain (dev-plan 3.1). The role split that stops the
/// app from editing these rows cannot run on SQLite; what can be proven here
/// is that any edit or deletion, however it happened, is detected.
/// </summary>
public class AuditChainTests
{
    private record Report(bool Ok, long Checked, long Unchained, long? BrokenAtSequence, string? Problem);

    private static async Task RegisterAsync(HttpClient client, string email) =>
        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .EnsureSuccessStatusCode();

    private static AppDbContext Db(TestAppFactory factory, out IServiceScope scope)
    {
        scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    [Fact]
    public async Task Every_audit_row_is_linked_in_order_and_the_chain_verifies()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        // Registration is not audited; space creation is, once per space.
        await admin.CreateSpaceAsync();
        await admin.CreateSpaceAsync();
        await admin.CreateSpaceAsync();

        var db = Db(factory, out var scope);
        using (scope)
        {
            var rows = (await db.AuditLogs.AsNoTracking().ToListAsync()).OrderBy(a => a.Sequence).ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal(Enumerable.Range(1, rows.Count).Select(i => (long?)i), rows.Select(r => r.Sequence));
            Assert.Equal(AuditChain.GenesisHash, rows[0].PrevHash);
            for (var i = 1; i < rows.Count; i++) Assert.Equal(rows[i - 1].Hash, rows[i].PrevHash);
        }

        var report = await (await admin.PostAsync("/api/admin/audit/verify", null))
            .Content.ReadFromJsonAsync<Report>();
        Assert.True(report!.Ok, report.Problem);
        Assert.Equal(0, report.Unchained);
    }

    [Fact]
    public async Task Altering_a_row_is_detected_at_that_row()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        await admin.CreateSpaceAsync();
        await admin.CreateSpaceAsync();
        await admin.CreateSpaceAsync();

        long target;
        var db = Db(factory, out var scope);
        using (scope)
        {
            // Something an attacker with the owner password might do: make a
            // damning entry look innocuous.
            var row = await db.AuditLogs.OrderBy(a => a.Sequence).Skip(1).FirstAsync();
            target = row.Sequence!.Value;
            row.Action = "space.viewed";
            await db.SaveChangesAsync();
        }

        var report = await (await admin.PostAsync("/api/admin/audit/verify", null))
            .Content.ReadFromJsonAsync<Report>();
        Assert.False(report!.Ok);
        Assert.Equal(target, report.BrokenAtSequence);
        Assert.Contains("altered", report.Problem);
    }

    [Fact]
    public async Task Deleting_a_row_is_detected_as_a_gap()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        await admin.CreateSpaceAsync();
        await admin.CreateSpaceAsync();
        await admin.CreateSpaceAsync();

        long removed;
        var db = Db(factory, out var scope);
        using (scope)
        {
            var row = await db.AuditLogs.OrderBy(a => a.Sequence).Skip(1).FirstAsync();
            removed = row.Sequence!.Value;
            db.AuditLogs.Remove(row);
            await db.SaveChangesAsync();
        }

        var report = await (await admin.PostAsync("/api/admin/audit/verify", null))
            .Content.ReadFromJsonAsync<Report>();
        Assert.False(report!.Ok);
        Assert.Equal(removed + 1, report.BrokenAtSequence);
        Assert.Contains("missing", report.Problem);
    }

    [Fact]
    public async Task Rows_from_before_the_chain_are_linked_by_the_backfill()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");

        var db = Db(factory, out var scope);
        using (scope)
        {
            // A row written by an older version: no sequence, no hash. Inserted
            // raw so SaveChanges cannot chain it on the way in.
            var id = Guid.NewGuid();
            var when = DateTimeOffset.UtcNow.AddDays(-1);
            var metadata = """{"b":1,"a":"x"}""";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "AuditLogs" ("Id","ActorId","Action","TargetType","TargetId","MetadataJson","CreatedAt")
                VALUES ({id}, NULL, 'legacy.action', 'instance', NULL, {metadata}, {when})
                """);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Sequence == null));

            Assert.Equal(1, await AuditChain.BackfillAsync(db));
            Assert.Equal(0, await db.AuditLogs.CountAsync(a => a.Sequence == null));
        }

        var report = await (await admin.PostAsync("/api/admin/audit/verify", null))
            .Content.ReadFromJsonAsync<Report>();
        Assert.True(report!.Ok, report.Problem);
    }

    [Fact]
    public void Canonical_json_ignores_key_order_whitespace_and_number_spelling()
    {
        // jsonb re-orders keys, strips whitespace and normalises numbers; the
        // hash must be over what reads back, not what was written.
        var written = AuditChain.CanonicalJson("""{ "z": 1e2, "a": { "y": [1.50, "é"], "x": true } }""");
        var readBack = AuditChain.CanonicalJson("""{"a":{"x":true,"y":[1.50,"é"]},"z":100}""");
        Assert.Equal(written, readBack);
    }

    [Fact]
    public async Task Only_an_administrator_can_run_verification()
    {
        using var factory = new TestAppFactory();
        await RegisterAsync(factory.CreateClient(), "admin@example.com");
        var member = factory.CreateClient();
        await RegisterAsync(member, "member@example.com");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsync("/api/admin/audit/verify", null)).StatusCode);
    }
}
