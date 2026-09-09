using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Media;

/// <summary>
/// Serves and replaces profile media (dev-plan 0.4). Avatars today; space icons
/// join in Phase 6, which owns the <c>Space</c> columns — the storage service
/// already has that namespace, so only a column and a route are missing.
/// </summary>
public static class MediaEndpoints
{
    public record AvatarResponse(string AvatarHash);
    public record AvatarVariantRequest(int? Variant);

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/media").WithTags("Media").RequireAuthorization();

        group.MapGet("/avatars/{userId:guid}", GetAvatar);
        // Framework form-token check off; the CsrfHeaderMiddleware covers this (dev-plan 3.4).
        group.MapPut("/avatars/me", UploadOwnAvatar).DisableAntiforgery();
        group.MapDelete("/avatars/me", DeleteOwnAvatar);
        group.MapPut("/avatars/me/variant", SetOwnAvatarVariant);

        return routes;
    }

    /// <summary>
    /// An avatar is readable by any signed-in user: it is shown next to every
    /// comment and page version, so gating it per-viewer would gate nothing
    /// while costing a permission check on each render. Page attachments remain
    /// permission-checked, which is the case that actually matters.
    ///
    /// Served with a long max-age because the URL carries a content hash
    /// (<c>?v=</c>) — a new upload produces a new URL, so a stale cache is not
    /// possible and revalidation is wasted work. <c>immutable</c> says exactly
    /// that to the browser.
    /// </summary>
    private static async Task<IResult> GetAvatar(Guid userId, AppDbContext db, IProfileMediaService media)
    {
        var key = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AvatarKey)
            .FirstOrDefaultAsync();

        // 404 for "no avatar" as well as "no such user": the client falls back
        // to the generated default either way, and this does not become a probe
        // for which user ids exist.
        if (string.IsNullOrEmpty(key)) return Results.NotFound();

        var stream = media.OpenRead(key);
        if (stream is null) return Results.NotFound();

        return Results.Stream(stream, "image/webp", enableRangeProcessing: false);
    }

    private static async Task<IResult> UploadOwnAvatar(
        IFormFile? file, AppDbContext db, CurrentUser current, IProfileMediaService media)
    {
        if (file is null || file.Length == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["An image file is required."],
            });

        var userId = current.RequireId();

        StoredMedia stored;
        try
        {
            await using var source = file.OpenReadStream();
            stored = await media.StoreAsync(ProfileMediaKind.Avatar, userId, source);
        }
        catch (ProfileMediaException ex)
        {
            // The service's messages are written to be shown to the uploader.
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [ex.Message],
            });
        }

        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.AvatarKey, stored.StorageKey)
            .SetProperty(x => x.AvatarHash, stored.ContentHash));

        return Results.Ok(new AvatarResponse(stored.ContentHash));
    }

    private static async Task<IResult> DeleteOwnAvatar(
        AppDbContext db, CurrentUser current, IProfileMediaService media)
    {
        var userId = current.RequireId();
        var key = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.AvatarKey).FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(key)) media.Delete(key);

        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.AvatarKey, (string?)null)
            .SetProperty(x => x.AvatarHash, (string?)null));

        return Results.NoContent();
    }

    /// <summary>
    /// Picks one of the generated avatars, or clears the choice (null) to fall
    /// back to the one derived from the user's id.
    ///
    /// The upper bound lives in the client (AVATAR_COLORS) and is validated
    /// here only as a sanity bound: the set can grow without a server change,
    /// and an index past the end degrades to the derived avatar rather than
    /// rendering nothing.
    /// </summary>
    private static async Task<IResult> SetOwnAvatarVariant(
        AvatarVariantRequest req, AppDbContext db, CurrentUser current)
    {
        if (req.Variant is { } v && (v < 0 || v > 63))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["variant"] = ["Not a valid avatar."],
            });

        var userId = current.RequireId();
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.AvatarVariant, req.Variant));

        return Results.NoContent();
    }
}
