using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Groups;

/// <summary>
/// The Groups page's list (dev-plan 21.4): searched by group name and by
/// member, filtered by kind or by space, with each group's members counted
/// and what it grants where. <c>GET /api/groups</c> keeps its shape for the
/// permission picker and the space tab; this is the richer view for the
/// people who manage groups.
/// <para>
/// Nothing here shows more than the caller could already find: a space's
/// groups only for spaces they can view (21.1), emails only with See the
/// user list, a group's grants only on spaces whose permissions the caller
/// may see (<see cref="CanSeeSpaceAccessAsync"/>), and page restrictions
/// only on pages the caller can read.
/// </para>
/// </summary>
public static class GroupOverview
{
    /// <summary>Which of the four kinds of group (21.1) a row is.</summary>
    public static class Kinds
    {
        /// <summary>Owner, Admins and Users: members follow each account's role.</summary>
        public const string BuiltIn = "builtin";
        /// <summary>Global Viewers and Global Reviewers: chosen members, View on every space.</summary>
        public const string Global = "global";
        public const string Custom = "custom";
        /// <summary>One of a space's own four groups.</summary>
        public const string Space = "space";
    }

    /// <param name="Kind">One of <see cref="Kinds"/>.</param>
    /// <param name="ActiveMembers">Members whose accounts are active: the ones who get anything from it.</param>
    /// <param name="SuspendedMembers">Members whose accounts are suspended, who keep the membership but get nothing.</param>
    /// <param name="CanManageMembers">Whether the caller may add and remove its members (21.1's rules).</param>
    /// <param name="EverySpace">Global Viewers and Global Reviewers: View on every space, without a grant.</param>
    /// <param name="Grants">The spaces it holds a grant on, and what, where the caller may see that space's permissions.</param>
    /// <param name="Restrictions">The page restrictions that name it, on pages the caller can read.</param>
    /// <param name="Matches">With a search, the members it matched by name or email.</param>
    public record OverviewGroup(
        Guid Id, string Name, string? Description, string Kind, bool Computed,
        Guid? SpaceId, string? SpaceKey, string? SpaceName, SpaceGroupRole? SpaceRole,
        int ActiveMembers, int SuspendedMembers, bool CanManageMembers, bool EverySpace,
        List<OverviewGrant> Grants, List<OverviewRestriction> Restrictions, List<OverviewMatch> Matches);

    public record OverviewGrant(Guid SpaceId, string SpaceKey, string SpaceName, SpaceOperation Operation);
    public record OverviewRestriction(Guid PageId, string PageTitle, string SpaceKey, PageOperation Operation);
    public record OverviewMatch(Guid UserId, string DisplayName, string? Email);

    /// <param name="Groups">In the page's order: built-in, global, custom, then space groups by space.</param>
    /// <param name="Truncated">A search matched more people than are looked at; narrow it.</param>
    public record OverviewResponse(List<OverviewGroup> Groups, bool Truncated);

    /// <summary>How many people a member search looks at, so one letter cannot walk every account.</summary>
    public const int MaxMatchedPeople = 200;

    /// <summary>How many restrictions are read for the whole list: cheap to count, not worth paging.</summary>
    private const int MaxRestrictions = 500;

    /// <summary>
    /// Whether the caller may see a space's permissions (dev-plan 21.4): its
    /// administrators (as for its Permissions tab), or holders of both See
    /// the user list and Manage spaces, the rights that show people and
    /// spaces in Administration. Never a space the caller cannot view; that
    /// is the caller's to check first, and answers not found.
    /// </summary>
    public static async Task<bool> CanSeeSpaceAccessAsync(
        Guid spaceId, IPermissionService perms, IInstancePermissions rights) =>
        await perms.CanAdminSpaceAsync(spaceId)
        || (await rights.HasAsync(InstancePermissions.UsersView) && await rights.HasAsync(InstancePermissions.SpacesManage));

