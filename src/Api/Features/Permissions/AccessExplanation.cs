using Tesria.Api.Domain;
using Tesria.Api.Features.Groups;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Permissions;

/// <summary>
/// "Why can this person see this space?" (dev-plan 21.4): for one person and
/// one space or page, the answer and every reason behind it.
/// <para>
/// The answer is never worked out here: it is <see cref="IPermissionService"/>
/// evaluated as that person (<see cref="IPermissionService.AsUser"/>), the
/// same check every request makes, so the page cannot say yes where the
/// wiki says no. The reasons are read from the same rows the check reads,
/// and the tests hold them to the answer across a matrix of cases; a
/// disagreement here is logged, and the answer shown is still the real one.
/// </para>
/// <para>
/// Asking is for whoever may see the space's permissions
/// (<see cref="GroupOverview.CanSeeSpaceAccessAsync"/>), and never about a
/// space or page the asker cannot see: those answer not found, the way the
/// space or page itself does.
/// </para>
/// </summary>
public static class AccessExplanation
{
    /// <summary>Why a person gets something in a space.</summary>
    public static class ReasonKinds
    {
        /// <summary>The space's EveryoneAccess: every signed-in, active account.</summary>
        public const string Everyone = "everyone";
        /// <summary>Global Viewers or Global Reviewers: View on every space.</summary>
        public const string Global = "global";
        /// <summary>A grant to a group they are in.</summary>
        public const string Group = "group";
        /// <summary>A grant to them by name.</summary>
        public const string Direct = "direct";
    }

    /// <param name="Active">False for a suspended account, which cannot sign in at all.</param>
    public record Person(Guid Id, string DisplayName, string? Email, bool Active);

    public record SpaceInfo(Guid Id, string Key, string Name, SpaceOperation? EveryoneAccess, bool Archived);

    /// <param name="Counts">
    /// False where the person holds it but it gives them nothing: what
    /// everyone signed in gets, and the global groups, for an account that
    /// is suspended.
    /// </param>
    /// <param name="GroupKind">For a group: built-in, global, custom or space (<see cref="GroupOverview.Kinds"/>).</param>
    /// <param name="RecoveredAt">For a space's Admins group: when they added themselves with Get Access, if they did.</param>
    public record Reason(
        string Kind, SpaceOperation Level, bool Counts, string Label,
        Guid? GroupId = null, string? GroupKind = null, DateTimeOffset? RecoveredAt = null);

    /// <param name="Inherited">Set on an ancestor rather than the page itself.</param>
    /// <param name="Matches">Whether the person is the one named, or in the group named.</param>
    public record Restriction(
        Guid PageId, string PageTitle, bool Inherited, PageOperation Operation,
        PrincipalType PrincipalType, Guid PrincipalId, string PrincipalName, bool Matches);

    /// <param name="CanView">Whether they can read it: <see cref="IPermissionService.CanReadPageAsync"/> as them.</param>
    /// <param name="CanEdit"><see cref="IPermissionService.CanEditPageAsync"/> as them.</param>
    /// <param name="Draft">Not yet published: only its author and the space's editors read it.</param>
    /// <param name="IsAuthor">They started it, which matters only for a draft.</param>
    /// <param name="CanLift">
    /// An explicit administrator of the space: restrictions bind them like
    /// anyone (21.6), but they may lift them.
    /// </param>
    public record PageAnswer(
        Guid Id, string Title, bool CanView, bool CanEdit, bool Draft, bool IsAuthor, bool CanLift,
        List<Restriction> Restrictions);

    /// <param name="Level">What they may do in the space, from the real check: null for nothing.</param>
    /// <param name="ExplicitAdmin">They hold a real Admin grant: they may change who administers it, and lift page restrictions.</param>
    /// <param name="CanRecoverAccess">
    /// They are not an explicit administrator but hold Manage spaces, so
    /// Get Access in Administration would make them one (audited).
    /// </param>
    /// <param name="PubliclyReadable">The space can be read without an account at all.</param>
    public record Response(
        Person Person, SpaceInfo Space, SpaceOperation? Level, bool ExplicitAdmin,
        List<Reason> Reasons, PageAnswer? Page, bool CanRecoverAccess, bool PubliclyReadable);

