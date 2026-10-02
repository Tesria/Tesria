using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>
/// A space's four groups (dev-plan 21.1): Viewers, Editors, Admins and
/// Reviewers, made with every space, owned by it and gone with it.
/// <para>
/// Each is an ordinary group holding one ordinary grant on its space, so
/// every rule that reads grants (explicit admin, the last-admin rule, the
/// live-editing revocations) works on them unchanged. What is special is
/// only what may be done to them: the grant cannot be changed or revoked,
/// the group cannot be renamed or deleted, it can be granted or named in a
/// restriction only within its own space, and its members are managed by
/// the space's administrators, never by Manage Groups.
/// </para>
/// </summary>
public static class SpaceGroups
{
    /// <summary>In the order the Permissions tab shows them.</summary>
    public static readonly SpaceGroupRole[] Roles =
        [SpaceGroupRole.Admins, SpaceGroupRole.Editors, SpaceGroupRole.Viewers, SpaceGroupRole.Reviewers];

    /// <summary>The fixed grant each group holds. Reviewers view until review mode (22.4) asks more of them.</summary>
    public static SpaceOperation OperationOf(SpaceGroupRole role) => role switch
    {
        SpaceGroupRole.Admins => SpaceOperation.Admin,
        SpaceGroupRole.Editors => SpaceOperation.Edit,
        _ => SpaceOperation.View,
    };

    /// <summary>Where a person's grant of this level goes when it moves into a group.</summary>
    public static SpaceGroupRole RoleFor(SpaceOperation operation) => operation switch
    {
        SpaceOperation.Admin => SpaceGroupRole.Admins,
        SpaceOperation.Edit => SpaceGroupRole.Editors,
        _ => SpaceGroupRole.Viewers,
    };

    /// <summary>"Handbook Viewers": derived when read, never stored, so renames and same-named spaces need nothing.</summary>
    public static string DisplayName(string spaceName, SpaceGroupRole role) => $"{spaceName} {role}";

    /// <summary>
    /// What a space group stores as its normalized name: unique per space and
    /// role, and outside the unique index people's group names live in.
    /// </summary>
    public static string Key(Guid spaceId, SpaceGroupRole role) =>
        $"space:{spaceId:N}:{role.ToString().ToLowerInvariant()}";

    /// <summary>Adds a space's four groups and their fixed grants to the context; the caller saves.</summary>
    public static Dictionary<SpaceGroupRole, Group> Add(AppDbContext db, Guid spaceId, DateTimeOffset now)
    {
        var made = new Dictionary<SpaceGroupRole, Group>();
        foreach (var role in Roles)
        {
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = role.ToString(),
                NormalizedName = Key(spaceId, role),
                SpaceId = spaceId,
                SpaceRole = role,
                CreatedAt = now,
            };
            db.Groups.Add(group);
            db.SpacePermissions.Add(new SpacePermission
            {
                Id = Guid.NewGuid(),
                SpaceId = spaceId,
                PrincipalType = PrincipalType.Group,
                PrincipalId = group.Id,
                Operation = OperationOf(role),
                CreatedAt = now,
            });
            made[role] = group;
        }
        return made;
    }

    /// <summary>The name to show for each group, a space's groups named after their space.</summary>
    public static async Task<Dictionary<Guid, string>> NamesAsync(AppDbContext db, IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.Groups.AsNoTracking()
            .Where(g => idList.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.SpaceRole, SpaceName = g.Space != null ? g.Space.Name : null })
            .ToListAsync();
        return rows.ToDictionary(g => g.Id,
            g => g is { SpaceRole: { } role, SpaceName: { } space } ? DisplayName(space, role) : g.Name);
    }

    /// <summary>
    /// One way to lose administrators (dev-plan 21.1's last-admin rule), to
    /// check before doing it. Exactly one is set, except
    /// <see cref="LowersEveryone"/>, which says the space's EveryoneAccess is
    /// leaving Admin.
    /// </summary>
    public sealed record AdminLoss(
        Guid? RevokedGrantId = null,
        Guid? DeletedGroupId = null,
        Guid? RemovedFromGroupId = null,
        Guid? RemovedUserId = null,
        bool LowersEveryone = false);

    /// <summary>
    /// Whether the space would still have someone to administer it after the
    /// change: an EveryoneAccess of Admin, or an active account holding Admin
    /// directly, through its Admins group, or through any other group with an
    /// Admin grant. Suspended accounts do not count: they cannot sign in to
    /// do it. Runs on every way to lose admin: revoking an Admin grant,
    /// removing a member of a group that holds one, lowering EveryoneAccess
    /// from Admin, and deleting a group that holds one.
    /// </summary>
    public static async Task<bool> HasAdminAfterAsync(AppDbContext db, Guid spaceId, AdminLoss change)
    {
        var everyone = await db.Spaces.AsNoTracking()
            .Where(s => s.Id == spaceId).Select(s => s.EveryoneAccess).FirstOrDefaultAsync();
        if (!change.LowersEveryone && everyone == SpaceOperation.Admin) return true;

        var grants = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.SpaceId == spaceId && p.Operation == SpaceOperation.Admin)
            .Select(p => new { p.Id, p.PrincipalType, p.PrincipalId })
            .ToListAsync();
        foreach (var grant in grants)
        {
            if (grant.Id == change.RevokedGrantId) continue;
            if (grant.PrincipalType == PrincipalType.User)
            {
                if (await db.Users.AnyAsync(u => u.Id == grant.PrincipalId && u.Status == UserStatus.Active)) return true;
                continue;
            }
            if (grant.PrincipalId == change.DeletedGroupId) continue;
            if (BuiltInGroups.IsComputed(grant.PrincipalId))
            {
                if (await BuiltInGroups.Members(db, grant.PrincipalId).AnyAsync()) return true;
                continue;
            }
            var members = db.UserGroups.Where(ug => ug.GroupId == grant.PrincipalId && ug.User!.Status == UserStatus.Active);
            if (grant.PrincipalId == change.RemovedFromGroupId && change.RemovedUserId is { } removing)
                members = members.Where(ug => ug.UserId != removing);
            if (await members.AnyAsync()) return true;
        }
        return false;
    }
}
