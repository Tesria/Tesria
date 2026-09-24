using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Settings;
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

    public static async Task EnsureAsync(
        AppDbContext db, PermissionCache cache, ISiteSettingsService settings, ILogger logger,
        CancellationToken ct = default)
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

        var added = await GrantNewRightsAsync(db, settings, ct);

        cache.Invalidate();
        if (created.Count > 0 || unattached.Count > 0 || added.Count > 0)
            logger.LogInformation(
                "Instance roles seeded: created {Created}, attached {Attached} account(s), new rights {Added}",
                created.Count == 0 ? "none" : string.Join(", ", created), unattached.Count,
                added.Count == 0 ? "none" : string.Join(", ", added));
    }

    /// <summary>
    /// Hands out rights the catalog has gained since the last start, to the
    /// built-in roles whose defaults include them.
    ///
    /// Without this a right added in a later release would arrive switched
    /// off for everyone, the owner included, and nobody would know to turn it
    /// on. The record of what has already been handed out is what keeps this
    /// from undoing a deliberate removal: a key that has been seeded once is
    /// never granted again.
    /// </summary>
    private static async Task<List<string>> GrantNewRightsAsync(
        AppDbContext db, ISiteSettingsService settings, CancellationToken ct)
    {
        // Read from the row, not through the settings cache: at startup the
        // cache is cold anyway, and a stale copy here would silently skip a
        // release's new rights.
        var stored = await db.SiteSettings.AsNoTracking()
            .Select(x => x.SeededPermissionKeys)
            .FirstOrDefaultAsync(ct);
        var seeded = stored is { } json
            ? JsonSerializer.Deserialize<string[]>(json) ?? []
            : [];
        var known = seeded.ToHashSet();
        var fresh = InstancePermissions.All.Where(p => !known.Contains(p.Key)).ToList();
        if (fresh.Count == 0) return [];

        // Only when there is a record to compare against. The first run after
        // this record existed finds every key "new" although none is: the
        // roles were built from the same catalog, and an owner may already
        // have taken rights away. Recording them without granting anything is
        // the only reading that cannot undo a decision.
        var builtIns = await db.Roles.Include(r => r.Permissions).Where(r => r.Key != null).ToListAsync(ct);
        var granting = seeded.Length > 0;
        if (granting)
            foreach (var permission in fresh)
                foreach (var role in builtIns.Where(r => r.Tier >= permission.DefaultFrom))
                    if (role.Permissions.All(p => p.Key != permission.Key))
                        role.Permissions.Add(new RolePermission { RoleId = role.Id, Key = permission.Key });

        await db.SaveChangesAsync(ct);
        await settings.UpdateAsync(
            s => s.SeededPermissionKeys = JsonSerializer.Serialize(InstancePermissions.All.Select(p => p.Key)),
            actorId: null, ct);
        return granting ? [.. fresh.Select(p => p.Key)] : [];
    }

    /// <summary>
    /// Takes administration rights away from user-tier roles (dev-plan 15.1),
    /// once, for an instance that gave some before the rule. Recorded in the
    /// audit log, one entry per role, so the owner can see what changed.
    /// </summary>
    public static async Task StripAdministrationRightsFromUserTierAsync(
        AppDbContext db, Audit.IAuditLogger audit, PermissionCache cache, ILogger logger, CancellationToken ct = default)
    {
        var roles = await db.Roles.Include(r => r.Permissions).Where(r => r.Tier == UserRole.Member).ToListAsync(ct);
        var changed = false;
        foreach (var role in roles)
        {
            var removed = role.Permissions.Where(p => !InstancePermissions.AllowedInTier(p.Key, UserRole.Member)).Select(p => p.Key).Order().ToArray();
            if (removed.Length == 0) continue;
            role.Permissions.RemoveAll(p => removed.Contains(p.Key));
            audit.Record("permissions.changed", "role", role.Id,
                new { role.Name, Added = Array.Empty<string>(), Removed = removed, Reason = "Administration rights belong to administrator roles" });
            logger.LogWarning("Removed administration rights {Removed} from the user-tier role {Role}.", string.Join(", ", removed), role.Name);
            changed = true;
        }
        if (!changed) return;
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }
}