    public static IEndpointRouteBuilder MapAccessExplanationEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/access/explain", Explain)
            .WithTags("Permissions").RequireAuthorization()
            .Produces<Response>();
        return routes;
    }

    private static async Task<IResult> Explain(
        Guid userId, string? space, Guid? pageId,
        AppDbContext db, IPermissionService perms, IInstancePermissions rights, ILoggerFactory logs)
    {
        // -- what is asked about, and whether the asker may see it
        Guid spaceId;
        if (pageId is { } pid)
        {
            // A trashed page, or a draft the asker may not read, is not found
            // here as everywhere else.
            if (!await perms.CanReadPageAsync(pid)) return Results.NotFound();
            spaceId = await db.Pages.AsNoTracking().IgnoreQueryFilters().Where(p => p.Id == pid).Select(p => p.SpaceId).FirstAsync();
        }
        else if (!string.IsNullOrWhiteSpace(space))
        {
            var key = space.Trim().ToUpperInvariant();
            var id = await db.Spaces.AsNoTracking().Where(s => s.Key == key).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
            if (id is null || !await perms.CanViewSpaceAsync(id.Value)) return Results.NotFound();
            spaceId = id.Value;
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["space"] = ["Choose a space or a page."],
            });
        }
        if (!await GroupOverview.CanSeeSpaceAccessAsync(spaceId, perms, rights))
            return Results.Json(new
            {
                message = "Only the space's administrators, and people who may see users and spaces in Administration, can see why someone has access here.",
            }, statusCode: StatusCodes.Status403Forbidden);

        var account = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role, u.Status }).FirstOrDefaultAsync();
        if (account is null) return Results.NotFound(new { message = "There is no such person." });

        var showEmail = await rights.HasAsync(InstancePermissions.UsersView);
        var spaceRow = await db.Spaces.AsNoTracking().Where(s => s.Id == spaceId)
            .Select(s => new { s.Id, s.Key, s.Name, s.EveryoneAccess, s.Archived }).FirstAsync();

        // -- the real answer: the permission service, as them
        var asThem = perms.AsUser(userId);
        SpaceOperation? level =
            await asThem.CanAdminSpaceAsync(spaceId) ? SpaceOperation.Admin
            : await asThem.CanEditSpaceAsync(spaceId) ? SpaceOperation.Edit
            : await asThem.CanViewSpaceAsync(spaceId) ? SpaceOperation.View
            : null;
        var explicitAdmin = await asThem.IsExplicitSpaceAdminAsync(spaceId);

        // -- the reasons, read from the rows that check reads
        var active = account.Status == UserStatus.Active;
        var (reasons, principals) = await ReasonsAsync(db, account.Id, account.DisplayName, account.Role, active, [spaceId]);
        var spaceReasons = reasons[spaceId];

        var explainedLevel = spaceReasons.Where(r => r.Counts).Select(r => (SpaceOperation?)r.Level).Max();
        var explainedAdmin = spaceReasons.Any(r => r.Kind is ReasonKinds.Direct or ReasonKinds.Group && r.Level == SpaceOperation.Admin);
        var log = logs.CreateLogger(typeof(AccessExplanation));
        if (explainedLevel != level || explainedAdmin != explicitAdmin)
            log.LogWarning("Access explanation disagrees with the permission check for {UserId} in {SpaceId}: explained {Explained}, checked {Checked}.",
                userId, spaceId, explainedLevel, level);

        // -- a page: the restrictions on it and its ancestors
        PageAnswer? pageAnswer = null;
        if (pageId is { } targetId)
        {
            var pages = await db.Pages.AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.SpaceId == spaceId)
                .Select(p => new { p.Id, p.ParentPageId, p.Title, p.Status, p.CreatedById })
                .ToDictionaryAsync(p => p.Id);
            var target = pages[targetId];
            var chain = new List<Guid>();
            Guid? cursor = targetId;
            // Hop limit guards against a cycle in corrupt data, as the check's does.
            for (var hops = 0; cursor is { } at && hops < 10_000; hops++)
            {
                chain.Add(at);
                cursor = pages.TryGetValue(at, out var p) ? p.ParentPageId : null;
            }
            var rows = await db.PageRestrictions.AsNoTracking()
                .Where(r => chain.Contains(r.PageId))
                .Select(r => new { r.PageId, r.PrincipalType, r.PrincipalId, r.Operation })
                .ToListAsync();
            var groupNames = await SpaceGroups.NamesAsync(db, rows.Where(r => r.PrincipalType == PrincipalType.Group).Select(r => r.PrincipalId));
            var userIds = rows.Where(r => r.PrincipalType == PrincipalType.User).Select(r => r.PrincipalId).Distinct().ToList();
            var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            var restrictions = rows
                .Select(r => new Restriction(
                    r.PageId, pages.TryGetValue(r.PageId, out var p) ? p.Title : "", r.PageId != targetId, r.Operation,
                    r.PrincipalType, r.PrincipalId,
                    (r.PrincipalType == PrincipalType.Group ? groupNames.GetValueOrDefault(r.PrincipalId) : userNames.GetValueOrDefault(r.PrincipalId))
                        ?? "Someone removed",
                    principals.Contains(r.PrincipalId)))
                // Nearest page first, then View before Edit, then by name.
                .OrderBy(r => chain.IndexOf(r.PageId))
                .ThenBy(r => r.Operation)
                .ThenBy(r => r.PrincipalName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var draft = target.Status == PageStatus.Draft;
            var isAuthor = target.CreatedById == userId;
            var canView = await asThem.CanReadPageAsync(targetId);
            var canEdit = await asThem.CanEditPageAsync(targetId);
            pageAnswer = new PageAnswer(target.Id, target.Title, canView, canEdit, draft, isAuthor, explicitAdmin, restrictions);

            bool Passes(PageOperation op) =>
                restrictions.All(r => r.Operation != op) || restrictions.Any(r => r.Operation == op && r.Matches);
            var explainedView = explainedLevel >= SpaceOperation.View
                && Passes(PageOperation.View)
                && (!draft || isAuthor || explainedLevel >= SpaceOperation.Edit);
            var explainedEdit = explainedLevel >= SpaceOperation.Edit
                && Passes(PageOperation.View) && Passes(PageOperation.Edit);
            if (explainedView != canView || explainedEdit != canEdit)
                log.LogWarning("Access explanation disagrees with the permission check for {UserId} on page {PageId}.", userId, targetId);
        }

        IReadOnlySet<string> theirRights = active ? await rights.ForUserAsync(userId) : new HashSet<string>();
        var canRecover = !explicitAdmin && theirRights.Contains(InstancePermissions.SpacesManage);
        var publicly = await perms.AsAnonymous().IsPubliclyViewableSpaceAsync(spaceId);

        return Results.Ok(new Response(
            new Person(account.Id, account.DisplayName, showEmail ? account.Email : null, active),
            new SpaceInfo(spaceRow.Id, spaceRow.Key, spaceRow.Name, spaceRow.EveryoneAccess, spaceRow.Archived),
            level, explicitAdmin, spaceReasons, pageAnswer, canRecover, publicly));
    }

    /// <summary>
    /// Every reason a person gets something in each of the given spaces,
    /// read from the rows <see cref="IPermissionService"/> reads, and the
    /// principal set it builds (the person, their stored groups, and the
    /// computed groups only while active). Shared by Check Access and the
    /// per-person view in Administration (21.5), so the two cannot word the
    /// same access differently. Ordered as shown: what counts first, then
    /// the highest level, then direct before group before global before
    /// everyone.
    /// </summary>
    internal static async Task<(Dictionary<Guid, List<Reason>> Reasons, HashSet<Guid> Principals)> ReasonsAsync(
        AppDbContext db, Guid userId, string displayName, UserRole role, bool active, IReadOnlyCollection<Guid> spaceIds)
    {
        var stored = await db.UserGroups.AsNoTracking().Where(ug => ug.UserId == userId).Select(ug => ug.GroupId).ToListAsync();
        var principals = new HashSet<Guid>([userId, .. stored]);
        if (active) principals.UnionWith(BuiltInGroups.For(role));

        var everyone = await db.Spaces.AsNoTracking().Where(s => spaceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.EveryoneAccess);
        var grants = await db.SpacePermissions.AsNoTracking()
            .Where(p => spaceIds.Contains(p.SpaceId) && principals.Contains(p.PrincipalId))
            .Select(p => new { p.SpaceId, p.PrincipalType, p.PrincipalId, p.Operation })
            .ToListAsync();
        var groupIds = grants.Where(g => g.PrincipalType == PrincipalType.Group).Select(g => g.PrincipalId).Distinct().ToList();
        var groupInfo = await db.Groups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.SpaceId, g.SpaceRole })
            .ToDictionaryAsync(g => g.Id);
        var names = await SpaceGroups.NamesAsync(db, groupIds);
        // Get Access (recover access) adds an administrator to the space's
        // Admins group, audited; say so beside that membership.
        var recovered = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "space.access_recovered" && a.ActorId == userId
                            && a.TargetId != null && spaceIds.Contains(a.TargetId.Value))
                .Select(a => new { SpaceId = a.TargetId!.Value, a.Sequence, a.CreatedAt })
                .ToListAsync())
            .GroupBy(a => a.SpaceId)
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.Sequence)!.CreatedAt);
        var globals = stored.Where(BuiltInGroups.IsGlobal).OrderBy(g => Array.IndexOf(BuiltInGroups.InOrder, g)).ToList();

        var result = new Dictionary<Guid, List<Reason>>();
        foreach (var spaceId in spaceIds)
        {
            var reasons = new List<Reason>();
            if (everyone.GetValueOrDefault(spaceId) is { } level)
                reasons.Add(new Reason(ReasonKinds.Everyone, level, active, "Everyone signed in"));
            foreach (var global in globals)
                reasons.Add(new Reason(ReasonKinds.Global, SpaceOperation.View, active,
                    global == BuiltInGroups.GlobalViewersId ? "Global Viewers" : "Global Reviewers",
                    global, GroupOverview.Kinds.Global));
            foreach (var grant in grants.Where(g => g.SpaceId == spaceId))
            {
                if (grant.PrincipalType == PrincipalType.User)
                {
                    reasons.Add(new Reason(ReasonKinds.Direct, grant.Operation, true, displayName));
                    continue;
                }
                var info = groupInfo.GetValueOrDefault(grant.PrincipalId);
                var kind = info?.SpaceId is not null ? GroupOverview.Kinds.Space
                    : BuiltInGroups.IsComputed(grant.PrincipalId) ? GroupOverview.Kinds.BuiltIn
                    : BuiltInGroups.IsGlobal(grant.PrincipalId) ? GroupOverview.Kinds.Global
                    : GroupOverview.Kinds.Custom;
                DateTimeOffset? recoveredAt = info?.SpaceRole == SpaceGroupRole.Admins && recovered.TryGetValue(spaceId, out var at) ? at : null;
                reasons.Add(new Reason(ReasonKinds.Group, grant.Operation, true,
                    names.GetValueOrDefault(grant.PrincipalId) ?? "A group", grant.PrincipalId, kind, recoveredAt));
            }
            result[spaceId] = reasons
                .OrderByDescending(r => r.Counts)
                .ThenByDescending(r => r.Level)
                .ThenBy(r => r.Kind switch
                {
                    ReasonKinds.Direct => 0, ReasonKinds.Group => 1, ReasonKinds.Global => 2, _ => 3,
                })
                .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        return (result, principals);
    }
}
