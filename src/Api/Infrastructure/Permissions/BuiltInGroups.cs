using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>
/// The three groups every instance has (dev-plan 15.1, the owner's request):
/// Owner, Admins and Users. They cannot be renamed, deleted, or have members
/// added or removed, because their membership is not stored at all: it is the
/// account's tier at the moment of the check, nested the way the owner chose.
///
///   Owner  the owner
///   Admins administrators and the owner
///   Users  every active account
///
/// Fixed ids, so a grant to "Users" means the same thing on every instance
/// and survives anything that renames rows.
/// </summary>
public static class BuiltInGroups
{
    public static readonly Guid OwnerId = new("00000000-0000-0000-0001-000000000001");
    public static readonly Guid AdminsId = new("00000000-0000-0000-0001-000000000002");
    public static readonly Guid UsersId = new("00000000-0000-0000-0001-000000000003");

    private static readonly (Guid Id, string Name, string Description)[] All =
    [
        (OwnerId, "Owner", "The owner of this instance. Built in: its member is always the owner."),
        (AdminsId, "Admins", "Every administrator, and the owner. Built in: follows who is an administrator."),
        (UsersId, "Users", "Everyone with an account. Built in: follows who has an account."),
    ];

    public static bool IsBuiltIn(Guid groupId) => groupId == OwnerId || groupId == AdminsId || groupId == UsersId;

    /// <summary>The built-in groups an account of this tier is in.</summary>
    public static IEnumerable<Guid> For(UserRole tier)
    {
        yield return UsersId;
        if (tier >= UserRole.Admin) yield return AdminsId;
        if (tier == UserRole.Owner) yield return OwnerId;
    }

    /// <summary>Who is in a built-in group, as a query over accounts.</summary>
    public static IQueryable<User> Members(AppDbContext db, Guid groupId) => db.Users.Where(u =>
        u.Status == UserStatus.Active &&
        (groupId == UsersId
            || (groupId == AdminsId && u.Role >= UserRole.Admin)
            || (groupId == OwnerId && u.Role == UserRole.Owner)));

    /// <summary>
    /// Creates the three on the first start that knows about them. A group
    /// someone already made with one of these names is renamed, with an
    /// audit entry, rather than silently shadowed.
    /// </summary>
    public static async Task EnsureAsync(AppDbContext db, Audit.IAuditLogger audit, ILogger log)
    {
        var changed = false;
        foreach (var (id, name, description) in All)
        {
            if (await db.Groups.AnyAsync(g => g.Id == id)) continue;
            var normalized = name.ToLowerInvariant();
            var clash = await db.Groups.FirstOrDefaultAsync(g => g.NormalizedName == normalized);
            if (clash is not null)
            {
                var renamed = $"{clash.Name} (renamed)";
                audit.Record("group.renamed_for_builtin", "group", clash.Id, new { From = clash.Name, To = renamed });
                log.LogWarning("Renamed the group {Name} to {Renamed}: the built-in group {Builtin} needs the name.", clash.Name, renamed, name);
                clash.Name = renamed;
                clash.NormalizedName = renamed.ToLowerInvariant();
            }
            db.Groups.Add(new Group
            {
                Id = id, Name = name, NormalizedName = normalized, Description = description,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            changed = true;
        }
        if (changed) await db.SaveChangesAsync();
    }
}
