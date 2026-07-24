using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Infrastructure.Permissions;

/// <summary>
/// Resolves what the current user may do (PLAN §4). Two independent layers:
/// <list type="bullet">
/// <item><b>Space permissions</b> — default-open: a space with no permission
/// rows is open to all authenticated users; once any row exists a matching
/// grant is required. Admin implies Edit implies View.</item>
/// <item><b>Page restrictions</b> — a page is restricted if it or any ancestor
/// carries a restriction; the user must match one. A View restriction also
/// gates editing. Space admins bypass page restrictions.</item>
/// </list>
/// </summary>
public interface IPermissionService
{
    Task<bool> CanViewSpaceAsync(Guid spaceId);
    Task<bool> CanEditSpaceAsync(Guid spaceId);
    Task<bool> CanAdminSpaceAsync(Guid spaceId);
    Task<bool> CanViewPageAsync(Guid pageId);
    Task<bool> CanEditPageAsync(Guid pageId);
    /// <summary>Ids of spaces the current user may view — for filtering listings.</summary>
    Task<HashSet<Guid>> ViewableSpaceIdsAsync();
}

public sealed class PermissionService(AppDbContext db, CurrentUser current) : IPermissionService
{
    // The user's own id plus every group they belong to; permissions may be
    // granted to any of these. Cached for the lifetime of the request.
    private HashSet<Guid>? _principals;

    private async Task<HashSet<Guid>> PrincipalsAsync()
    {
        if (_principals is not null) return _principals;
        var userId = current.Id;
        if (userId is null) return _principals = [];

        var groupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        _principals = [userId.Value, .. groupIds];
        return _principals;
    }

    // -- spaces ---------------------------------------------------------------

    public Task<bool> CanViewSpaceAsync(Guid spaceId) => HasSpaceAsync(spaceId, SpaceOperation.View);
    public Task<bool> CanEditSpaceAsync(Guid spaceId) => HasSpaceAsync(spaceId, SpaceOperation.Edit);
    public Task<bool> CanAdminSpaceAsync(Guid spaceId) => HasSpaceAsync(spaceId, SpaceOperation.Admin);

    private async Task<bool> HasSpaceAsync(Guid spaceId, SpaceOperation required)
    {
        if (current.Id is null) return false;

        var grants = await db.SpacePermissions.AsNoTracking()
            .Where(p => p.SpaceId == spaceId)
            .Select(p => new { p.PrincipalId, p.Operation })
            .ToListAsync();

        // Default-open: an unconfigured space is accessible to any signed-in user.
        if (grants.Count == 0) return true;

        var principals = await PrincipalsAsync();
        // Higher operations imply lower ones, so compare by rank.
        return grants.Any(g => principals.Contains(g.PrincipalId) && g.Operation >= required);
    }

    /// <summary>
    /// True only when the user holds a real Admin grant on the space — unlike
    /// <see cref="CanAdminSpaceAsync"/>, this does not treat an unconfigured
    /// (default-open) space as granting admin to everyone.
    /// </summary>
    private async Task<bool> HasExplicitSpaceAdminAsync(Guid spaceId)
    {
        if (current.Id is null) return false;
        var principals = await PrincipalsAsync();
        return await db.SpacePermissions.AsNoTracking()
            .AnyAsync(p => p.SpaceId == spaceId
                           && p.Operation == SpaceOperation.Admin
                           && principals.Contains(p.PrincipalId));
    }

    public async Task<HashSet<Guid>> ViewableSpaceIdsAsync()
    {
        var allIds = await db.Spaces.AsNoTracking().Select(s => s.Id).ToListAsync();
        if (current.Id is null) return [];

        var grants = await db.SpacePermissions.AsNoTracking()
            .Select(p => new { p.SpaceId, p.PrincipalId, p.Operation })
            .ToListAsync();

        var principals = await PrincipalsAsync();
        var configured = grants.Select(g => g.SpaceId).ToHashSet();

        return allIds.Where(id =>
            !configured.Contains(id) // default-open
            || grants.Any(g => g.SpaceId == id
                               && principals.Contains(g.PrincipalId)
                               && g.Operation >= SpaceOperation.View))
            .ToHashSet();
    }

    // -- pages ----------------------------------------------------------------

    public Task<bool> CanViewPageAsync(Guid pageId) => HasPageAsync(pageId, PageOperation.View);
    public Task<bool> CanEditPageAsync(Guid pageId) => HasPageAsync(pageId, PageOperation.Edit);

    private async Task<bool> HasPageAsync(Guid pageId, PageOperation required)
    {
        if (current.Id is null) return false;

        // Ignore the soft-delete filter so trash/restore checks still resolve.
        var page = await db.Pages.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.Id == pageId)
            .Select(p => new { p.Id, p.SpaceId })
            .FirstOrDefaultAsync();
        if (page is null) return false;

        // Space access is the floor: Edit on a page needs Edit on the space.
        var spaceOk = required == PageOperation.Edit
            ? await CanEditSpaceAsync(page.SpaceId)
            : await CanViewSpaceAsync(page.SpaceId);
        if (!spaceOk) return false;

        // Space admins are never blocked by page restrictions — but only ones
        // holding an *explicit* admin grant. In a default-open space everyone
        // would otherwise count as an admin, which would make page restrictions
        // meaningless exactly where they are most used.
        if (await HasExplicitSpaceAdminAsync(page.SpaceId)) return true;

        var ancestry = await AncestryAsync(pageId, page.SpaceId);
        var restrictions = await db.PageRestrictions.AsNoTracking()
            .Where(r => ancestry.Contains(r.PageId))
            .Select(r => new { r.PrincipalId, r.Operation })
            .ToListAsync();
        if (restrictions.Count == 0) return true;

        var principals = await PrincipalsAsync();

        // A View restriction anywhere in the ancestry gates everything.
        var viewRules = restrictions.Where(r => r.Operation == PageOperation.View).ToList();
        if (viewRules.Count > 0 && !viewRules.Any(r => principals.Contains(r.PrincipalId)))
            return false;

        if (required == PageOperation.Edit)
        {
            var editRules = restrictions.Where(r => r.Operation == PageOperation.Edit).ToList();
            if (editRules.Count > 0 && !editRules.Any(r => principals.Contains(r.PrincipalId)))
                return false;
        }

        return true;
    }

    /// <summary>The page's id plus all of its ancestors', for inherited restrictions.</summary>
    private async Task<List<Guid>> AncestryAsync(Guid pageId, Guid spaceId)
    {
        var pages = await db.Pages.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.SpaceId == spaceId)
            .Select(p => new { p.Id, p.ParentPageId })
            .ToListAsync();
        var parentOf = pages.ToDictionary(p => p.Id, p => p.ParentPageId);

        var chain = new List<Guid>();
        Guid? cursor = pageId;
        // Hop limit guards against a cycle in corrupt data.
        for (var hops = 0; cursor is { } id && hops < 10_000; hops++)
        {
            chain.Add(id);
            cursor = parentOf.TryGetValue(id, out var parent) ? parent : null;
        }
        return chain;
    }
}
