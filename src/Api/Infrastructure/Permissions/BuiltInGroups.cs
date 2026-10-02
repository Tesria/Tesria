using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>
/// The groups every instance has. Five, of two kinds, and none of them can
/// be renamed or deleted:
/// <list type="bullet">
/// <item><b>Computed</b> (dev-plan 15.1): Owner, Admins and Users. Their
/// membership is not stored at all: it is the account's tier at the moment
/// of the check, nested as decided in 15.1, so nobody adds or removes
/// members.
///
///   Owner  the owner
///   Admins administrators and the owner
///   Users  every active account</item>
/// <item><b>Global</b> (dev-plan 21.1): Global Viewers and Global Reviewers.
/// Their members are stored, and chosen by holders of Manage Groups with the
/// password again, because each one can read every space: an implicit View,
/// so page restrictions still bind them and drafts stay hidden.</item>
/// </list>
///
/// Fixed ids, so a grant to "Users" means the same thing on every instance
/// and survives anything that renames rows.
/// </summary>
public static class BuiltInGroups
{
    public static readonly Guid OwnerId = new("00000000-0000-0000-0001-000000000001");
    public static readonly Guid AdminsId = new("00000000-0000-0000-0001-000000000002");
    public static readonly Guid UsersId = new("00000000-0000-0000-0001-000000000003");
    public static readonly Guid GlobalViewersId = new("00000000-0000-0000-0001-000000000004");
    public static readonly Guid GlobalReviewersId = new("00000000-0000-0000-0001-000000000005");

    /// <summary>In the order the Groups page lists them.</summary>
    public static readonly Guid[] InOrder = [OwnerId, AdminsId, UsersId, GlobalViewersId, GlobalReviewersId];

    private static readonly (Guid Id, string Name, string Description)[] All =
    [
        (OwnerId, "Owner", "The owner of this instance. Built in: its member is always the owner."),
        (AdminsId, "Admins", "Every administrator, and the owner. Built in: follows who is an administrator."),
        (UsersId, "Users", "Everyone with an account. Built in: follows who has an account."),
        (GlobalViewersId, "Global Viewers",
            "Can read every space, archived ones included. Page restrictions still apply and drafts stay hidden. Built in: its members are chosen here."),
        (GlobalReviewersId, "Global Reviewers",
            "Can read every space, as Global Viewers can. Meant for the people who review changes. Built in: its members are chosen here."),
    ];

    /// <summary>Any of the five: none can be renamed or deleted.</summary>
    public static bool IsBuiltIn(Guid groupId) => IsComputed(groupId) || IsGlobal(groupId);

    /// <summary>Owner, Admins and Users: members follow each account's tier and are never stored.</summary>
    public static bool IsComputed(Guid groupId) => groupId == OwnerId || groupId == AdminsId || groupId == UsersId;

    /// <summary>Global Viewers and Global Reviewers (21.1): stored members, an implicit View on every space.</summary>
    public static bool IsGlobal(Guid groupId) => groupId == GlobalViewersId || groupId == GlobalReviewersId;

    /// <summary>The computed groups an account of this tier is in.</summary>
    public static IEnumerable<Guid> For(UserRole tier)
    {
        yield return UsersId;
        if (tier >= UserRole.Admin) yield return AdminsId;
        if (tier == UserRole.Owner) yield return OwnerId;
    }

    /// <summary>Who is in a computed group (<see cref="IsComputed"/>), as a query over accounts.</summary>
    public static IQueryable<User> Members(AppDbContext db, Guid groupId) => db.Users.Where(u =>
        u.Status == UserStatus.Active &&
        (groupId == UsersId
            || (groupId == AdminsId && u.Role >= UserRole.Admin)
            || (groupId == OwnerId && u.Role == UserRole.Owner)));

    /// <summary>
    /// Creates each on the first start that knows about it. A group someone
    /// already made with one of these names is renamed, with an audit entry,
    /// rather than silently shadowed.
    /// </summary>
    public static async Task EnsureAsync(AppDbContext db, Audit.IAuditLogger audit, ILogger log)
    {
        var changed = false;
        foreach (var (id, name, description) in All)
        {
            if (await db.Groups.AnyAsync(g => g.Id == id)) continue;
            var normalized = name.ToLowerInvariant();
            // A space's groups are never named by people (21.1), so cannot clash.
            var clash = await db.Groups.FirstOrDefaultAsync(g => g.NormalizedName == normalized && g.SpaceId == null);
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
