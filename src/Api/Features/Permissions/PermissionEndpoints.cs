using Tesria.Api.Domain;
using Tesria.Api.Features.Groups;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Permissions;

public static class PermissionEndpoints
{
    public record GrantSpaceRequest(PrincipalType PrincipalType, Guid PrincipalId, SpaceOperation Operation);
    public record SpacePermissionResponse(
        Guid Id, PrincipalType PrincipalType, Guid PrincipalId, string? PrincipalName, SpaceOperation Operation);

    /// <summary>
    /// A space's access, as its Permissions tab shows it (dev-plan 21.1): what
    /// everyone signed in gets, the space's own four groups with their
    /// members, then every other grant. Before 21.1 this was a bare list of
    /// grants in which an empty list meant "open"; that meaning is gone, so
    /// the shape changed with it rather than keep a list that now misleads.
    /// </summary>
    /// <param name="EveryoneAccess">What every signed-in, active account may do here: null for nothing.</param>
    /// <param name="CanManageAdmins">
    /// Whether the caller may change the Admins group and Admin grants: an
    /// explicit administrator only, never the implicit one an EveryoneAccess
    /// of Admin makes of everyone.
    /// </param>
    /// <param name="GlobalViewers">Active members of Global Viewers, who can read this space whatever it says here.</param>
    /// <param name="GlobalReviewers">Active members of Global Reviewers, likewise.</param>
    /// <param name="Groups">The space's four groups: Admins, Editors, Viewers, Reviewers.</param>
    /// <param name="Grants">Every other grant, to people and to global or custom groups.</param>
    public record SpaceAccessResponse(
        SpaceOperation? EveryoneAccess,
        bool CanManageAdmins,
        int GlobalViewers,
        int GlobalReviewers,
        List<SpaceGroupResponse> Groups,
        List<SpacePermissionResponse> Grants);

    /// <summary>One of a space's groups, with the fixed level it holds.</summary>
    public record SpaceGroupResponse(
        Guid Id, SpaceGroupRole Role, string Name, SpaceOperation Operation,
        List<GroupEndpoints.MemberResponse> Members);

    /// <summary>What every signed-in account may do in the space: null (nothing), View, Edit or Admin.</summary>
    public record EveryoneAccessRequest(SpaceOperation? Access);

