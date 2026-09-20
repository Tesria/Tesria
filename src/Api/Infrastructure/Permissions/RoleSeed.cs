using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>
/// Creates the three built-in roles and attaches every account to one
/// (dev-plan 11.1).
///
/// Runs at startup beside the other seeds. It creates what is missing and
/// never touches what exists: an owner who has removed a right from
/// Administrator must not find it back after a restart.
/// </summary>
public static class RoleSeed
{
    /// <summary>
    /// The built-in role of a tier. Used wherever a tier changes, so the role
    /// travels with it and the two never disagree.
    /// </summary>
    public static async Task<Guid?> BuiltInIdAsync(AppDbContext db, UserRole tier, CancellationToken ct = default) =>
        await db.Roles.AsNoTracking()
            .Where(r => r.Key == Role.Keys.For(tier))
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

    public static async Task EnsureAsync(AppDbContext db, PermissionCache cache, ILogger logger, CancellationToken ct = default)
    {
        var existing = await db.Roles.Where(r => r.Key != null).ToListAsync(ct);
        var created = new List<string>();

        foreach (var tier in new[] { UserRole.Member, UserRole.Admin, UserRole.Owner })
        {
            var key = Role.Keys.For(tier);
            if (existing.Any(r => r.Key == key)) continue;

            var role = new Role
            {
                Id = Guid.NewGuid(),
                Key = key,
                Name = Role.NameFor(tier),
                Description = tier switch
                {
                    UserRole.Owner => "The account that owns this instance.",
                    UserRole.Admin => "Runs the instance day to day.",
                    _ => "Reads and writes content.",
                },
                Tier = tier,
                BuiltIn = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            role.Permissions = [.. InstancePermissions.DefaultsFor(tier)
                .Select(k => new RolePermission { RoleId = role.Id, Key = k })];
            db.Roles.Add(role);
            created.Add(key);
        }

        if (created.Count > 0) await db.SaveChangesAsync(ct);

        // Attach anyone the seed has not reached yet, including every account
        // on an instance upgrading to 11.1.
        var builtIns = await db.Roles.AsNoTracking()
            .Where(r => r.Key != null)
            .Select(r => new { r.Id, r.Tier })
            .ToListAsync(ct);
        var unattached = await db.Users.Where(u => u.RoleId == null).ToListAsync(ct);
        foreach (var user in unattached)
            user.RoleId = builtIns.First(r => r.Tier == user.Role).Id;
        if (unattached.Count > 0) await db.SaveChangesAsync(ct);

        cache.Invalidate();
        if (created.Count > 0 || unattached.Count > 0)
            logger.LogInformation(
                "Instance roles seeded: created {Created}, attached {Attached} account(s)",
                created.Count == 0 ? "none" : string.Join(", ", created), unattached.Count);
    }
}
