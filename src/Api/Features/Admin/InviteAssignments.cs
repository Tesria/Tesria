using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// What an invite gives the account it makes (dev-plan 21.3): a tier (user
/// or administrator) and groups (Global Viewers, Global Reviewers, and one
/// of the four groups of any space the inviter administers explicitly).
/// <para>
/// Checked twice. When the invite is made, by the rules that apply to doing
/// the same thing directly: making an administrator needs Promote to
/// administrator (or the owner's tier right) and the password again; a
/// global group needs Manage Groups and the password again; a space's group
/// needs the inviter to be an explicit administrator of that space, for all
/// four, which is stricter than 21.1 asks of the Permissions tab (where an
/// implicit administrator may fill Viewers, Editors and Reviewers): an
/// invite reaches someone who has no account yet, so it is kept to people
/// who chose to administer the space. And again when the account is made,
/// by the inviter's rights at that moment: whatever they could no longer do
/// is skipped and recorded, never applied.
/// </para>
/// </summary>
public static class InviteAssignments
{
    /// <summary>A refusal of what an invite asks for, or the groups to store on it.</summary>
    public sealed record Checked(IResult? Refusal, List<Group> Groups, bool NeedsGlobalSudo);

    /// <summary>The words for a group the inviter may not give, whether or not it exists.</summary>
    public const string GroupNotFound = "A group chosen was not found, or is not yours to give. Choose again.";

    /// <summary>Checks the groups an invite asks for, as its inviter, when it is made.</summary>
    public static async Task<Checked> CheckGroupsAsync(
        AppDbContext db, IPermissionService perms, IReadOnlySet<string> held, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return new(null, [], false);
        var groups = await db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToListAsync();
        // Gone, or a space's group in a space the inviter cannot see: the
        // same words, so an invite cannot be used to learn either.
        if (groups.Count != ids.Count) return new(Invalid(GroupNotFound), [], false);

        var needsSudo = false;
        foreach (var group in groups)
        {
            if (BuiltInGroups.IsGlobal(group.Id))
            {
                if (!held.Contains(InstancePermissions.GroupsManage))
                    return new(RightRequired(InstancePermissions.GroupsManage,
                        "Your role does not allow choosing who is in Global Viewers or Global Reviewers."), [], false);
                needsSudo = true;
                continue;
            }
            if (group.SpaceId is { } spaceId)
            {
                if (!await perms.IsExplicitSpaceAdminAsync(spaceId)) return new(Invalid(GroupNotFound), [], false);
                continue;
            }
            return new(Invalid("An invite can give Global Viewers, Global Reviewers and a space's own groups, not other groups."), [], false);
        }
        if (groups.Where(g => g.SpaceId is not null).GroupBy(g => g.SpaceId).Any(s => s.Count() > 1))
            return new(Invalid("Choose one group in each space: Viewers, Editors, Admins or Reviewers."), [], false);
        return new(null, groups, needsSudo);
    }

    /// <summary>Whether this set of rights may make an administrator: the same two rights a promotion takes.</summary>
    public static bool MayMakeAdmins(IReadOnlySet<string> held) =>
        held.Contains(InstancePermissions.UsersPromoteAdmins) || held.Contains(InstancePermissions.RolesAssignTier);

    /// <summary>
    /// Gives a new account what its invite carries, as the inviter's rights
    /// stand now. Adds to the context and records; the caller saves, in the
    /// transaction that makes the account, so all of it lands or none does.
    /// </summary>
    public static async Task ApplyAsync(
        AppDbContext db, Invite invite, User user, IPermissionService perms, IInstancePermissions rights,
        IAuditLogger audit, ISecurityDetector detector)
    {
        var inviterId = invite.CreatedById;
        var wantsAdmin = invite.Role == UserRole.Admin;
        var rows = await db.InviteGroups.AsNoTracking()
            .Where(x => x.InviteId == invite.Id)
            .Select(x => new
            {
                x.GroupId, x.Group!.Name, x.Group.SpaceId, x.Group.SpaceRole,
                SpaceName = x.Group.Space != null ? x.Group.Space.Name : null,
            })
            .ToListAsync();
        if (!wantsAdmin && rows.Count == 0) return;

        // A suspended inviter holds no rights (ForUserAsync), and is checked
        // here too because explicit space administration does not look at it.
        var inviterActive = await db.Users.AnyAsync(u => u.Id == inviterId && u.Status == UserStatus.Active);
        var held = await rights.ForUserAsync(inviterId);
        var asInviter = perms.AsUser(inviterId);
        var now = DateTimeOffset.UtcNow;

        void Skipped(string what, string reason) =>
            audit.RecordAs(inviterId, "invite.assignment_skipped", "user", user.Id,
                new { InviteId = invite.Id, user.Email, What = what, Reason = reason });

        if (wantsAdmin)
        {
            if (inviterActive && MayMakeAdmins(held))
            {
                user.Role = UserRole.Admin;
                user.RoleId = await RoleSeed.BuiltInIdAsync(db, UserRole.Admin) ?? user.RoleId;
                audit.RecordAs(inviterId, "user.role_changed", "user", user.Id, new
                {
                    user.Email, Role = nameof(UserRole.Admin), From = Role.NameFor(UserRole.Member),
                    To = Role.NameFor(UserRole.Admin), Via = "invite", InviteId = invite.Id,
                });
                // Every new administrator is an alert, as a promotion is.
                await detector.AdminPromotedAsync(inviterId, user);
            }
            else
            {
                Skipped("Administrator", "The person who sent the invite can no longer make administrators.");
            }
        }

        foreach (var row in rows)
        {
            var name = row is { SpaceRole: { } role, SpaceName: { } space } ? SpaceGroups.DisplayName(space, role) : row.Name;
            bool allowed;
            string reason;
            if (BuiltInGroups.IsGlobal(row.GroupId))
            {
                allowed = inviterActive && held.Contains(InstancePermissions.GroupsManage);
                reason = "The person who sent the invite can no longer choose who is in this group.";
            }
            else if (row.SpaceId is { } spaceId)
            {
                allowed = inviterActive && await asInviter.IsExplicitSpaceAdminAsync(spaceId);
                reason = "The person who sent the invite no longer administers this space.";
            }
            else
            {
                // Not one an invite may carry; only reachable by editing the table.
                allowed = false;
                reason = "An invite cannot give this group.";
            }
            if (!allowed)
            {
                Skipped(name, reason);
                continue;
            }

            db.UserGroups.Add(new UserGroup { GroupId = row.GroupId, UserId = user.Id, AddedAt = now });
            if (row is { SpaceId: { } owner, SpaceRole: { } spaceRole })
                // Against the space, as an addition in its Permissions tab is.
                audit.RecordAs(inviterId, "space.group_member_added", "space", owner,
                    new { row.GroupId, Role = spaceRole.ToString(), UserId = user.Id, Via = "invite", InviteId = invite.Id });
            else
                audit.RecordAs(inviterId, "group.member_added", "group", row.GroupId,
                    new { UserId = user.Id, Group = row.Name, Via = "invite", InviteId = invite.Id });
            if (BuiltInGroups.IsGlobal(row.GroupId))
                await detector.GlobalGroupMemberAddedAsync(inviterId, row.GroupId, row.Name, user.Id, user.DisplayName);
        }
    }

    private static IResult Invalid(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["groupIds"] = [message] });

    internal static IResult RightRequired(string permission, string message) => Results.Json(new
    {
        title = "Forbidden",
        status = 403,
        code = "permission_required",
        permission,
        message,
    }, statusCode: StatusCodes.Status403Forbidden);
}
