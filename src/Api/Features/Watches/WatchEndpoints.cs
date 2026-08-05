using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Watches;

public static class WatchEndpoints
{
    public record WatchStatusResponse(bool Watching);

    public static IEndpointRouteBuilder MapWatchEndpoints(this IEndpointRouteBuilder routes)
    {
        var page = routes.MapGroup("/pages/{pageId:guid}/watch")
            .WithTags("Watches").RequireAuthorization();
        page.MapGet("/", (Guid pageId, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            GetStatus(db, current, perms.CanViewPageAsync(pageId), "page", pageId));
        page.MapPost("/", (Guid pageId, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            Add(db, current, perms.CanViewPageAsync(pageId), "page", pageId));
        page.MapDelete("/", (Guid pageId, AppDbContext db, CurrentUser current) =>
            Remove(db, current, "page", pageId));

        var space = routes.MapGroup("/spaces/{key}/watch")
            .WithTags("Watches").RequireAuthorization();
        space.MapGet("/", (string key, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            GetSpaceStatus(key, db, current, perms));
        space.MapPost("/", (string key, AppDbContext db, CurrentUser current, IPermissionService perms) =>
            AddSpace(key, db, current, perms));
        space.MapDelete("/", (string key, AppDbContext db, CurrentUser current) =>
            RemoveSpace(key, db, current));

        return routes;
    }

    private static async Task<IResult> GetStatus(
        AppDbContext db, CurrentUser current, Task<bool> canView, string targetType, Guid targetId)
    {
        if (!await canView) return Results.NotFound();
        var watching = await db.Watches.AsNoTracking().AnyAsync(w =>
            w.UserId == current.RequireId() && w.TargetType == targetType && w.TargetId == targetId);
        return Results.Ok(new WatchStatusResponse(watching));
    }

    private static async Task<IResult> Add(
        AppDbContext db, CurrentUser current, Task<bool> canView, string targetType, Guid targetId)
    {
        if (!await canView) return Results.NotFound();
        var userId = current.RequireId();
        var exists = await db.Watches.AnyAsync(w =>
            w.UserId == userId && w.TargetType == targetType && w.TargetId == targetId);
        if (!exists)
        {
            db.Watches.Add(new Watch
            {
                UserId = userId,
                TargetType = targetType,
                TargetId = targetId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> Remove(
        AppDbContext db, CurrentUser current, string targetType, Guid targetId)
    {
        var userId = current.RequireId();
        var watch = await db.Watches.FirstOrDefaultAsync(w =>
            w.UserId == userId && w.TargetType == targetType && w.TargetId == targetId);
        if (watch is not null)
        {
            db.Watches.Remove(watch);
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    // Spaces are keyed by their short `key` in the URL, so these resolve to an
    // id before delegating to the shared page/space-agnostic helpers above.
    private static async Task<IResult> GetSpaceStatus(
        string key, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var space = await FindSpaceAsync(db, key);
        if (space is null) return Results.NotFound();
        return await GetStatus(db, current, perms.CanViewSpaceAsync(space.Id), "space", space.Id);
    }

    private static async Task<IResult> AddSpace(
        string key, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var space = await FindSpaceAsync(db, key);
        if (space is null) return Results.NotFound();
        return await Add(db, current, perms.CanViewSpaceAsync(space.Id), "space", space.Id);
    }

    private static async Task<IResult> RemoveSpace(string key, AppDbContext db, CurrentUser current)
    {
        var space = await FindSpaceAsync(db, key);
        if (space is null) return Results.NotFound();
        return await Remove(db, current, "space", space.Id);
    }

    private static Task<Space?> FindSpaceAsync(AppDbContext db, string key)
    {
        var normalized = (key ?? "").ToUpperInvariant();
        return db.Spaces.FirstOrDefaultAsync(s => s.Key == normalized);
    }
}
