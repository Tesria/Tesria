using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// Instance-level operations, gated by the Admin role alone — they are about
/// the instance, not about any space's content.
///
/// Note what is deliberately absent: there is no endpoint here that returns
/// page content. Admins do not bypass space permissions or page restrictions
/// (see docs/architecture.md, "Roles and administrators"); to read a space
/// they hold no grant for, an admin uses <see cref="RecoverSpaceAccess"/>,
/// which is audited and leaves a revocable grant behind. A silent bypass
/// would let any admin read any team's private space with no trace.
/// </summary>
public static class AdminEndpoints
{
    public record RecoverAccessResponse(
        Guid SpaceId, string Key, string Name, bool AlreadyHadAccess);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin").WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/spaces/{key}/recover-access", RecoverSpaceAccess);

        return routes;
    }

    /// <summary>
    /// Grants the calling admin an explicit <see cref="SpaceOperation.Admin"/>
    /// permission on a space, so they can administer (or recover) it.
    ///
    /// From that point the existing permission rules apply unchanged — including
    /// the one that already lets an explicit space admin past page restrictions —
    /// rather than adding an "unless admin" branch to every check. The grant is a
    /// normal row, so it can be revoked afterwards through the usual permissions
    /// endpoint, returning the admin to ordinary access.
    /// </summary>
    private static async Task<IResult> RecoverSpaceAccess(
        string key, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == normalizedKey);
        if (space is null) return Results.NotFound();

        var userId = current.RequireId();
        var alreadyHadAccess = await db.SpacePermissions.AnyAsync(p =>
            p.SpaceId == space.Id
            && p.PrincipalType == PrincipalType.User
            && p.PrincipalId == userId
            && p.Operation == SpaceOperation.Admin);

        // Idempotent: re-running it is a no-op rather than a duplicate grant and
        // a second audit entry, so a retried request doesn't pollute the log.
        if (alreadyHadAccess)
            return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, true));

        db.SpacePermissions.Add(new SpacePermission
        {
            Id = Guid.NewGuid(),
            SpaceId = space.Id,
            PrincipalType = PrincipalType.User,
            PrincipalId = userId,
            Operation = SpaceOperation.Admin,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        audit.Record("space.access_recovered", "space", space.Id,
            new { space.Key, space.Name });
        await db.SaveChangesAsync();

        return Results.Ok(new RecoverAccessResponse(space.Id, space.Key, space.Name, false));
    }
}
