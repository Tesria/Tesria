using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>
/// Resolves what the current user may do (PLAN §4). Two independent layers:
/// <list type="bullet">
/// <item><b>Space permissions</b> (dev-plan 21.1): a signed-in, active account
/// gets the space's <see cref="Space.EveryoneAccess"/>, members of Global
/// Viewers and Global Reviewers get View on every space, and anything more
/// needs a grant, to the person or to a group they are in (a space's own
/// four groups hold ordinary grants). Admin implies Edit implies View.
/// Before 21.1 a space with no grants was open to everyone; the seed turned
/// that into an EveryoneAccess of Admin.</item>
/// <item><b>Page restrictions</b>: a page is restricted if it or any ancestor
/// carries a restriction; the user must match one. A View restriction also
/// gates editing. Space admins bypass page restrictions, but only explicit
/// ones: the implicit levels (EveryoneAccess, the global groups) never
/// do.</item>
/// </list>
/// </summary>
public interface IPermissionService
{
    Task<bool> CanViewSpaceAsync(Guid spaceId);
    Task<bool> CanEditSpaceAsync(Guid spaceId);
    Task<bool> CanAdminSpaceAsync(Guid spaceId);

    /// <summary>
    /// Whether the caller holds a real Admin grant on the space, directly or
    /// through a group (its Admins group included): unlike
    /// <see cref="CanAdminSpaceAsync"/>, an EveryoneAccess of Admin does not
    /// count. This is who passes page restrictions, and who may change who
    /// administers the space (dev-plan 21.1).
    /// </summary>
    Task<bool> IsExplicitSpaceAdminAsync(Guid spaceId);
    Task<bool> CanViewPageAsync(Guid pageId);
    Task<bool> CanEditPageAsync(Guid pageId);

    /// <summary>
    /// Whether the caller may read what hangs off a page: its versions,
    /// attachments, labels and comments (dev-plan 14.1). Stricter than
    /// <see cref="CanViewPageAsync"/>, which deliberately resolves drafts and
    /// trashed pages for the trash, restore and draft-upload paths: a trashed
    /// page reads as not found, the way the page itself does, and a draft is
    /// readable by its author and by those who may edit its space, the same
    /// people who may publish or discard it.
    /// </summary>
    Task<bool> CanReadPageAsync(Guid pageId);
    /// <summary>Ids of spaces the current user may view, for filtering listings.</summary>
    Task<HashSet<Guid>> ViewableSpaceIdsAsync();

    /// <summary>
    /// Whether a page is readable with no account at all: evaluated as the
    /// anonymous principal regardless of who is asking (dev-plan 5.1). Used
    /// where the answer must not depend on the caller: link previews, the
    /// sitemap.
    /// </summary>
    Task<bool> IsPubliclyViewablePageAsync(Guid pageId);
    Task<bool> IsPubliclyViewableSpaceAsync(Guid spaceId);

    /// <summary>
    /// The same rules, evaluated as a *different* user than the one making
    /// the request. Needed wherever the app acts on someone else's behalf:
    /// today, deciding whether a mentioned user may be told about the page
    /// they were mentioned on (dev-plan Phase 7 Wave C), since a notification
    /// carries the page title and must not leak a restricted one.
    ///
    /// Returns a fresh instance: the principal cache is per-user, so reusing
    /// this one would answer for the wrong person.
    /// </summary>
    IPermissionService AsUser(Guid userId);

    /// <summary>
    /// The same rules evaluated as a reader with no account at all. Used by
    /// the site export (dev-plan 12.2), whose default audience is the public:
    /// deciding which pages go into a site by the exporter's own access is
    /// how a private page ends up on the internet.
    /// </summary>
    IPermissionService AsAnonymous();
}

