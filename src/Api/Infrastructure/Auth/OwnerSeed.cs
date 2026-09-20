using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Gives an existing instance its owner on the first start after dev-plan
/// 10.1 (a fresh one gets its owner from the first account to register).
///
/// A startup step rather than SQL in the migration, because the promotion is
/// an administrative act and has to be in the audit log, which means going
/// through <see cref="AppDbContext.SaveChangesAsync"/> so the entry is
/// chained.
/// </summary>
public static class OwnerSeed
{
    /// <summary>
    /// Promotes the longest-standing administrator, preferring an active one.
    /// Returns the account that now owns the instance, or null when there was
    /// nothing to do.
    /// </summary>
    public static async Task<User?> EnsureAsync(
        AppDbContext db, IAuditLogger audit, ISiteSettingsService settings, ILogger logger,
        CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Owner, ct)) return null;

        // Ordered in memory: the SQLite provider used by the tests cannot
        // ORDER BY a DateTimeOffset, and this runs once per start.
        var candidates = await db.Users
            .Where(u => u.Role == UserRole.Admin)
            .ToListAsync(ct);
        var owner = candidates.Where(u => u.Status == UserStatus.Active).OrderBy(u => u.CreatedAt).FirstOrDefault()
            ?? candidates.OrderBy(u => u.CreatedAt).FirstOrDefault()
            // An instance with members but no administrator should not exist,
            // and if it does, leaving it with no owner leaves it unusable.
            ?? (await db.Users.ToListAsync(ct)).OrderBy(u => u.CreatedAt).FirstOrDefault();
        if (owner is null) return null;

        owner.Role = UserRole.Owner;
        audit.RecordAs(null, "owner.assigned", "user", owner.Id,
            new { Source = "upgrade", owner.Email });
        await db.SaveChangesAsync(ct);

        // This instance was set up long before the setup wizard existed
        // (dev-plan 10.2); it must not be sent through it.
        if ((await settings.GetAsync(ct)).SetupCompletedAt is null)
            await settings.UpdateAsync(s => s.SetupCompletedAt = DateTimeOffset.UtcNow, actorId: null, ct);

        logger.LogInformation("Instance owner assigned on upgrade: {Email}", owner.Email);
        return owner;
    }
}
