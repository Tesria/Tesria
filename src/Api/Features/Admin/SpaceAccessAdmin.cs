using Tesria.Api.Domain;
using Tesria.Api.Features.Groups;
using Tesria.Api.Features.Permissions;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Who has access to what, from Administration (dev-plan 21.5). The owner
/// found the 21.1 model sound but impossible to read: Spaces said nothing
/// about access, and nothing answered "what can this person see?". Three
/// views and one action:
/// <list type="bullet">
/// <item>each space's access in counts, on the Spaces list
/// (<see cref="SummariesAsync"/>);</item>
/// <item>a space's access in full: who, at what level, and why
/// (<c>GET /api/admin/spaces/{key}/access</c>);</item>
/// <item>a person's access to every space, with the reasons Check Access
/// gives (<c>GET /api/admin/users/{id}/access</c>);</item>
/// <item>the review of spaces everyone signed in may administer that
/// nobody chose to leave that way (<c>POST .../access-review</c>).</item>
/// </list>
/// <para>
/// Who is in a space is metadata, like its page count, and is shown here for
/// spaces the caller cannot open: deciding whether to Get Access is easier
/// knowing who already has it, and seeing it reads nothing in the space.
/// Page titles are never shown, only how many pages are restricted. The
/// detail views need both See the user list and Manage spaces, the rights
/// <see cref="GroupOverview.CanSeeSpaceAccessAsync"/> pairs for the same
/// reason.
/// </para>
/// </summary>
public static class SpaceAccessAdmin
{
    /// <param name="EveryoneAdminConfirmed">For an EveryoneAccess of Admin: someone chose it, so the review does not list it.</param>
    /// <param name="Admins">Active members of the space's Admins group; likewise the next three.</param>
    /// <param name="OtherGrants">Grants other than the four groups' own: to custom or built-in groups, or to people by name.</param>
    /// <param name="HasExplicitAdmin">An active account holds a real Admin grant, so someone can manage who administers it and lift its page restrictions.</param>
    /// <param name="RestrictedPages">Pages carrying a restriction of their own (their children inherit it).</param>
    /// <param name="TesriaAdministrators">Tesria's administrators administer it (21.6): an Admin grant to the built-in Admins group, not counted in <paramref name="OtherGrants"/>.</param>
    public record AccessSummary(
        SpaceOperation? EveryoneAccess, bool EveryoneAdminConfirmed,
        int Admins, int Editors, int Viewers, int Reviewers, int OtherGrants,
        bool HasExplicitAdmin, int RestrictedPages, bool TesriaAdministrators = false);

    public record PersonRef(Guid Id, string DisplayName, string? Email, bool Active);

    public record GroupAccess(Guid GroupId, string Name, SpaceGroupRole Role, SpaceOperation Level, List<PersonRef> Members);

    /// <param name="Kind">"person" or "group".</param>
    /// <param name="GroupKind">For a group: built-in, global or custom (<see cref="GroupOverview.Kinds"/>).</param>
    /// <param name="Members">For a group: its active members.</param>
    public record OtherAccess(string Kind, Guid PrincipalId, string Name, SpaceOperation Level, string? GroupKind, int? Members, bool Active);

    public record GlobalAccess(Guid GroupId, string Name, int Members);

    /// <param name="SignedInAccounts">How many active accounts "everyone signed in" is today.</param>
    /// <param name="YouAreExplicitAdmin">The caller is in its Admins group or holds Admin there.</param>
    /// <param name="YouCanAdminister">The caller may open its Permissions tab (explicitly or as everyone).</param>
    public record SpaceAccessDetail(
        Guid Id, string Key, string Name, bool Archived,
        SpaceOperation? EveryoneAccess, bool EveryoneAdminConfirmed, int SignedInAccounts,
        bool PubliclyReadable, PersonRef? CreatedBy,
        List<GroupAccess> Groups, List<OtherAccess> Other, List<GlobalAccess> Global,
        int RestrictedPages, bool HasExplicitAdmin, bool YouAreExplicitAdmin, bool YouCanAdminister);

    /// <param name="Level">From the real check made as them; null for nothing.</param>
    public record PersonSpace(
        Guid SpaceId, string Key, string Name, bool Archived, bool IsPublic,
        SpaceOperation? Level, bool ExplicitAdmin, int RestrictedPages, List<AccessExplanation.Reason> Reasons);

