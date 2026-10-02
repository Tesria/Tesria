using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Groups;

public static class GroupEndpoints
{
    public record SaveGroupRequest(string Name, string? Description);
    public record AddMemberRequest(Guid UserId);

    /// <summary>Several people at once (dev-plan 21.4): chosen from the list, by email, or both.</summary>
    public record AddMembersRequest(List<Guid>? UserIds, List<string>? Emails);

    /// <summary>One person in a bulk add's report.</summary>
    /// <param name="Active">False for a suspended account: added, but it gets nothing until it is active again.</param>
    public record BulkPerson(Guid UserId, string DisplayName, string? Email, bool Active);

    /// <summary>One entry a bulk add did not act on, as given, and why.</summary>
    public record BulkRefusal(string Input, string Reason);

    public record AddMembersResponse(List<BulkPerson> Added, List<BulkPerson> AlreadyMembers, List<BulkRefusal> Refused);

    /// <summary>How many people one bulk add takes: a team, not the whole directory.</summary>
    public const int MaxBulkMembers = 500;

    /// <param name="BuiltIn">One of the five every instance has: never renamed or deleted.</param>
    /// <param name="Computed">Owner, Admins or Users: members follow each account's role, so none are added or removed.</param>
    /// <param name="SpaceId">For one of a space's four groups (dev-plan 21.1): the space it belongs to.</param>
    /// <param name="SpaceKey">Likewise, its key.</param>
    /// <param name="SpaceRole">Likewise, which of the four it is.</param>
    public record GroupResponse(
        Guid Id, string Name, string? Description, int MemberCount, bool BuiltIn = false,
        bool Computed = false, Guid? SpaceId = null, string? SpaceKey = null, SpaceGroupRole? SpaceRole = null);
    /// <summary>
    /// <c>Email</c> is filled only for callers who may see the user list, and
    /// for the caller's own row (dev-plan 14.1): everyone else gets names and
    /// avatars, which is all mentions, pickers and member lists need. Before,
    /// every signed-in account could collect every address. <c>Active</c> is
    /// false for a suspended account, which keeps its memberships but gets
    /// nothing from them (21.1).
    /// </summary>
    public record MemberResponse(Guid UserId, string? Email, string DisplayName, bool Active = true);
    public record UserResponse(
        Guid Id, string? Email, string DisplayName, string? AvatarHash, int? AvatarVariant);

    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder routes)
    {
        var groups = routes.MapGroup("/groups").WithTags("Groups").RequireAuthorization();
        // Anyone signed in may see groups: the permission picker needs the
        // list. Shaping them is instance administration, except a space's own
        // groups (21.1), which are hidden from anyone who cannot see the space.
        groups.MapGet("/", List).Produces<List<GroupResponse>>();
        groups.MapGet("/{id:guid}/members", Members).Produces<List<MemberResponse>>();
        var manage = groups.MapGroup("")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + InstancePermissions.GroupsManage);
        manage.MapPost("/", Create).Produces<GroupResponse>(StatusCodes.Status201Created);
        manage.MapPut("/{id:guid}", Update).Produces<GroupResponse>();
        manage.MapDelete("/{id:guid}", Delete).Produces(StatusCodes.Status204NoContent);
        // The Groups page's list (21.4): searched, filtered and counted.
        manage.MapGet("/overview", GroupOverview.Get).Produces<GroupOverview.OverviewResponse>();
        // Membership is checked in the handler, because who may change it
        // depends on the group (21.1): Manage Groups for global and custom
        // groups, the space's administrators for a space's own.
        groups.MapPost("/{id:guid}/members", AddMember).Produces(StatusCodes.Status204NoContent);
        groups.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMember).Produces(StatusCodes.Status204NoContent);
        groups.MapPost("/{id:guid}/members/bulk", AddMembers).Produces<AddMembersResponse>();

        // Directory of accounts, used when picking permission principals.
        routes.MapGet("/users", ListUsers).WithTags("Users").RequireAuthorization().Produces<List<UserResponse>>();

        return routes;
    }

    private static async Task<IResult> ListUsers(AppDbContext db, CurrentUser current, IInstancePermissions rights)
    {
        var showEmail = await rights.HasAsync(InstancePermissions.UsersView);
        var me = current.RequireId();
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.AvatarKey, u.AvatarHash, u.AvatarVariant })
            .ToListAsync();
        return Results.Ok(users.Select(u => new UserResponse(
            u.Id, showEmail || u.Id == me ? u.Email : null, u.DisplayName,
            // Only an uploaded picture has a hash worth fetching.
            u.AvatarKey is null ? null : u.AvatarHash, u.AvatarVariant)));
    }

    private static async Task<IResult> List(AppDbContext db, IPermissionService perms)
    {
        var rows = await db.Groups.AsNoTracking()
            .Select(g => new
            {
                g.Id, g.Name, g.Description, Count = g.Members.Count, g.SpaceId, g.SpaceRole,
                SpaceKey = g.Space != null ? g.Space.Key : null,
                SpaceName = g.Space != null ? g.Space.Name : null,
            })
            .ToListAsync();
        // A space's groups only for spaces the caller can see (21.1), the
        // same rule as the space itself: their names say the space's name.
        var viewable = rows.Any(r => r.SpaceId is not null) ? await perms.ViewableSpaceIdsAsync() : [];

        var result = new List<GroupResponse>();
        // The built-in groups first; the computed ones counted from the accounts they follow.
        foreach (var id in BuiltInGroups.InOrder)
            if (rows.FirstOrDefault(r => r.Id == id) is { } g)
                result.Add(new GroupResponse(g.Id, g.Name, g.Description,
                    BuiltInGroups.IsComputed(id) ? await BuiltInGroups.Members(db, id).CountAsync() : g.Count,
                    BuiltIn: true, Computed: BuiltInGroups.IsComputed(id)));
        result.AddRange(rows
            .Where(r => !BuiltInGroups.IsBuiltIn(r.Id) && (r.SpaceId is null || viewable.Contains(r.SpaceId.Value)))
            .Select(r => r is { SpaceRole: { } role, SpaceName: { } spaceName }
                ? new GroupResponse(r.Id, SpaceGroups.DisplayName(spaceName, role), r.Description, r.Count,
                    SpaceId: r.SpaceId, SpaceKey: r.SpaceKey, SpaceRole: role)
                : new GroupResponse(r.Id, r.Name, r.Description, r.Count))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase));
        return Results.Ok(result);
    }

    /// <summary>The refusal for anything that would change a computed built-in group.</summary>
    private static IResult BuiltInRefusal() => Results.Conflict(new
    {
        message = "Owner, Admins and Users are built in: their members follow each account's role, and they cannot be renamed or deleted.",
    });

    private static IResult GlobalRefusal() => Results.Conflict(new
    {
        message = "Global Viewers and Global Reviewers are built in: choose who is in them, but they cannot be renamed or deleted.",
    });

    private static IResult SpaceGroupRefusal() => Results.Conflict(new
    {
        message = "A space's own groups belong to it: they are named after the space, go when it does, and their members are chosen in the space's Permissions tab.",
    });

    private static async Task<IResult> Create(
        SaveGroupRequest req, AppDbContext db, IAuditLogger audit)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Group name is required."));

        var normalized = name.ToLowerInvariant();
        if (await db.Groups.AnyAsync(g => g.NormalizedName == normalized && g.SpaceId == null))
            return Results.Conflict(new { message = $"A group named '{name}' already exists." });

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalized,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Groups.Add(group);
        audit.Record("group.created", "group", group.Id, new { group.Name });
        await db.SaveChangesAsync();

        return Results.Created($"/api/groups/{group.Id}", new GroupResponse(group.Id, group.Name, group.Description, 0));
    }

    private static async Task<IResult> Update(
        Guid id, SaveGroupRequest req, AppDbContext db, IAuditLogger audit)
    {
        if (BuiltInGroups.IsComputed(id)) return BuiltInRefusal();
        if (BuiltInGroups.IsGlobal(id)) return GlobalRefusal();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        // Manage Groups does not reach a space's groups (21.1).
        if (group is null) return Results.NotFound();
        if (group.SpaceId is not null) return SpaceGroupRefusal();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Group name is required."));

        var normalized = name.ToLowerInvariant();
        if (await db.Groups.AnyAsync(g => g.NormalizedName == normalized && g.SpaceId == null && g.Id != id))
            return Results.Conflict(new { message = $"A group named '{name}' already exists." });

        group.Name = name;
        group.NormalizedName = normalized;
        group.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        audit.Record("group.updated", "group", group.Id, new { group.Name });
        await db.SaveChangesAsync();

        var count = await db.UserGroups.CountAsync(ug => ug.GroupId == id);
        return Results.Ok(new GroupResponse(group.Id, group.Name, group.Description, count));
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, IAuditLogger audit, IPermissionService perms)
    {
        if (BuiltInGroups.IsComputed(id)) return BuiltInRefusal();
        if (BuiltInGroups.IsGlobal(id)) return GlobalRefusal();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return Results.NotFound();
        if (group.SpaceId is not null) return SpaceGroupRefusal();

        // A group's grants go with it; they used to stay behind, pointing at
        // nothing (found 2026-09-23). Since 21.1 removing a grant can no
        // longer open a space (what everyone signed in gets is its own
        // setting), but it can still take a space's last administrator, and
        // removing a page's only View restriction would still make the page
        // readable by everyone in its space. Those are refused, with where,
        // rather than done quietly.
        var grants = await db.SpacePermissions
            .Where(p => p.PrincipalType == PrincipalType.Group && p.PrincipalId == id).ToListAsync();
        var restrictions = await db.PageRestrictions
            .Where(r => r.PrincipalType == PrincipalType.Group && r.PrincipalId == id).ToListAsync();

        var blocked = new List<string>();
        foreach (var spaceId in grants.Where(g => g.Operation == SpaceOperation.Admin).Select(g => g.SpaceId).Distinct())
            if (!await SpaceGroups.HasAdminAfterAsync(db, spaceId, new SpaceGroups.AdminLoss(DeletedGroupId: id)))
                blocked.Add(await SpaceLabelAsync(db, perms, spaceId) + " (its last administrator)");
        foreach (var r in restrictions)
        {
            var anotherOfSameKind = await db.PageRestrictions.AnyAsync(o => o.PageId == r.PageId && o.Operation == r.Operation
                && !(o.PrincipalType == PrincipalType.Group && o.PrincipalId == id));
            if (!anotherOfSameKind)
                blocked.Add("the page " + await db.Pages.IgnoreQueryFilters().Where(p => p.Id == r.PageId).Select(p => p.Title).FirstAsync());
        }
        if (blocked.Count > 0)
            return Results.Conflict(new
            {
                message = $"{group.Name} is the only access to {string.Join(", ", blocked.Distinct().Take(5))}"
                    + (blocked.Distinct().Count() > 5 ? " and more" : "")
                    + ". Give that access to someone else first, or deleting the group would leave it without.",
            });

        db.SpacePermissions.RemoveRange(grants);
        db.PageRestrictions.RemoveRange(restrictions);
        db.Groups.Remove(group);
        audit.Record("group.deleted", "group", group.Id, new { group.Name, Grants = grants.Count, Restrictions = restrictions.Count });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>A space by name if the caller can see it; otherwise without saying which.</summary>
    private static async Task<string> SpaceLabelAsync(AppDbContext db, IPermissionService perms, Guid spaceId) =>
        await perms.CanViewSpaceAsync(spaceId)
            ? "the space " + await db.Spaces.Where(s => s.Id == spaceId).Select(s => s.Name).FirstAsync()
            : "a space you cannot see";

    private static async Task<IResult> Members(
        Guid id, AppDbContext db, CurrentUser current, IInstancePermissions rights, IPermissionService perms)
    {
        var group = await db.Groups.AsNoTracking().Where(g => g.Id == id).Select(g => new { g.SpaceId }).FirstOrDefaultAsync();
        if (group is null) return Results.NotFound();
        // A space's group is as hidden as its space (21.1).
        if (group.SpaceId is { } spaceId && !await perms.CanViewSpaceAsync(spaceId)) return Results.NotFound();
        var showEmail = await rights.HasAsync(InstancePermissions.UsersView);
        var me = current.RequireId();
        MemberResponse Shown(MemberResponse m) => showEmail || m.UserId == me ? m : m with { Email = null };
        if (BuiltInGroups.IsComputed(id))
            return Results.Ok((await BuiltInGroups.Members(db, id).AsNoTracking()
                .OrderBy(u => u.DisplayName)
                .Select(u => new MemberResponse(u.Id, u.Email, u.DisplayName, true))
                .ToListAsync()).Select(Shown));
        var members = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.GroupId == id)
            .Select(ug => new MemberResponse(ug.UserId, ug.User!.Email, ug.User.DisplayName, ug.User.Status == UserStatus.Active))
            .ToListAsync();
        return Results.Ok(members.OrderBy(m => m.DisplayName).Select(Shown));
    }

    /// <summary>
    /// Who may change a group's members (dev-plan 21.1), or the refusal. A
    /// space's groups are its administrators': its Admins group only its
    /// explicit ones, because in a space everyone signed in may administer,
    /// anyone could otherwise add themselves to Admins and pass every page
    /// restriction. Every other group is Manage Groups', which does not
    /// reach a space's groups; Global Viewers and Global Reviewers also need
    /// the password again, since each member reads every space.
    /// </summary>
    private static async Task<IResult?> MembershipRefusalAsync(
        Group group, IPermissionService perms, IAuthorizationService auth, HttpContext http, IConfiguration config)
    {
        if (BuiltInGroups.IsComputed(group.Id)) return BuiltInRefusal();
        if (group.SpaceId is { } spaceId)
        {
            if (!await perms.CanViewSpaceAsync(spaceId)) return Results.NotFound();
            if (group.SpaceRole == SpaceGroupRole.Admins)
                return await perms.IsExplicitSpaceAdminAsync(spaceId) ? null : Tesria.Api.Features.Permissions.PermissionEndpoints.ExplicitAdminRefusal();
            return await perms.CanAdminSpaceAsync(spaceId) ? null : Results.Forbid();
        }
        if (!(await auth.AuthorizeAsync(http.User, PermissionPolicyProvider.Prefix + InstancePermissions.GroupsManage)).Succeeded)
            return Results.Forbid();
        if (BuiltInGroups.IsGlobal(group.Id) && Features.Auth.AuthEndpoints.RequireSudo(http, config, Features.Auth.SudoReasons.GlobalReaders) is { } denied)
            return denied;
        return null;
    }

    private static async Task<IResult> AddMember(
        Guid id, AddMemberRequest req, AppDbContext db, IAuditLogger audit, IPermissionService perms,
        IAuthorizationService auth, HttpContext http, IConfiguration config, CurrentUser current,
        Infrastructure.Security.ISecurityDetector detector)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return Results.NotFound();
        if (await MembershipRefusalAsync(group, perms, auth, http, config) is { } refused) return refused;
        var user = await db.Users.AsNoTracking().Where(u => u.Id == req.UserId)
            .Select(u => new { u.Id, u.DisplayName }).FirstOrDefaultAsync();
        if (user is null)
            return Results.ValidationProblem(Error("userId", "User not found."));

        if (!await db.UserGroups.AnyAsync(ug => ug.GroupId == id && ug.UserId == req.UserId))
        {
            db.UserGroups.Add(new UserGroup
            {
                GroupId = id,
                UserId = req.UserId,
                AddedAt = DateTimeOffset.UtcNow,
            });
            if (group is { SpaceId: { } spaceId, SpaceRole: { } role })
                // Recorded against the space, so the audit log hides it from
                // anyone who cannot see the space, as for its other entries.
                audit.Record("space.group_member_added", "space", spaceId, new { GroupId = id, Role = role.ToString(), req.UserId });
            else
                audit.Record("group.member_added", "group", id, new { req.UserId, Group = group.Name });
            if (BuiltInGroups.IsGlobal(id))
                await detector.GlobalGroupMemberAddedAsync(current.RequireId(), id, group.Name, user.Id, user.DisplayName);
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    /// <summary>
    /// Adds several people to one group (dev-plan 21.4), under exactly the
    /// rights a single add needs: the group's rule is checked once, as for
    /// one person, and refuses the whole request when it refuses. Each
    /// person is then added, found already there, or refused with a reason,
    /// and every addition is audited (and, for the global groups, alerted)
    /// as a single add would be. Emails are looked up only for callers who
    /// may see the user list, so a paste cannot test whether an address has
    /// an account.
    /// </summary>
    private static async Task<IResult> AddMembers(
        Guid id, AddMembersRequest req, AppDbContext db, IAuditLogger audit, IPermissionService perms,
        IAuthorizationService auth, HttpContext http, IConfiguration config, CurrentUser current,
        IInstancePermissions rights, Infrastructure.Security.ISecurityDetector detector)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return Results.NotFound();
        if (await MembershipRefusalAsync(group, perms, auth, http, config) is { } refused) return refused;

        var ids = (req.UserIds ?? []).Distinct().ToList();
        // Kept as given for the report, compared lower-cased as stored.
        var emails = (req.Emails ?? []).Select(e => (e ?? "").Trim()).Where(e => e.Length > 0)
            .DistinctBy(e => e.ToLowerInvariant()).ToList();
        if (ids.Count + emails.Count == 0)
            return Results.ValidationProblem(Error("userIds", "Choose at least one person."));
        if (ids.Count + emails.Count > MaxBulkMembers)
            return Results.ValidationProblem(Error("userIds", $"Add at most {MaxBulkMembers} people at once."));

        var showEmail = await rights.HasAsync(InstancePermissions.UsersView);
        var me = current.RequireId();
        var refusals = new List<BulkRefusal>();
        var lowered = emails.Select(e => e.ToLowerInvariant()).ToList();
        var byEmail = showEmail
            ? await db.Users.AsNoTracking().Where(u => lowered.Contains(u.Email))
                .Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Email, u => u.Id)
            : [];
        foreach (var email in emails)
        {
            if (!showEmail)
                refusals.Add(new BulkRefusal(email, "You cannot look people up by email here: choose them from the list instead."));
            else if (!email.Contains('@'))
                refusals.Add(new BulkRefusal(email, "This is not an email address."));
            else if (byEmail.TryGetValue(email.ToLowerInvariant(), out var found))
                ids.Add(found);
            else
                refusals.Add(new BulkRefusal(email, "No account has this email address."));
        }
        ids = ids.Distinct().ToList();

        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Status })
            .ToDictionaryAsync(u => u.Id);
        var already = (await db.UserGroups.AsNoTracking()
            .Where(ug => ug.GroupId == id && ids.Contains(ug.UserId)).Select(ug => ug.UserId).ToListAsync()).ToHashSet();

        var added = new List<BulkPerson>();
        var alreadyMembers = new List<BulkPerson>();
        var now = DateTimeOffset.UtcNow;
        foreach (var userId in ids)
        {
            if (!people.TryGetValue(userId, out var user))
            {
                refusals.Add(new BulkRefusal(userId.ToString(), "No such account."));
                continue;
            }
            var person = new BulkPerson(user.Id, user.DisplayName, showEmail || user.Id == me ? user.Email : null,
                user.Status == UserStatus.Active);
            if (already.Contains(userId))
            {
                alreadyMembers.Add(person);
                continue;
            }
            db.UserGroups.Add(new UserGroup { GroupId = id, UserId = userId, AddedAt = now });
            if (group is { SpaceId: { } spaceId, SpaceRole: { } role })
                audit.Record("space.group_member_added", "space", spaceId, new { GroupId = id, Role = role.ToString(), UserId = userId });
            else
                audit.Record("group.member_added", "group", id, new { UserId = userId, Group = group.Name });
            if (BuiltInGroups.IsGlobal(id))
                await detector.GlobalGroupMemberAddedAsync(me, id, group.Name, user.Id, user.DisplayName);
            added.Add(person);
        }
        if (added.Count > 0) await db.SaveChangesAsync();

        static List<BulkPerson> ByName(IEnumerable<BulkPerson> list) =>
            list.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        return Results.Ok(new AddMembersResponse(ByName(added), ByName(alreadyMembers), refusals));
    }

    private static async Task<IResult> RemoveMember(
        Guid id, Guid userId, AppDbContext db, IAuditLogger audit, IPermissionService perms,
        IAuthorizationService auth, HttpContext http, IConfiguration config)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return Results.NotFound();
        if (await MembershipRefusalAsync(group, perms, auth, http, config) is { } refused) return refused;
        var link = await db.UserGroups.FirstOrDefaultAsync(ug => ug.GroupId == id && ug.UserId == userId);
        if (link is null) return Results.NotFound();

        // The last-admin rule (21.1): a group holding Admin somewhere, the
        // space's own Admins group included, keeps its last active member.
        var adminSpaces = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.PrincipalType == PrincipalType.Group && p.PrincipalId == id && p.Operation == SpaceOperation.Admin)
            .Select(p => p.SpaceId).Distinct().ToListAsync();
        foreach (var spaceId in adminSpaces)
            if (!await SpaceGroups.HasAdminAfterAsync(db, spaceId,
                    new SpaceGroups.AdminLoss(RemovedFromGroupId: id, RemovedUserId: userId)))
                return Results.Conflict(new
                {
                    message = $"That would leave {await SpaceLabelAsync(db, perms, spaceId)} without an administrator. Add another one first.",
                });

        db.UserGroups.Remove(link);
        if (group is { SpaceId: { } owner, SpaceRole: { } role })
            audit.Record("space.group_member_removed", "space", owner, new { GroupId = id, Role = role.ToString(), UserId = userId });
        else
            audit.Record("group.member_removed", "group", id, new { UserId = userId, Group = group.Name });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
