using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Administration → Roles (dev-plan 11.1): which role may do what.
///
/// Who may edit which row is the whole point. An administrator may widen or
/// narrow user-tier roles; only the owner may touch administrator or owner
/// rows, because an administrator who could edit their own role could grant
/// themselves everything and every other restriction here would be
/// decoration.
/// </summary>
public static class RoleEndpoints
{
    public record PermissionDto(string Key, string Area, string Label, string Description, string Scope);

    public record RoleDto(
        Guid Id, string? Key, string Name, string? Description, UserRole Tier, bool BuiltIn,
        string[] Permissions, int Members, bool Editable);

    public record MatrixResponse(
        IReadOnlyList<PermissionDto> Catalogue,
        IReadOnlyList<PermissionDto> Reserved,
        IReadOnlyList<RoleDto> Roles,
        DateTimeOffset? ReviewedAt,
        string? ReviewedByName);

    public record UpdatePermissionsRequest(string[] Permissions);
    public record CreateRoleRequest(string Name, string? Description, UserRole Tier, Guid? CopyFrom);
    public record UpdateRoleRequest(string Name, string? Description);

    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/roles").WithTags("Admin").RequireAuthorization();

        // Not RequirePermission(permissions.view): that right is assignable,
        // and an owner who cleared their own row would have no way back to the
        // matrix to undo it. Reaching this tab is "may see it, or may edit any
        // row", and the owner's edit right is reserved, so their way back is
        // never closed. Each handler still checks the row it touches.
        group.MapGet("", GetMatrix);
        group.MapPost("", CreateRole);
        group.MapPut("/{roleId:guid}", UpdateRole);
        group.MapDelete("/{roleId:guid}", DeleteRole);
        group.MapPut("/{roleId:guid}/permissions", UpdatePermissions);
        group.MapPost("/{roleId:guid}/reset", ResetPermissions);
        group.MapPost("/review", MarkReviewed);
        return routes;
    }

    /// <summary>May this caller reach the Roles tab at all.</summary>
    private static bool MayReachMatrix(IReadOnlySet<string> held) =>
        held.Contains(InstancePermissions.PermissionsView)
        || held.Contains(InstancePermissions.PermissionsEditUserTier)
        || held.Contains(InstancePermissions.PermissionsEditAdminTier);

    private static async Task<IResult> GetMatrix(
        AppDbContext db, IInstancePermissions rights, ISiteSettingsService settings, CurrentUser current)
    {
        var held = await rights.ForCurrentUserAsync();
        if (!MayReachMatrix(held)) return Results.Forbid();
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).ToListAsync();
        var counts = await db.Users.AsNoTracking()
            .Where(u => u.RoleId != null)
            .GroupBy(u => u.RoleId!.Value)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);
        var site = await settings.GetAsync();
        var reviewedBy = site.PermissionsReviewedById is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync()
            : null;

        return Results.Ok(new MatrixResponse(
            [.. InstancePermissions.All.Select(ToDto)],
            [.. InstancePermissions.Reserved.Select(ToDto)],
            [.. roles
                .OrderByDescending(r => r.Tier).ThenBy(r => r.BuiltIn ? 0 : 1).ThenBy(r => r.Name)
                .Select(r => new RoleDto(
                    r.Id, r.Key, r.Name, r.Description, r.Tier, r.BuiltIn,
                    [.. r.Permissions.Select(p => p.Key).Where(InstancePermissions.IsAssignable).Order()],
                    counts.GetValueOrDefault(r.Id),
                    MayEdit(r, held)))],
            site.PermissionsReviewedAt,
            reviewedBy));
    }

    /// <summary>
    /// Creates a role in the user or administrator tier (dev-plan 11.2).
    ///
    /// It starts as a copy of an existing role rather than empty: a role with
    /// no rights is useless, and copying the tier's built-in is what someone
    /// means by "like a user, but...".
    /// </summary>
    private static async Task<IResult> CreateRole(
        CreateRoleRequest req, AppDbContext db, IInstancePermissions rights, PermissionCache cache,
        CurrentUser current, IAuditLogger audit)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 60)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A name of 1 to 60 characters is required."],
            });
        // Exactly one account owns the instance, so a second owner-tier role
        // would describe nobody.
        if (req.Tier is not (UserRole.Member or UserRole.Admin))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tier"] = ["A role belongs to the user or the administrator tier."],
            });
        if (await Refused(req.Tier, rights) is { } refusal) return refusal;
        if (await db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower()))
            return Results.Conflict(new { message = $"A role called '{name}' already exists." });

        // Copy from the named role, or from the tier's built-in.
        var source = req.CopyFrom is { } from
            ? await db.Roles.AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == from)
            : await db.Roles.AsNoTracking().Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Key == Role.Keys.For(req.Tier));
        if (req.CopyFrom is not null && source is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["copyFrom"] = ["That role no longer exists."],
            });
        // Copying a role of a higher tier would hand out rights the caller may
        // not be able to grant directly.
        if (source is not null && source.Tier > req.Tier && !await rights.HasAsync(InstancePermissions.PermissionsEditAdminTier))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["copyFrom"] = ["You cannot copy a role from a higher tier."],
            });

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Key = null,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Tier = req.Tier,
            BuiltIn = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = current.RequireId(),
        };
        role.Permissions = [.. (source?.Permissions.Select(p => p.Key) ?? InstancePermissions.DefaultsFor(req.Tier))
            .Where(InstancePermissions.IsAssignable)
            .Distinct()
            .Select(k => new RolePermission { RoleId = role.Id, Key = k })];
        db.Roles.Add(role);

        audit.Record("role.created", "role", role.Id,
            new { role.Name, Tier = role.Tier.ToString(), CopiedFrom = source?.Name });
        await db.SaveChangesAsync();
        cache.Invalidate();

        return Results.Created($"/api/admin/roles/{role.Id}", new RoleDto(
            role.Id, role.Key, role.Name, role.Description, role.Tier, role.BuiltIn,
            [.. role.Permissions.Select(p => p.Key).Order()], 0, true));
    }

    /// <summary>Renames a custom role. Built-in names are fixed: the seed recreates them by key.</summary>
    private static async Task<IResult> UpdateRole(
        Guid roleId, UpdateRoleRequest req, AppDbContext db, IInstancePermissions rights,
        PermissionCache cache, IAuditLogger audit)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null) return Results.NotFound();
        if (role.BuiltIn)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["The built-in roles cannot be renamed."],
            });
        if (await Refused(role.Tier, rights) is { } refusal) return refusal;

        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 60)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A name of 1 to 60 characters is required."],
            });
        if (await db.Roles.AnyAsync(r => r.Id != roleId && r.Name.ToLower() == name.ToLower()))
            return Results.Conflict(new { message = $"A role called '{name}' already exists." });

        var before = role.Name;
        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        audit.Record("role.updated", "role", role.Id, new { From = before, To = role.Name });
        await db.SaveChangesAsync();
        cache.Invalidate();
        return Results.Ok(new { role.Id, role.Name, role.Description });
    }

    /// <summary>
    /// Removes a custom role nobody holds. Reassigning first is deliberate:
    /// silently moving people to another role would change what they may do
    /// without anyone deciding it.
    /// </summary>
    private static async Task<IResult> DeleteRole(
        Guid roleId, AppDbContext db, IInstancePermissions rights, PermissionCache cache,
        IAuditLogger audit, HttpContext http, IConfiguration config)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null) return Results.NotFound();
        if (role.BuiltIn)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roleId"] = ["The built-in roles cannot be deleted."],
            });
        if (await Refused(role.Tier, rights) is { } refusal) return refusal;
        if (AuthEndpointsSudo(http, config) is { } denied) return denied;

        var members = await db.Users.CountAsync(u => u.RoleId == roleId);
        if (members > 0)
            return Results.Conflict(new
            {
                message = $"{members} {(members == 1 ? "account holds" : "accounts hold")} this role. "
                    + "Move them to another role first.",
            });

        db.Roles.Remove(role);
        audit.Record("role.deleted", "role", role.Id, new { role.Name, Tier = role.Tier.ToString() });
        await db.SaveChangesAsync();
        cache.Invalidate();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdatePermissions(
        Guid roleId, UpdatePermissionsRequest req, AppDbContext db, IInstancePermissions rights,
        PermissionCache cache, CurrentUser current, IAuditLogger audit, ISecurityDetector detector,
        ISiteSettingsService settings, HttpContext http, IConfiguration config)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null) return Results.NotFound();
        if (!MayReachMatrix(await rights.ForCurrentUserAsync())) return Results.Forbid();
        if (await Refused(role, rights) is { } refusal) return refusal;
        // Widening a role is how an attacker with one session would take an
        // instance short of ownership (dev-plan 3.5).
        if (AuthEndpointsSudo(http, config) is { } denied) return denied;

        // Unknown and reserved keys are ignored rather than refused: a client
        // echoing back a catalogue it half understands should not fail.
        var wanted = req.Permissions.Where(InstancePermissions.IsAssignable).ToHashSet();
        var current_ = role.Permissions.Select(p => p.Key).ToHashSet();
        var added = wanted.Except(current_).Order().ToArray();
        var removed = current_.Except(wanted).Order().ToArray();
        if (added.Length == 0 && removed.Length == 0)
            return Results.Ok(new { role.Id, Permissions = wanted.Order().ToArray() });

        role.Permissions.RemoveAll(p => !wanted.Contains(p.Key));
        foreach (var key in added) role.Permissions.Add(new RolePermission { RoleId = role.Id, Key = key });

        audit.Record("permissions.changed", "role", role.Id, new { role.Name, Added = added, Removed = removed });
        if (added.Length > 0) await AlertOnWidening(role, added, detector, current.RequireId());

        await db.SaveChangesAsync();
        cache.Invalidate();
        await MarkReviewedAsync(settings, current.RequireId());

        return Results.Ok(new { role.Id, Permissions = wanted.Order().ToArray() });
    }

    private static async Task<IResult> ResetPermissions(
        Guid roleId, AppDbContext db, IInstancePermissions rights, PermissionCache cache,
        CurrentUser current, IAuditLogger audit, ISecurityDetector detector,
        ISiteSettingsService settings, HttpContext http, IConfiguration config)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null) return Results.NotFound();
        return await UpdatePermissions(
            roleId, new UpdatePermissionsRequest([.. InstancePermissions.DefaultsFor(role.Tier)]),
            db, rights, cache, current, audit, detector, settings, http, config);
    }

    /// <summary>
    /// Records that someone has looked at the defaults and is content with
    /// them. The setup wizard's "Keep these defaults" calls this; saving a
    /// change records it too.
    /// </summary>
    private static async Task<IResult> MarkReviewed(
        ISiteSettingsService settings, CurrentUser current, IInstancePermissions rights)
    {
        // Only someone who could change the matrix can vouch for it.
        if (!await rights.HasAsync(InstancePermissions.PermissionsEditUserTier)
            && !await rights.HasAsync(InstancePermissions.PermissionsEditAdminTier))
            return Results.Forbid();

        await MarkReviewedAsync(settings, current.RequireId());
        return Results.NoContent();
    }

    private static Task MarkReviewedAsync(ISiteSettingsService settings, Guid actorId) =>
        settings.UpdateAsync(s =>
        {
            s.PermissionsReviewedAt = DateTimeOffset.UtcNow;
            s.PermissionsReviewedById = actorId;
        }, actorId);

    /// <summary>
    /// User-tier rows need <c>permissions.edit_user_tier</c>; administrator
    /// and owner rows need the reserved <c>permissions.edit_admin_tier</c>,
    /// which only the owner ever holds.
    /// </summary>
    private static bool MayEdit(Role role, IReadOnlySet<string> held) =>
        role.Tier >= UserRole.Admin
            ? held.Contains(InstancePermissions.PermissionsEditAdminTier)
            : held.Contains(InstancePermissions.PermissionsEditUserTier);

    private static async Task<IResult?> Refused(UserRole tier, IInstancePermissions rights) =>
        (tier >= UserRole.Admin
            ? (await rights.ForCurrentUserAsync()).Contains(InstancePermissions.PermissionsEditAdminTier)
            : (await rights.ForCurrentUserAsync()).Contains(InstancePermissions.PermissionsEditUserTier))
            ? null
            : Results.Json(new
            {
                title = "Forbidden",
                status = 403,
                code = "permission_required",
                message = tier >= UserRole.Admin
                    ? "Only the owner shapes administrator roles."
                    : "You do not have the right to edit roles.",
            }, statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult?> Refused(Role role, IInstancePermissions rights) =>
        MayEdit(role, await rights.ForCurrentUserAsync())
            ? null
            : Results.Json(new
            {
                title = "Forbidden",
                status = 403,
                code = "permission_required",
                message = role.Tier >= UserRole.Admin
                    ? "Only the owner changes what administrators may do."
                    : "You do not have the right to edit roles.",
            }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// A role gaining rights is worth telling everyone about: an
    /// administrator tier gaining anything is critical, and a user-tier role
    /// gaining an administration right is how a quiet privilege escalation
    /// would look.
    /// </summary>
    private static async Task AlertOnWidening(
        Role role, string[] added, ISecurityDetector detector, Guid actorId)
    {
        var administrative = added
            .Where(k => InstancePermissions.All.First(p => p.Key == k).Scope == PermissionScope.Administration)
            .ToArray();

        if (role.Tier >= UserRole.Admin)
            await detector.PermissionsExpandedAsync(actorId, SecuritySeverity.Critical,
                new { Role = role.Name, Tier = role.Tier.ToString(), Added = added });
        else if (administrative.Length > 0)
            await detector.PermissionsExpandedAsync(actorId, SecuritySeverity.Warning,
                new { Role = role.Name, Tier = role.Tier.ToString(), Added = administrative });
    }

    private static IResult? AuthEndpointsSudo(HttpContext http, IConfiguration config) =>
        Auth.AuthEndpoints.RequireSudo(http, config);

    private static PermissionDto ToDto(InstancePermission p) =>
        new(p.Key, p.Area, p.Label, p.Description, p.Scope.ToString());
}