public sealed class PermissionService(AppDbContext db, CurrentUser current, ISiteSettingsService settings)
    : IPermissionService
{
    /// <summary>
    /// Set only by <see cref="AsUser"/>. Every rule below reads
    /// <see cref="UserId"/>, never the request's identity directly, so there
    /// is exactly one place the identity comes from and an "as user"
    /// evaluation cannot fall back to the caller's own rights.
    /// </summary>
    private Guid? _asUserId;

    /// <summary>Set by <see cref="AsAnonymous"/>: evaluate as nobody, whoever is calling.</summary>
    private bool _asAnonymous;

    private Guid? UserId => _asAnonymous ? null : _asUserId ?? current.Id;

    public IPermissionService AsUser(Guid userId) =>
        new PermissionService(db, current, settings) { _asUserId = userId };

    public IPermissionService AsAnonymous() =>
        new PermissionService(db, current, settings) { _asAnonymous = true };

    // -- the anonymous principal (dev-plan 5.1) --------------------------------
    //
    // No session, no token: exactly one capability, reading a public space's
    // unrestricted, current pages, and only while the instance switch is on.
    // See architecture.md, "Public read mode".

    private bool? _publicSpacesAllowed;

    private async Task<bool> PublicSpacesAllowedAsync() =>
        _publicSpacesAllowed ??= (await settings.GetAsync()).AllowPublicSpaces;

    public async Task<bool> IsPubliclyViewableSpaceAsync(Guid spaceId)
    {
        if (!await PublicSpacesAllowedAsync()) return false;
        return await db.Spaces.AsNoTracking().AnyAsync(s => s.Id == spaceId && s.IsPublic && !s.Archived);
    }

    public async Task<bool> IsPubliclyViewablePageAsync(Guid pageId)
    {
        // The soft-delete filter stays on: a trashed page is never public.
        var page = await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId)
            .Select(p => new { p.SpaceId, p.Status })
            .FirstOrDefaultAsync();
        if (page is null || page.Status != PageStatus.Current) return false;
        if (!await IsPubliclyViewableSpaceAsync(page.SpaceId)) return false;

        // Any restriction, of any kind, anywhere in the ancestry hides the
        // page from the world. A signed-in user is only hidden from by View
        // restrictions; "everyone" now includes the internet.
        var ancestry = await AncestryAsync(pageId, page.SpaceId);
        return !await db.PageRestrictions.AsNoTracking().AnyAsync(r => ancestry.Contains(r.PageId));
    }

    // The user's own id plus every group they belong to; permissions may be
    // granted to any of these. Cached for the lifetime of the request.
    private HashSet<Guid>? _principals;

    private async Task<HashSet<Guid>> PrincipalsAsync()
    {
        if (_principals is not null) return _principals;
        var userId = UserId;
        if (userId is null) return _principals = [];

        var groupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();
        // The computed groups follow the account's tier (dev-plan 15.1); an
        // account that is not active is in none of them.
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => new { u.Role, u.Status }).FirstOrDefaultAsync();
        _active = account is { Status: UserStatus.Active };
        if (_active) groupIds.AddRange(BuiltInGroups.For(account!.Role));

        // Global Viewers and Global Reviewers see every space (dev-plan 21.1),
        // as an implicit level like EveryoneAccess, and so only while active.
        _globalViewer = _active && groupIds.Any(BuiltInGroups.IsGlobal);

        _principals = [userId.Value, .. groupIds];
        return _principals;
    }

    // Both set by PrincipalsAsync, for the same account and request.
    private bool _active;
    private bool _globalViewer;

    // -- spaces ---------------------------------------------------------------

    public Task<bool> CanViewSpaceAsync(Guid spaceId) => HasSpaceAsync(spaceId, SpaceOperation.View);
    public Task<bool> CanEditSpaceAsync(Guid spaceId) => HasSpaceAsync(spaceId, SpaceOperation.Edit);
    public Task<bool> CanAdminSpaceAsync(Guid spaceId) => HasSpaceAsync(spaceId, SpaceOperation.Admin);

    private async Task<bool> HasSpaceAsync(Guid spaceId, SpaceOperation required)
    {
        if (UserId is null)
            return required == SpaceOperation.View && await IsPubliclyViewableSpaceAsync(spaceId);

        var space = await db.Spaces.AsNoTracking()
            .Where(s => s.Id == spaceId).Select(s => new { s.EveryoneAccess }).FirstOrDefaultAsync();
        if (space is null) return false;

        var principals = await PrincipalsAsync();

        // The implicit levels (dev-plan 21.1). Only for an account that is
        // active: one that is suspended is not "signed in", even for a check
        // made on its behalf, such as whether to tell it about a mention.
        if (_active && space.EveryoneAccess is { } everyone && everyone >= required) return true;
        if (_globalViewer && required == SpaceOperation.View) return true;

        // Higher operations imply lower ones, so compare by rank.
        return await db.SpacePermissions.AsNoTracking()
            .AnyAsync(p => p.SpaceId == spaceId && p.Operation >= required && principals.Contains(p.PrincipalId));
    }

    public Task<bool> IsExplicitSpaceAdminAsync(Guid spaceId) => HasExplicitSpaceAdminAsync(spaceId);

    /// <summary>
    /// True only when the user holds a real Admin grant on the space: unlike
    /// <see cref="CanAdminSpaceAsync"/>, an EveryoneAccess of Admin does not
    /// make everyone an admin here.
    /// </summary>
    private async Task<bool> HasExplicitSpaceAdminAsync(Guid spaceId)
    {
        if (UserId is null) return false;
        var principals = await PrincipalsAsync();
        return await db.SpacePermissions.AsNoTracking()
            .AnyAsync(p => p.SpaceId == spaceId
                           && p.Operation == SpaceOperation.Admin
                           && principals.Contains(p.PrincipalId));
    }

    public async Task<HashSet<Guid>> ViewableSpaceIdsAsync()
    {
        if (UserId is null)
        {
            if (!await PublicSpacesAllowedAsync()) return [];
            return (await db.Spaces.AsNoTracking().Where(s => s.IsPublic && !s.Archived).Select(s => s.Id).ToListAsync()).ToHashSet();
        }

        var principals = await PrincipalsAsync();
        // Every space, archived ones included (21.1).
        if (_globalViewer) return (await db.Spaces.AsNoTracking().Select(s => s.Id).ToListAsync()).ToHashSet();

        var granted = await db.SpacePermissions.AsNoTracking()
            .Where(p => principals.Contains(p.PrincipalId))
            .Select(p => p.SpaceId)
            .ToListAsync();
        var result = granted.ToHashSet();
        if (_active)
            result.UnionWith(await db.Spaces.AsNoTracking()
                .Where(s => s.EveryoneAccess != null).Select(s => s.Id).ToListAsync());
        return result;
    }

    // -- pages ----------------------------------------------------------------

    public Task<bool> CanViewPageAsync(Guid pageId) => HasPageAsync(pageId, PageOperation.View);
    public Task<bool> CanEditPageAsync(Guid pageId) => HasPageAsync(pageId, PageOperation.Edit);

    public async Task<bool> CanReadPageAsync(Guid pageId)
    {
        var page = await db.Pages.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.Id == pageId)
            .Select(p => new { p.SpaceId, p.Status, p.DeletedAt, p.CreatedById })
            .FirstOrDefaultAsync();
        if (page is null || page.DeletedAt is not null) return false;
        if (page.Status == Domain.PageStatus.Draft
            && page.CreatedById != UserId
            && !await CanEditSpaceAsync(page.SpaceId))
            return false;
        return await CanViewPageAsync(pageId);
    }

    private async Task<bool> HasPageAsync(Guid pageId, PageOperation required)
    {
        if (UserId is null)
            return required == PageOperation.View && await IsPubliclyViewablePageAsync(pageId);

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

        // Space admins are never blocked by page restrictions, but only ones
        // holding an *explicit* admin grant. In a space whose EveryoneAccess is
        // Admin everyone would otherwise count as an admin, which would make
        // page restrictions meaningless exactly where they are most used; and
        // Global Viewers are bound by them too (21.1).
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