    public static async Task<IResult> Get(
        string? q, string? kind, string? space,
        AppDbContext db, CurrentUser current, IPermissionService perms, IInstancePermissions rights)
    {
        var query = (q ?? "").Trim();
        kind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToLowerInvariant();
        if (kind is not null and not (Kinds.BuiltIn or Kinds.Global or Kinds.Custom or Kinds.Space))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = ["Choose builtin, global, custom or space."],
            });

        Guid? spaceFilter = null;
        if (!string.IsNullOrWhiteSpace(space))
        {
            var key = space.Trim().ToUpperInvariant();
            var id = await db.Spaces.AsNoTracking().Where(s => s.Key == key).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
            // A space the caller cannot view is as missing as one that is not there.
            if (id is null || !await perms.CanViewSpaceAsync(id.Value)) return Results.NotFound();
            spaceFilter = id;
        }

        var showEmail = await rights.HasAsync(InstancePermissions.UsersView);

        var rows = await db.Groups.AsNoTracking()
            .Select(g => new
            {
                g.Id, g.Name, g.Description, g.SpaceId, g.SpaceRole,
                SpaceKey = g.Space != null ? g.Space.Key : null,
                SpaceName = g.Space != null ? g.Space.Name : null,
            })
            .ToListAsync();

        // Space groups (21.1) only for spaces the caller can view, and in the
        // default view only under a chosen space or a search: an instance
        // with many spaces has four groups for each.
        var viewable = rows.Any(r => r.SpaceId is not null) ? await perms.ViewableSpaceIdsAsync() : [];
        string KindOf(Guid id, Guid? spaceId) =>
            spaceId is not null ? Kinds.Space
            : BuiltInGroups.IsComputed(id) ? Kinds.BuiltIn
            : BuiltInGroups.IsGlobal(id) ? Kinds.Global
            : Kinds.Custom;
        string NameOf(string name, string? spaceName, SpaceGroupRole? role) =>
            role is { } r && spaceName is not null ? SpaceGroups.DisplayName(spaceName, r) : name;

        var candidates = rows
            .Where(r => r.SpaceId is null || viewable.Contains(r.SpaceId.Value))
            .Where(r => spaceFilter is null || r.SpaceId == spaceFilter)
            .Where(r => kind is null || KindOf(r.Id, r.SpaceId) == kind)
            .Where(r => r.SpaceId is null || spaceFilter is not null || query.Length > 0)
            .ToList();

        // -- the member search: who matches, then which groups they are in
        var matchesByGroup = new Dictionary<Guid, List<OverviewMatch>>();
        var truncated = false;
        if (query.Length > 0)
        {
            var lowered = query.ToLowerInvariant();
            var people = await db.Users.AsNoTracking()
                .Where(u => u.DisplayName.ToLower().Contains(lowered) || (showEmail && u.Email.Contains(lowered)))
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role, u.Status })
                .Take(MaxMatchedPeople + 1)
                .ToListAsync();
            truncated = people.Count > MaxMatchedPeople;
            people = people.Take(MaxMatchedPeople).ToList();
            var ids = people.Select(p => p.Id).ToList();
            var me = current.RequireId();
            var byId = people.ToDictionary(p => p.Id,
                p => new OverviewMatch(p.Id, p.DisplayName, showEmail || p.Id == me ? p.Email : null));

            var memberships = await db.UserGroups.AsNoTracking()
                .Where(ug => ids.Contains(ug.UserId))
                .Select(ug => new { ug.GroupId, ug.UserId })
                .ToListAsync();
            foreach (var m in memberships)
                Add(matchesByGroup, m.GroupId, byId[m.UserId]);
            // The computed groups hold every active account of their tier.
            foreach (var p in people.Where(p => p.Status == UserStatus.Active))
                foreach (var g in BuiltInGroups.For(p.Role))
                    Add(matchesByGroup, g, byId[p.Id]);

            candidates = candidates
                .Where(r => NameOf(r.Name, r.SpaceName, r.SpaceRole).Contains(query, StringComparison.OrdinalIgnoreCase)
                            || matchesByGroup.ContainsKey(r.Id))
                .ToList();
        }

        var groupIds = candidates.Select(c => c.Id).ToList();

        // -- members counted: active ones, and suspended ones apart
        var counts = (await db.UserGroups.AsNoTracking()
                .Where(ug => groupIds.Contains(ug.GroupId))
                .GroupBy(ug => new { ug.GroupId, Active = ug.User!.Status == UserStatus.Active })
                .Select(g => new { g.Key.GroupId, g.Key.Active, Count = g.Count() })
                .ToListAsync())
            .ToLookup(c => c.GroupId);
        var computedCounts = new Dictionary<Guid, int>();
        foreach (var id in groupIds.Where(BuiltInGroups.IsComputed))
            computedCounts[id] = await BuiltInGroups.Members(db, id).CountAsync();

        // -- what each grants where
        var grantRows = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.PrincipalType == PrincipalType.Group && groupIds.Contains(p.PrincipalId))
            .Select(p => new { p.PrincipalId, p.SpaceId, p.Operation, p.Space!.Key, p.Space.Name })
            .ToListAsync();
        var seeAccess = new Dictionary<Guid, bool>();
        async Task<bool> MaySeeAsync(Guid spaceId)
        {
            if (seeAccess.TryGetValue(spaceId, out var known)) return known;
            return seeAccess[spaceId] = await perms.CanViewSpaceAsync(spaceId) && await CanSeeSpaceAccessAsync(spaceId, perms, rights);
        }
        var grants = new Dictionary<Guid, List<OverviewGrant>>();
        foreach (var g in grantRows)
        {
            var group = candidates.First(c => c.Id == g.PrincipalId);
            // A space's own group holds one fixed grant its name already
            // says; any other grant only where the caller may see the
            // space's permissions.
            if (group.SpaceId != g.SpaceId && !await MaySeeAsync(g.SpaceId)) continue;
            Add(grants, g.PrincipalId, new OverviewGrant(g.SpaceId, g.Key, g.Name, g.Operation));
        }

        // -- page restrictions naming it, cheap enough to list
        var restrictionRows = await db.PageRestrictions.AsNoTracking()
            .Where(r => r.PrincipalType == PrincipalType.Group && groupIds.Contains(r.PrincipalId)
                        && r.Page!.DeletedAt == null)
            .OrderBy(r => r.Page!.Title)
            .Select(r => new { r.PrincipalId, r.PageId, r.Page!.Title, r.Operation, SpaceKey = r.Page.Space!.Key })
            .Take(MaxRestrictions)
            .ToListAsync();
        var readable = new Dictionary<Guid, bool>();
        var restrictions = new Dictionary<Guid, List<OverviewRestriction>>();
        foreach (var r in restrictionRows)
        {
            if (!readable.TryGetValue(r.PageId, out var ok))
                readable[r.PageId] = ok = await perms.CanReadPageAsync(r.PageId);
            if (ok) Add(restrictions, r.PrincipalId, new OverviewRestriction(r.PageId, r.Title, r.SpaceKey, r.Operation));
        }

        // -- who may change each one's members, by 21.1's rules; the
        // endpoint already requires Manage Groups, which covers the rest
        var spaceRights = new Dictionary<(Guid, bool), bool>();
        async Task<bool> CanManageAsync(Guid id, Guid? spaceId, SpaceGroupRole? role)
        {
            if (BuiltInGroups.IsComputed(id)) return false;
            if (spaceId is not { } s) return true;
            var adminsGroup = role == SpaceGroupRole.Admins;
            if (spaceRights.TryGetValue((s, adminsGroup), out var known)) return known;
            return spaceRights[(s, adminsGroup)] = adminsGroup
                ? await perms.IsExplicitSpaceAdminAsync(s)
                : await perms.CanAdminSpaceAsync(s);
        }

        var result = new List<OverviewGroup>();
        foreach (var c in candidates)
        {
            var groupCounts = counts[c.Id].ToList();
            var active = BuiltInGroups.IsComputed(c.Id)
                ? computedCounts[c.Id]
                : groupCounts.Where(x => x.Active).Sum(x => x.Count);
            var suspended = BuiltInGroups.IsComputed(c.Id) ? 0 : groupCounts.Where(x => !x.Active).Sum(x => x.Count);
            result.Add(new OverviewGroup(
                c.Id, NameOf(c.Name, c.SpaceName, c.SpaceRole), c.Description, KindOf(c.Id, c.SpaceId),
                BuiltInGroups.IsComputed(c.Id), c.SpaceId, c.SpaceKey, c.SpaceName, c.SpaceRole,
                active, suspended, await CanManageAsync(c.Id, c.SpaceId, c.SpaceRole), BuiltInGroups.IsGlobal(c.Id),
                (grants.GetValueOrDefault(c.Id) ?? []).OrderBy(g => g.SpaceName, StringComparer.OrdinalIgnoreCase).ToList(),
                restrictions.GetValueOrDefault(c.Id) ?? [],
                matchesByGroup.GetValueOrDefault(c.Id)?.Take(10).ToList() ?? []));
        }

        return Results.Ok(new OverviewResponse(Ordered(result), truncated));
    }

    /// <summary>
    /// Built-in first, in their fixed order, then global, custom by name,
    /// and space groups by space name, each space's in its Permissions tab's
    /// order (Admins, Editors, Viewers, Reviewers).
    /// </summary>
    private static List<OverviewGroup> Ordered(List<OverviewGroup> groups)
    {
        int Rank(OverviewGroup g) => g.Kind switch
        {
            Kinds.BuiltIn => 0,
            Kinds.Global => 1,
            Kinds.Custom => 2,
            _ => 3,
        };
        return groups
            .OrderBy(Rank)
            .ThenBy(g => Array.IndexOf(BuiltInGroups.InOrder, g.Id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(g => g.SpaceName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.SpaceId)
            .ThenBy(g => g.SpaceRole is { } r ? Array.IndexOf(SpaceGroups.Roles, r) : 0)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Add<T>(Dictionary<Guid, List<T>> map, Guid key, T value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (!list.Contains(value)) list.Add(value);
    }
}
