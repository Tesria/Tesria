using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Permissions;

/// <summary>
/// A space's restricted pages, for its administrators (dev-plan 21.6). Page
/// restrictions now bind a space's administrators like anyone, which is
/// Confluence's rule: they may not read a restricted page, but they may see
/// that it exists and lift its restrictions. Lifting is visible by design,
/// so it cannot be used to read quietly: it is audited, and the page's
/// author is told.
/// <para>
/// Only an explicit administrator (in the space's Admins group, or given
/// Admin there): in a space everyone may administer, everyone is an implicit
/// one, and letting them lift restrictions would make restrictions
/// meaningless exactly where they are most used.
/// </para>
/// </summary>
public static class RestrictedPages
{
    public record RestrictionRef(PageOperation Operation, PrincipalType PrincipalType, string PrincipalName);

    /// <param name="PagesUnder">Pages below it, which its restrictions reach too.</param>
    /// <param name="YouCanRead">Whether the caller is named, so can read it anyway.</param>
    public record RestrictedPage(
        Guid Id, string Title, string? CreatedByName, bool Draft, bool YouCanRead,
        int PagesUnder, List<RestrictionRef> Restrictions);

    public static IEndpointRouteBuilder MapRestrictedPageEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/spaces/{key}/restricted-pages", List)
            .WithTags("Permissions").RequireAuthorization()
            .Produces<List<RestrictedPage>>();
        routes.MapPost("/pages/{pageId:guid}/restrictions/lift", Lift)
            .WithTags("Permissions").RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);
        return routes;
    }

    private static IResult NotExplicitAdmin() => Results.Json(new
    {
        message = "Only the space's own administrators (the people in its Admins group, or given Admin there) can see its restricted pages and lift their restrictions.",
    }, statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult> List(string key, AppDbContext db, IPermissionService perms)
    {
        var normalized = (key ?? "").ToUpperInvariant();
        var spaceId = await db.Spaces.AsNoTracking().Where(s => s.Key == normalized).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
        if (spaceId is not { } id || !await perms.CanViewSpaceAsync(id)) return Results.NotFound();
        if (!await perms.IsExplicitSpaceAdminAsync(id)) return NotExplicitAdmin();

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == id)
            .Select(p => new { p.Id, p.ParentPageId, p.Title, p.Status, CreatedByName = p.CreatedBy != null ? p.CreatedBy.DisplayName : null })
            .ToListAsync();
        var pageIds = pages.Select(p => p.Id).ToList();
        var rows = await db.PageRestrictions.AsNoTracking()
            .Where(r => pageIds.Contains(r.PageId))
            .Select(r => new { r.PageId, r.Operation, r.PrincipalType, r.PrincipalId })
            .ToListAsync();
        if (rows.Count == 0) return Results.Ok(new List<RestrictedPage>());

        var groupNames = await SpaceGroups.NamesAsync(db, rows.Where(r => r.PrincipalType == PrincipalType.Group).Select(r => r.PrincipalId));
        var userIds = rows.Where(r => r.PrincipalType == PrincipalType.User).Select(r => r.PrincipalId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var children = pages.Where(p => p.ParentPageId != null).ToLookup(p => p.ParentPageId!.Value, p => p.Id);
        int Under(Guid pageId)
        {
            var count = 0;
            var stack = new Stack<Guid>(children[pageId]);
            while (stack.Count > 0 && count < 100_000)
            {
                var next = stack.Pop();
                count++;
                foreach (var c in children[next]) stack.Push(c);
            }
            return count;
        }

        var result = new List<RestrictedPage>();
        foreach (var group in rows.GroupBy(r => r.PageId))
        {
            var page = pages.First(p => p.Id == group.Key);
            result.Add(new RestrictedPage(
                page.Id, page.Title, page.CreatedByName, page.Status == PageStatus.Draft,
                await perms.CanViewPageAsync(page.Id), Under(page.Id),
                group.OrderBy(r => r.Operation)
                    .Select(r => new RestrictionRef(r.Operation, r.PrincipalType,
                        (r.PrincipalType == PrincipalType.Group ? groupNames.GetValueOrDefault(r.PrincipalId) : userNames.GetValueOrDefault(r.PrincipalId))
                            ?? "Someone removed"))
                    .ToList()));
        }
        return Results.Ok(result.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Removes every restriction a page carries itself (not those it
    /// inherits from a page above, which are lifted there). Audited with what
    /// was removed, so it can be put back by hand; the author is told unless
    /// they did it.
    /// </summary>
    private static async Task<IResult> Lift(
        Guid pageId, AppDbContext db, IPermissionService perms, IAuditLogger audit,
        INotificationService notifications, CurrentUser current)
    {
        var page = await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId)
            .Select(p => new { p.Id, p.SpaceId, p.Title, p.CreatedById })
            .FirstOrDefaultAsync();
        if (page is null || !await perms.CanViewSpaceAsync(page.SpaceId)) return Results.NotFound();
        if (!await perms.IsExplicitSpaceAdminAsync(page.SpaceId)) return NotExplicitAdmin();

        var rows = await db.PageRestrictions.Where(r => r.PageId == pageId).ToListAsync();
        if (rows.Count == 0) return Results.NoContent();

        db.PageRestrictions.RemoveRange(rows);
        audit.Record("page.restrictions_lifted", "page", pageId, new
        {
            page.Title,
            Removed = rows.Select(r => new { r.PrincipalType, r.PrincipalId, r.Operation }).ToList(),
        });
        await db.SaveChangesAsync();

        // After the save, so the check sees the page as it now is: an author
        // who has since lost the space hears nothing about it.
        var me = current.RequireId();
        var author = page.CreatedById;
        if (author != me && await perms.AsUser(author).CanViewPageAsync(pageId))
        {
            await notifications.NotifyUserAsync(author, "page.restrictions_lifted", "page", pageId, me, new { page.Title });
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }
}