    public record RestrictPageRequest(PrincipalType PrincipalType, Guid PrincipalId, PageOperation Operation);
    public record PageRestrictionResponse(
        Guid Id, PrincipalType PrincipalType, Guid PrincipalId, string? PrincipalName, PageOperation Operation);

    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder routes)
    {
        var space = routes.MapGroup("/spaces/{key}/permissions")
            .WithTags("Permissions").RequireAuthorization();
        space.MapGet("/", ListSpacePermissions).Produces<SpaceAccessResponse>();
        space.MapPost("/", GrantSpacePermission).Produces(StatusCodes.Status204NoContent);
        space.MapDelete("/{id:guid}", RevokeSpacePermission).Produces(StatusCodes.Status204NoContent);
        space.MapPut("/everyone", SetEveryoneAccess).Produces(StatusCodes.Status204NoContent);
        // Kept from 15.3, where it removed every grant: now it is "everyone
        // signed in may administer", which is what that used to mean.
        space.MapDelete("/", MakeSpaceOpen).Produces(StatusCodes.Status204NoContent);

        var page = routes.MapGroup("/pages/{pageId:guid}/restrictions")
            .WithTags("Permissions").RequireAuthorization();
        page.MapGet("/", ListPageRestrictions).Produces<List<PageRestrictionResponse>>();
        page.MapPost("/", AddPageRestriction).Produces(StatusCodes.Status204NoContent);
        page.MapDelete("/{id:guid}", RemovePageRestriction).Produces(StatusCodes.Status204NoContent);

        return routes;
    }

    // -- space permissions ----------------------------------------------------

    private static async Task<IResult> ListSpacePermissions(
        string key, AppDbContext db, IPermissionService perms, CurrentUser current, IInstancePermissions rights)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var groups = await db.Groups.AsNoTracking()
            .Where(g => g.SpaceId == space.Id)
            .Select(g => new { g.Id, g.SpaceRole })
            .ToListAsync();
        var groupIds = groups.Select(g => g.Id).ToList();
        var showEmail = await rights.HasAsync(InstancePermissions.UsersView);
        var me = current.RequireId();
        var members = (await db.UserGroups.AsNoTracking()
                .Where(ug => groupIds.Contains(ug.GroupId))
                .Select(ug => new { ug.GroupId, ug.UserId, ug.User!.Email, ug.User.DisplayName, ug.User.Status })
                .ToListAsync())
            .ToLookup(m => m.GroupId);

        var rows = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.SpaceId == space.Id && !(p.PrincipalType == PrincipalType.Group && groupIds.Contains(p.PrincipalId)))
            .ToListAsync();
        rows = rows.OrderBy(r => r.CreatedAt).ToList();
        var names = await PrincipalNamesAsync(db, rows.Select(r => r.PrincipalId));

        var groupRows = new List<SpaceGroupResponse>();
        foreach (var role in SpaceGroups.Roles)
        {
            var g = groups.FirstOrDefault(x => x.SpaceRole == role);
            if (g is null) continue;
            groupRows.Add(new SpaceGroupResponse(
                g.Id, role, SpaceGroups.DisplayName(space.Name, role), SpaceGroups.OperationOf(role),
                members[g.Id]
                    .OrderBy(m => m.DisplayName)
                    .Select(m => new GroupEndpoints.MemberResponse(
                        m.UserId, showEmail || m.UserId == me ? m.Email : null, m.DisplayName,
                        m.Status == UserStatus.Active))
                    .ToList()));
        }

        return Results.Ok(new SpaceAccessResponse(
            space.EveryoneAccess,
            await perms.IsExplicitSpaceAdminAsync(space.Id),
            await ActiveMembersAsync(db, BuiltInGroups.GlobalViewersId),
            await ActiveMembersAsync(db, BuiltInGroups.GlobalReviewersId),
            groupRows,
            rows.Select(r => new SpacePermissionResponse(
                r.Id, r.PrincipalType, r.PrincipalId, names.GetValueOrDefault(r.PrincipalId), r.Operation)).ToList()));
    }

    private static Task<int> ActiveMembersAsync(AppDbContext db, Guid groupId) =>
        db.UserGroups.CountAsync(ug => ug.GroupId == groupId && ug.User!.Status == UserStatus.Active);

    private static async Task<IResult> GrantSpacePermission(
        string key, GrantSpaceRequest req, AppDbContext db,
        IPermissionService perms, IAuditLogger audit)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        if (!Enum.IsDefined(req.Operation))
            return Results.ValidationProblem(Error("operation", "Choose View, Edit or Admin."));
        if (await PrincipalProblemAsync(db, perms, req.PrincipalType, req.PrincipalId, space.Id) is { } problem)
            return problem;
        if (await db.Groups.AnyAsync(g => g.Id == req.PrincipalId && g.SpaceId == space.Id))
            return Results.Conflict(new
            {
                message = "That is one of this space's own groups, and already has its access here. Change who is in it instead.",
            });
        // Who administers a space is the explicit administrators' to decide
        // (21.1): an EveryoneAccess of Admin would otherwise let anyone make
        // themselves an explicit admin, past every page restriction.
        if (req.Operation == SpaceOperation.Admin && !await perms.IsExplicitSpaceAdminAsync(space.Id))
            return ExplicitAdminRefusal();

        // Granting no longer closes an open space (21.1): that is what
        // everyone signed in may do, set on its own. So there is no first
        // grant to protect the granter from, and nothing is added for them.
        var exists = await db.SpacePermissions.AnyAsync(p =>
            p.SpaceId == space.Id && p.PrincipalType == req.PrincipalType
            && p.PrincipalId == req.PrincipalId && p.Operation == req.Operation);
        if (!exists)
        {
            db.SpacePermissions.Add(New(space.Id, req.PrincipalType, req.PrincipalId, req.Operation));
            audit.Record("space.permission_granted", "space", space.Id,
                new { req.PrincipalType, req.PrincipalId, req.Operation });
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    /// <summary>
    /// What every signed-in account may do in the space (dev-plan 21.1). The
    /// space administrator's. Widening it keeps 15.3's make-open protections,
    /// because it lets everyone into everything at once: the password again,
    /// the <c>space.opened</c> audit entry and an alert to every
    /// administrator. Narrowing it from Admin is refused unless the person
    /// doing it stays an administrator, so nobody locks themselves out, and
    /// it ends the live-editing connections it no longer covers.
    /// </summary>
    private static async Task<IResult> SetEveryoneAccess(
        string key, EveryoneAccessRequest req, AppDbContext db, IPermissionService perms, IAuditLogger audit,
        CurrentUser current, Infrastructure.Security.ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        if (req.Access is { } level && !Enum.IsDefined(level))
            return Results.ValidationProblem(Error("access", "Choose No Access, View, Edit or Administer."));
        return await ChangeEveryoneAccessAsync(space, req.Access, db, perms, audit, current, detector, http, config);
    }

    /// <summary>
    /// The 15.3 route, kept as an alias (it is in the docs and the API
    /// client): everyone signed in may view, edit and administer the space.
    /// It no longer removes any grant.
    /// </summary>
    private static async Task<IResult> MakeSpaceOpen(
        string key, AppDbContext db, IPermissionService perms, IAuditLogger audit, CurrentUser current,
        Infrastructure.Security.ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        return await ChangeEveryoneAccessAsync(space, SpaceOperation.Admin, db, perms, audit, current, detector, http, config);
    }

    private static async Task<IResult> ChangeEveryoneAccessAsync(
        Space space, SpaceOperation? to, AppDbContext db, IPermissionService perms, IAuditLogger audit,
        CurrentUser current, Infrastructure.Security.ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        var from = space.EveryoneAccess;
        if (from == to) return Results.NoContent();

        var widens = to is { } t && (from is null || t > from.Value);
        if (widens)
        {
            if (Features.Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        }
        else if (from == SpaceOperation.Admin && !await perms.IsExplicitSpaceAdminAsync(space.Id))
        {
            // Covers the last-admin rule too: the person doing it is one.
            return Results.Conflict(new
            {
                message = "You would no longer administer this space. Add yourself to its Admins group first, or ask one of its administrators.",
            });
        }

        space.EveryoneAccess = to;
        var change = new { space.Key, space.Name, From = from?.ToString(), To = to?.ToString() };
        if (widens)
        {
            audit.Record("space.opened", "space", space.Id, change);
            await detector.SpaceOpenedAsync(current.RequireId(), space.Id, space.Key);
        }
        else
        {
            audit.Record("space.everyone_access_changed", "space", space.Id, change);
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeSpacePermission(
        string key, Guid id, AppDbContext db, IPermissionService perms, IAuditLogger audit)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var row = await db.SpacePermissions.FirstOrDefaultAsync(p => p.Id == id && p.SpaceId == space.Id);
        if (row is null) return Results.NotFound();

        // The fixed grant of one of the space's own groups (21.1).
        if (row.PrincipalType == PrincipalType.Group
            && await db.Groups.AnyAsync(g => g.Id == row.PrincipalId && g.SpaceId == space.Id))
            return Results.Conflict(new
            {
                message = "This is the access of one of the space's own groups, and cannot be removed. Change who is in the group instead.",
            });

        if (row.Operation == SpaceOperation.Admin)
        {
            if (!await perms.IsExplicitSpaceAdminAsync(space.Id)) return ExplicitAdminRefusal();
            // Refuse to remove the last admin: that would leave the space
            // unmanageable but for recover-access.
            if (!await SpaceGroups.HasAdminAfterAsync(db, space.Id, new SpaceGroups.AdminLoss(RevokedGrantId: row.Id)))
                return Results.Conflict(new { message = "Cannot remove the last admin of the space." });
        }

        db.SpacePermissions.Remove(row);
        audit.Record("space.permission_revoked", "space", space.Id,
            new { row.PrincipalType, row.PrincipalId, row.Operation });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>The refusal for an implicit administrator reaching for who administers the space.</summary>
    internal static IResult ExplicitAdminRefusal() => Results.Json(new
    {
        message = "Only the space's own administrators can change who administers it: the people in its Admins group, or given Admin here.",
    }, statusCode: StatusCodes.Status403Forbidden);

    // -- page restrictions ----------------------------------------------------

    private static async Task<IResult> ListPageRestrictions(
        Guid pageId, AppDbContext db, IPermissionService perms)
    {
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();

        var rows = await db.PageRestrictions.AsNoTracking()
            .Where(r => r.PageId == pageId)
            .ToListAsync();
        var names = await PrincipalNamesAsync(db, rows.Select(r => r.PrincipalId));

        return Results.Ok(rows.Select(r => new PageRestrictionResponse(
            r.Id, r.PrincipalType, r.PrincipalId,
            names.GetValueOrDefault(r.PrincipalId), r.Operation)));
    }

    private static async Task<IResult> AddPageRestriction(
        Guid pageId, RestrictPageRequest req, AppDbContext db,
        IPermissionService perms, CurrentUser current, IAuditLogger audit)
    {
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(pageId)) return Results.Forbid();
        var spaceId = await db.Pages.IgnoreQueryFilters().Where(p => p.Id == pageId).Select(p => p.SpaceId).FirstAsync();
        if (await PrincipalProblemAsync(db, perms, req.PrincipalType, req.PrincipalId, spaceId) is { } problem)
            return problem;

        var isFirst = !await db.PageRestrictions.AnyAsync(r => r.PageId == pageId);

        var exists = await db.PageRestrictions.AnyAsync(r =>
            r.PageId == pageId && r.PrincipalType == req.PrincipalType
            && r.PrincipalId == req.PrincipalId && r.Operation == req.Operation);
        if (!exists)
        {
            db.PageRestrictions.Add(new PageRestriction
            {
                Id = Guid.NewGuid(),
                PageId = pageId,
                PrincipalType = req.PrincipalType,
                PrincipalId = req.PrincipalId,
                Operation = req.Operation,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            audit.Record("page.restricted", "page", pageId,
                new { req.PrincipalType, req.PrincipalId, req.Operation });
        }

        // Same anti-lockout guard as spaces: whoever restricts the page keeps access.
        if (isFirst && current.Id is { } actorId
            && !(req.PrincipalType == PrincipalType.User && req.PrincipalId == actorId))
        {
            db.PageRestrictions.Add(new PageRestriction
            {
                Id = Guid.NewGuid(),
                PageId = pageId,
                PrincipalType = PrincipalType.User,
                PrincipalId = actorId,
                Operation = req.Operation,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RemovePageRestriction(
        Guid pageId, Guid id, AppDbContext db, IPermissionService perms, IAuditLogger audit)
    {
        if (!await perms.CanViewPageAsync(pageId)) return Results.NotFound();
        if (!await perms.CanEditPageAsync(pageId)) return Results.Forbid();

        var row = await db.PageRestrictions.FirstOrDefaultAsync(r => r.Id == id && r.PageId == pageId);
        if (row is null) return Results.NotFound();

        db.PageRestrictions.Remove(row);
        audit.Record("page.unrestricted", "page", pageId,
            new { row.PrincipalType, row.PrincipalId, row.Operation });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // -- helpers --------------------------------------------------------------

    private static SpacePermission New(Guid spaceId, PrincipalType type, Guid principalId, SpaceOperation op) =>
        new()
        {
            Id = Guid.NewGuid(),
            SpaceId = spaceId,
            PrincipalType = type,
            PrincipalId = principalId,
            Operation = op,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static Task<Space?> FindSpaceAsync(AppDbContext db, string key)
    {
        var normalized = (key ?? "").ToUpperInvariant();
        return db.Spaces.FirstOrDefaultAsync(s => s.Key == normalized);
    }

    /// <summary>
    /// Why a person or group cannot be named in this space's grants or page
    /// restrictions, or null. A space's group may only be named in its own
    /// space (21.1); one of a space the caller cannot see is "not found", so
    /// the refusal does not say that it exists.
    /// </summary>
    private static async Task<IResult?> PrincipalProblemAsync(
        AppDbContext db, IPermissionService perms, PrincipalType type, Guid id, Guid spaceId)
    {
        if (type == PrincipalType.User)
            return await db.Users.AnyAsync(u => u.Id == id)
                ? null
                : Results.ValidationProblem(Error("principalId", "No such person or group."));
        if (type != PrincipalType.Group)
            return Results.ValidationProblem(Error("principalType", "Choose a person or a group."));

        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == id).Select(g => new { g.SpaceId }).FirstOrDefaultAsync();
        if (group is null) return Results.ValidationProblem(Error("principalId", "No such person or group."));
        if (group.SpaceId is { } owner && owner != spaceId)
            return await perms.CanViewSpaceAsync(owner)
                ? Results.ValidationProblem(Error("principalId",
                    "That group belongs to another space. A space's groups can only be given access in their own space."))
                : Results.ValidationProblem(Error("principalId", "No such person or group."));
        return null;
    }

    /// <summary>Display names for user/group principals, for readable listings.</summary>
    private static async Task<Dictionary<Guid, string>> PrincipalNamesAsync(
        AppDbContext db, IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        foreach (var (id, name) in await SpaceGroups.NamesAsync(db, idList)) users[id] = name;
        return users;
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