    /// <param name="Role">Their tier, which decides what they may do in Administration, not in spaces.</param>
    /// <param name="GlobalGroups">Global Viewers or Global Reviewers, if they are in either.</param>
    public record PersonAccess(
        AccessExplanation.Person Person, UserRole Role, List<string> GlobalGroups, List<PersonSpace> Spaces);

    /// <param name="Choice">"private" (only its groups), "edit" (everyone signed in may edit), or "keep".</param>
    /// <param name="Admins">
    /// Who to put in its Admins group, so someone still manages it: its
    /// creator, the caller, or both. Nobody else, from here: choosing a
    /// space's administrators is its own administrators' business.
    /// </param>
    /// <param name="TesriaAdministrators">
    /// Also let Tesria's administrators (the built-in Admins group) administer
    /// it (21.6), as new spaces do by default: an Admin grant to that group.
    /// </param>
    public record ReviewRequest(string Choice, List<Guid>? Admins, bool TesriaAdministrators = false);

    public static IEndpointRouteBuilder MapSpaceAccessAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin").WithTags("Admin").RequireAuthorization();
        admin.MapGet("/spaces/{key}/access", SpaceDetail)
            .RequirePermission(InstancePermissions.SpacesManage).RequirePermission(InstancePermissions.UsersView)
            .Produces<SpaceAccessDetail>();
        admin.MapPost("/spaces/{key}/access-review", Review)
            .RequirePermission(InstancePermissions.SpacesManage)
            .Produces(StatusCodes.Status204NoContent);
        admin.MapGet("/users/{userId:guid}/access", ForPerson)
            .RequirePermission(InstancePermissions.UsersView).RequirePermission(InstancePermissions.SpacesManage)
            .Produces<PersonAccess>();
        return routes;
    }

    // -- the list's counts ------------------------------------------------------

    /// <summary>Each space's access in counts, for the Spaces list; a few queries for every space at once.</summary>
    public static async Task<Dictionary<Guid, AccessSummary>> SummariesAsync(AppDbContext db, IReadOnlyCollection<Guid> spaceIds)
    {
        var spaces = await db.Spaces.AsNoTracking().Where(s => spaceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.EveryoneAccess, s.EveryoneAdminConfirmedAt })
            .ToListAsync();
        var own = await db.Groups.AsNoTracking().Where(g => g.SpaceId != null && spaceIds.Contains(g.SpaceId.Value))
            .Select(g => new { g.Id, SpaceId = g.SpaceId!.Value, Role = g.SpaceRole!.Value })
            .ToListAsync();
        var ownIds = own.Select(g => g.Id).ToHashSet();
        var grants = await db.SpacePermissions.AsNoTracking().Where(p => spaceIds.Contains(p.SpaceId))
            .Select(p => new { p.SpaceId, p.PrincipalType, p.PrincipalId, p.Operation })
            .ToListAsync();
        var active = await ActiveMemberCountsAsync(db);
        var activeUsers = (await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync()).ToHashSet();
        var restricted = (await db.PageRestrictions.AsNoTracking()
                .Where(r => r.Page!.DeletedAt == null && spaceIds.Contains(r.Page.SpaceId))
                .Select(r => new { r.Page!.SpaceId, r.PageId })
                .Distinct()
                .ToListAsync())
            .GroupBy(r => r.SpaceId)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new Dictionary<Guid, AccessSummary>();
        foreach (var s in spaces)
        {
            int Count(SpaceGroupRole role) =>
                own.Where(g => g.SpaceId == s.Id && g.Role == role).Sum(g => active.GetValueOrDefault(g.Id));
            var spaceGrants = grants.Where(g => g.SpaceId == s.Id).ToList();
            bool IsAdministrators(PrincipalType type, Guid id, SpaceOperation op) =>
                type == PrincipalType.Group && id == BuiltInGroups.AdminsId && op == SpaceOperation.Admin;
            var explicitAdmin = spaceGrants.Any(g => g.Operation == SpaceOperation.Admin
                && (g.PrincipalType == PrincipalType.User ? activeUsers.Contains(g.PrincipalId) : active.GetValueOrDefault(g.PrincipalId) > 0));
            result[s.Id] = new AccessSummary(
                s.EveryoneAccess, s.EveryoneAdminConfirmedAt is not null,
                Count(SpaceGroupRole.Admins), Count(SpaceGroupRole.Editors), Count(SpaceGroupRole.Viewers), Count(SpaceGroupRole.Reviewers),
                spaceGrants.Count(g => !(g.PrincipalType == PrincipalType.Group && ownIds.Contains(g.PrincipalId))
                                       && !IsAdministrators(g.PrincipalType, g.PrincipalId, g.Operation)),
                explicitAdmin, restricted.GetValueOrDefault(s.Id),
                spaceGrants.Any(g => IsAdministrators(g.PrincipalType, g.PrincipalId, g.Operation)));
        }
        return result;
    }

    /// <summary>Active members of every group, the computed ones (Owner, Admins, Users) included.</summary>
    private static async Task<Dictionary<Guid, int>> ActiveMemberCountsAsync(AppDbContext db)
    {
        var counts = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.User!.Status == UserStatus.Active)
            .GroupBy(ug => ug.GroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        foreach (var id in new[] { BuiltInGroups.OwnerId, BuiltInGroups.AdminsId, BuiltInGroups.UsersId })
            counts[id] = await BuiltInGroups.Members(db, id).CountAsync();
        return counts;
    }

    // -- one space in full ------------------------------------------------------

    private static async Task<IResult> SpaceDetail(
        string key, AppDbContext db, IPermissionService perms)
    {
        var normalized = key.Trim().ToUpperInvariant();
        var space = await db.Spaces.AsNoTracking().Where(s => s.Key == normalized)
            .Select(s => new { s.Id, s.Key, s.Name, s.Archived, s.EveryoneAccess, s.EveryoneAdminConfirmedAt, s.CreatedById })
            .FirstOrDefaultAsync();
        if (space is null) return Results.NotFound();

        var own = await db.Groups.AsNoTracking().Where(g => g.SpaceId == space.Id)
            .Select(g => new { g.Id, Role = g.SpaceRole!.Value })
            .ToListAsync();
        var ownIds = own.Select(g => g.Id).ToList();
        var memberRows = await db.UserGroups.AsNoTracking()
            .Where(ug => ownIds.Contains(ug.GroupId))
            .Select(ug => new { ug.GroupId, ug.UserId, ug.User!.DisplayName, ug.User.Email, Active = ug.User.Status == UserStatus.Active })
            .ToListAsync();
        var groups = SpaceGroups.Roles
            .Select(role => own.FirstOrDefault(g => g.Role == role) is { } g
                ? new GroupAccess(g.Id, SpaceGroups.DisplayName(space.Name, role), role, SpaceGroups.OperationOf(role),
                    memberRows.Where(m => m.GroupId == g.Id)
                        .Select(m => new PersonRef(m.UserId, m.DisplayName, m.Email, m.Active))
                        .OrderByDescending(m => m.Active).ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList())
                : null)
            .OfType<GroupAccess>()
            .ToList();

        var grants = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.SpaceId == space.Id && !(p.PrincipalType == PrincipalType.Group && ownIds.Contains(p.PrincipalId)))
            .Select(p => new { p.PrincipalType, p.PrincipalId, p.Operation })
            .ToListAsync();
        var groupNames = await SpaceGroups.NamesAsync(db, grants.Where(g => g.PrincipalType == PrincipalType.Group).Select(g => g.PrincipalId));
        var personIds = grants.Where(g => g.PrincipalType == PrincipalType.User).Select(g => g.PrincipalId).ToList();
        var people = await db.Users.AsNoTracking().Where(u => personIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.DisplayName, Active = u.Status == UserStatus.Active });
        var counts = await ActiveMemberCountsAsync(db);
        var other = grants
            .Select(g => g.PrincipalType == PrincipalType.User
                ? new OtherAccess("person", g.PrincipalId, people.GetValueOrDefault(g.PrincipalId)?.DisplayName ?? "Someone removed",
                    g.Operation, null, null, people.GetValueOrDefault(g.PrincipalId)?.Active ?? false)
                : new OtherAccess("group", g.PrincipalId, groupNames.GetValueOrDefault(g.PrincipalId) ?? "A group", g.Operation,
                    BuiltInGroups.IsComputed(g.PrincipalId) ? GroupOverview.Kinds.BuiltIn
                    : BuiltInGroups.IsGlobal(g.PrincipalId) ? GroupOverview.Kinds.Global
                    : GroupOverview.Kinds.Custom,
                    counts.GetValueOrDefault(g.PrincipalId), true))
            .OrderByDescending(o => o.Level).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var global = new[] { (BuiltInGroups.GlobalViewersId, "Global Viewers"), (BuiltInGroups.GlobalReviewersId, "Global Reviewers") }
            .Select(g => new GlobalAccess(g.Item1, g.Item2, counts.GetValueOrDefault(g.Item1)))
            .Where(g => g.Members > 0)
            .ToList();

        var creator = await db.Users.AsNoTracking().Where(u => u.Id == space.CreatedById)
            .Select(u => new PersonRef(u.Id, u.DisplayName, u.Email, u.Status == UserStatus.Active))
            .FirstOrDefaultAsync();
        var summary = (await SummariesAsync(db, [space.Id]))[space.Id];

        return Results.Ok(new SpaceAccessDetail(
            space.Id, space.Key, space.Name, space.Archived,
            space.EveryoneAccess, space.EveryoneAdminConfirmedAt is not null,
            await db.Users.CountAsync(u => u.Status == UserStatus.Active),
            await perms.AsAnonymous().IsPubliclyViewableSpaceAsync(space.Id), creator,
            groups, other, global, summary.RestrictedPages, summary.HasExplicitAdmin,
            await perms.IsExplicitSpaceAdminAsync(space.Id), await perms.CanAdminSpaceAsync(space.Id)));
    }

    // -- the open-space review --------------------------------------------------

    /// <summary>
    /// Settles one space that everyone signed in may administer and nobody
    /// chose to (dev-plan 21.5): make it private to its groups, let everyone
    /// edit it, or keep it, which records the choice so it is not asked
    /// again. Never automatic: the owner decided (2026-10-08) that each is a
    /// decision, because narrowing quietly takes administration away from
    /// everyone who uses it.
    /// <para>
    /// Narrowing from Administration needs someone left to administer the
    /// space explicitly: one it already has, Tesria's administrators (21.6),
    /// or its creator or the caller, put in its Admins group here. Adding the caller is Get Access by another door, audited and
    /// alerted the same way. Lowering access needs no password, as in the
    /// Permissions tab; nobody gains anything they did not already have,
    /// except the people put in Admins, who are named in the audit entry.
    /// </para>
    /// </summary>
    private static async Task<IResult> Review(
        string key, ReviewRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit,
        Infrastructure.Security.ISecurityDetector detector)
    {
        var normalized = key.Trim().ToUpperInvariant();
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == normalized);
        if (space is null) return Results.NotFound();

        var choice = (req.Choice ?? "").Trim().ToLowerInvariant();
        if (choice is not ("private" or "edit" or "keep"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["choice"] = ["Choose private, edit or keep."],
            });
        if (space.EveryoneAccess != SpaceOperation.Admin)
            return Results.Conflict(new
            {
                message = "Everyone signed in no longer administers this space, so there is nothing to review. Reload the page to see what it is now.",
            });

        var me = current.RequireId();
        // Tesria's administrators as its administrators (21.6), with any
        // choice: an Admin grant to the built-in Admins group, which always
        // holds the owner, so the space is never left without one.
        var grantsAdministrators = req.TesriaAdministrators && !await db.SpacePermissions.AnyAsync(p =>
            p.SpaceId == space.Id && p.PrincipalType == PrincipalType.Group
            && p.PrincipalId == BuiltInGroups.AdminsId && p.Operation == SpaceOperation.Admin);
        if (grantsAdministrators)
        {
            db.SpacePermissions.Add(new SpacePermission
            {
                Id = Guid.NewGuid(), SpaceId = space.Id, PrincipalType = PrincipalType.Group,
                PrincipalId = BuiltInGroups.AdminsId, Operation = SpaceOperation.Admin, CreatedAt = DateTimeOffset.UtcNow,
            });
            audit.Record("space.permission_granted", "space", space.Id, new
            {
                PrincipalType = PrincipalType.Group, PrincipalId = BuiltInGroups.AdminsId, Operation = SpaceOperation.Admin, Via = "space.open_access_reviewed",
            });
        }

        if (choice == "keep")
        {
            space.EveryoneAdminConfirmedAt = DateTimeOffset.UtcNow;
            audit.Record("space.open_access_kept", "space", space.Id, new { space.Key, space.Name, TesriaAdministrators = grantsAdministrators });
            await db.SaveChangesAsync();
            return Results.NoContent();
        }

        var wanted = (req.Admins ?? []).Distinct().ToList();
        if (wanted.Any(id => id != me && id != space.CreatedById))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["admins"] = ["From here, only the person who created the space, or you, can be made its administrator. Its administrators choose anyone else in its Permissions tab."],
            });
        var activeWanted = await db.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id) && u.Status == UserStatus.Active)
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync();
        if (activeWanted.Count != wanted.Count)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["admins"] = ["That account is suspended or gone, so it could not administer the space. Choose yourself instead."],
            });
        if (activeWanted.Count == 0 && !req.TesriaAdministrators
            && !await SpaceGroups.HasAdminAfterAsync(db, space.Id, new SpaceGroups.AdminLoss(LowersEveryone: true)))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["admins"] = ["Nobody would administer this space afterwards. Choose who should: Tesria's administrators, the person who created it, or you."],
            });

        var adminsGroup = await db.Groups
            .Where(g => g.SpaceId == space.Id && g.SpaceRole == SpaceGroupRole.Admins)
            .Select(g => g.Id).FirstOrDefaultAsync();
        if (adminsGroup == Guid.Empty)
            adminsGroup = SpaceGroups.Add(db, space.Id, DateTimeOffset.UtcNow)[SpaceGroupRole.Admins].Id;
        var already = await db.UserGroups.Where(ug => ug.GroupId == adminsGroup).Select(ug => ug.UserId).ToListAsync();
        var added = new List<string>();
        var now = DateTimeOffset.UtcNow;
        foreach (var person in activeWanted.Where(p => !already.Contains(p.Id)))
        {
            db.UserGroups.Add(new UserGroup { GroupId = adminsGroup, UserId = person.Id, AddedAt = now });
            added.Add(person.DisplayName);
            // The caller making themselves its administrator is Get Access,
            // and every administrator hears of it as they do of that.
            if (person.Id == me)
            {
                audit.Record("space.access_recovered", "space", space.Id, new { space.Key, space.Name, GroupId = adminsGroup });
                await detector.SpaceAccessRecoveredAsync(me, space.Id, space.Key);
            }
        }

        var to = choice == "edit" ? SpaceOperation.Edit : (SpaceOperation?)null;
        space.EveryoneAccess = to;
        space.EveryoneAdminConfirmedAt = null;
        audit.Record("space.open_access_reviewed", "space", space.Id, new
        {
            space.Key, space.Name, From = SpaceOperation.Admin.ToString(), To = to?.ToString() ?? "None", AddedAdmins = added,
            TesriaAdministrators = grantsAdministrators,
        });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // -- one person, every space ------------------------------------------------

    /// <summary>
    /// What one person can do in every space, and why (21.5): Check Access
    /// turned the other way. Each level is the permission service evaluated
    /// as them, as Check Access does, and the reasons come from the same
    /// helper, so the two pages cannot disagree.
    /// </summary>
    private static async Task<IResult> ForPerson(Guid userId, AppDbContext db, IPermissionService perms)
    {
        var account = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role, u.Status }).FirstOrDefaultAsync();
        if (account is null) return Results.NotFound(new { message = "There is no such person." });
        var active = account.Status == UserStatus.Active;

        var spaces = await db.Spaces.AsNoTracking()
            .Select(s => new { s.Id, s.Key, s.Name, s.Archived, s.IsPublic })
            .ToListAsync();
        var ids = spaces.Select(s => s.Id).ToList();
        var (reasons, _) = await AccessExplanation.ReasonsAsync(db, account.Id, account.DisplayName, account.Role, active, ids);
        var summaries = await SummariesAsync(db, ids);
        var stored = await db.UserGroups.AsNoTracking().Where(ug => ug.UserId == userId).Select(ug => ug.GroupId).ToListAsync();

        var asThem = perms.AsUser(userId);
        var rows = new List<PersonSpace>();
        foreach (var s in spaces)
        {
            SpaceOperation? level =
                await asThem.CanAdminSpaceAsync(s.Id) ? SpaceOperation.Admin
                : await asThem.CanEditSpaceAsync(s.Id) ? SpaceOperation.Edit
                : await asThem.CanViewSpaceAsync(s.Id) ? SpaceOperation.View
                : null;
            rows.Add(new PersonSpace(s.Id, s.Key, s.Name, s.Archived, s.IsPublic, level,
                await asThem.IsExplicitSpaceAdminAsync(s.Id), summaries[s.Id].RestrictedPages, reasons[s.Id]));
        }

        return Results.Ok(new PersonAccess(
            new AccessExplanation.Person(account.Id, account.DisplayName, account.Email, active),
            account.Role,
            stored.Where(BuiltInGroups.IsGlobal)
                .OrderBy(g => Array.IndexOf(BuiltInGroups.InOrder, g))
                .Select(g => g == BuiltInGroups.GlobalViewersId ? "Global Viewers" : "Global Reviewers")
                .ToList(),
            rows
                .OrderBy(r => r.Level is null)
                .ThenByDescending(r => r.Level)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()));
    }
}
