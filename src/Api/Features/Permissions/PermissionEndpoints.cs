using Tesria.Api.Domain;
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

    public record RestrictPageRequest(PrincipalType PrincipalType, Guid PrincipalId, PageOperation Operation);
    public record PageRestrictionResponse(
        Guid Id, PrincipalType PrincipalType, Guid PrincipalId, string? PrincipalName, PageOperation Operation);

    public static IEndpointRouteBuilder MapPermissionEndpoints(this IEndpointRouteBuilder routes)
    {
        var space = routes.MapGroup("/spaces/{key}/permissions")
            .WithTags("Permissions").RequireAuthorization();
        space.MapGet("/", ListSpacePermissions);
        space.MapPost("/", GrantSpacePermission);
        space.MapDelete("/{id:guid}", RevokeSpacePermission);
        space.MapDelete("/", MakeSpaceOpen);

        var page = routes.MapGroup("/pages/{pageId:guid}/restrictions")
            .WithTags("Permissions").RequireAuthorization();
        page.MapGet("/", ListPageRestrictions);
        page.MapPost("/", AddPageRestriction);
        page.MapDelete("/{id:guid}", RemovePageRestriction);

        return routes;
    }

    // -- space permissions ----------------------------------------------------

    private static async Task<IResult> ListSpacePermissions(
        string key, AppDbContext db, IPermissionService perms)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var rows = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.SpaceId == space.Id)
            .ToListAsync();
        var names = await PrincipalNamesAsync(db, rows.Select(r => r.PrincipalId));

        return Results.Ok(rows.Select(r => new SpacePermissionResponse(
            r.Id, r.PrincipalType, r.PrincipalId,
            names.GetValueOrDefault(r.PrincipalId), r.Operation)));
    }

    private static async Task<IResult> GrantSpacePermission(
        string key, GrantSpaceRequest req, AppDbContext db,
        IPermissionService perms, CurrentUser current, IAuditLogger audit)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        if (!await PrincipalExistsAsync(db, req.PrincipalType, req.PrincipalId))
            return Results.ValidationProblem(Error("principalId", "Principal not found."));

        var isFirst = !await db.SpacePermissions.AnyAsync(p => p.SpaceId == space.Id);

        var exists = await db.SpacePermissions.AnyAsync(p =>
            p.SpaceId == space.Id && p.PrincipalType == req.PrincipalType
            && p.PrincipalId == req.PrincipalId && p.Operation == req.Operation);
        if (!exists)
        {
            db.SpacePermissions.Add(New(space.Id, req.PrincipalType, req.PrincipalId, req.Operation));
            audit.Record("space.permission_granted", "space", space.Id,
                new { req.PrincipalType, req.PrincipalId, req.Operation });
        }

        // Locking yourself out is the classic footgun: the moment a space stops
        // being default-open, make sure the acting admin keeps admin access.
        if (isFirst && current.Id is { } actorId)
        {
            var keepsAdmin = req is { PrincipalType: PrincipalType.User, Operation: SpaceOperation.Admin }
                             && req.PrincipalId == actorId;
            if (!keepsAdmin)
            {
                db.SpacePermissions.Add(New(space.Id, PrincipalType.User, actorId, SpaceOperation.Admin));
                audit.Record("space.permission_granted", "space", space.Id,
                    new { PrincipalType = PrincipalType.User, PrincipalId = actorId, Operation = SpaceOperation.Admin, Reason = "grantor retains admin" });
            }
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Makes a private space open again (dev-plan 15.3): every grant goes, so
    /// every signed-in user can view, edit and administer it. The last-admin
    /// rule made this unreachable one grant at a time. A space administrator,
    /// with the password again, audited and alerted, because it widens access
    /// to everything in the space at once.
    /// </summary>
    private static async Task<IResult> MakeSpaceOpen(
        string key, AppDbContext db, IPermissionService perms, IAuditLogger audit, CurrentUser current,
        Infrastructure.Security.ISecurityDetector detector, HttpContext http, IConfiguration config)
    {
        var space = await FindSpaceAsync(db, key);
        // A space the caller cannot see is not found, not forbidden (dev-plan 14.1).
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        if (Features.Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;

        var rows = await db.SpacePermissions.Where(p => p.SpaceId == space.Id).ToListAsync();
        if (rows.Count == 0) return Results.NoContent();
        db.SpacePermissions.RemoveRange(rows);
        audit.Record("space.opened", "space", space.Id, new { space.Key, space.Name, Grants = rows.Count });
        await detector.SpaceOpenedAsync(current.RequireId(), space.Id, space.Key);
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

        // Refuse to remove the last admin: that would leave the space
        // unmanageable (it does not fall back to default-open while other
        // permission rows remain).
        if (row.Operation == SpaceOperation.Admin)
        {
            var otherAdmins = await db.SpacePermissions
                .CountAsync(p => p.SpaceId == space.Id && p.Operation == SpaceOperation.Admin && p.Id != id);
            if (otherAdmins == 0)
                return Results.Conflict(new { message = "Cannot remove the last admin of the space." });
        }

        db.SpacePermissions.Remove(row);
        audit.Record("space.permission_revoked", "space", space.Id,
            new { row.PrincipalType, row.PrincipalId, row.Operation });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

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
        if (!await PrincipalExistsAsync(db, req.PrincipalType, req.PrincipalId))
            return Results.ValidationProblem(Error("principalId", "Principal not found."));

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

    private static async Task<bool> PrincipalExistsAsync(AppDbContext db, PrincipalType type, Guid id) =>
        type == PrincipalType.User
            ? await db.Users.AnyAsync(u => u.Id == id)
            : await db.Groups.AnyAsync(g => g.Id == id);

    /// <summary>Display names for user/group principals, for readable listings.</summary>
    private static async Task<Dictionary<Guid, string>> PrincipalNamesAsync(
        AppDbContext db, IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        var groups = await db.Groups.AsNoTracking()
            .Where(g => idList.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name);
        foreach (var (id, name) in groups) users[id] = name;
        return users;
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
