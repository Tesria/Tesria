using Microsoft.EntityFrameworkCore;
using Tesria.Api.Infrastructure.Audit;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// The one-shot `migrate` step (dev-plan 14.3): everything that needs the
/// database owner, and nothing else. Docker Compose runs it (the app image,
/// with <c>--migrate</c>) before the app starts, and it exits; the owner's
/// password is then in no running container but the backup services, which
/// need it to dump and archive. It applies the migrations, links any audit
/// rows from before the audit chain existed (an UPDATE the app role may not
/// make), and creates or updates the app's least-privilege role and grants.
/// The seeds stay in the app: they write only what the app role may write.
/// </summary>
public static class MigrateCommand
{
    public static async Task<int> RunAsync(string ownerConnection, string? appConnection, ILogger log, CancellationToken ct = default)
    {
        try
        {
            // Unpooled: nothing of the owner's connection outlives this step.
            var unpooled = new Npgsql.NpgsqlConnectionStringBuilder(ownerConnection) { Pooling = false }.ConnectionString;
            await using var owner = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(unpooled).Options);
            var pending = (await owner.Database.GetPendingMigrationsAsync(ct)).ToList();
            await owner.Database.MigrateAsync(ct);
            log.LogInformation("Migrations applied: {Count}", pending.Count);
            var chained = await AuditChain.BackfillAsync(owner);
            if (chained > 0) log.LogInformation("Audit chain: linked {Count} pre-existing rows", chained);
            if (appConnection is not null)
                await DatabaseRoles.EnsureAppRoleAsync(owner, appConnection, log, ct);
            return 0;
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Migrating the database failed; the app will not start until this succeeds.");
            return 1;
        }
    }
}
