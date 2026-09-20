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

    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/roles").WithTags("Admin").RequireAuthorization();

        // Not RequirePermission(permissions.view): that right is assignable,
        // and an owner who cleared their own row would have no way back to the
        // matrix to undo it. Reaching this tab is "may see it, or may edit any
        // row", and the owner's edit right is reserved, so their way back is
        // never closed. Each handler still checks the row it touches.
        group.MapGet("", GetMatrix);
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
