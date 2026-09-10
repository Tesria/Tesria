using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Media;

/// <summary>
/// Serves and replaces profile media (dev-plan 0.4): user avatars and space
/// icons (dev-plan 6). Both go through the same re-encoding pipeline — the
/// security control described on <see cref="ProfileMediaService"/> — and are
/// served from URLs carrying a content hash, so they cache indefinitely.
/// </summary>
public static class MediaEndpoints
{
    public record AvatarResponse(string AvatarHash);
    public record AvatarVariantRequest(int? Variant);
    public record SpaceIconResponse(string IconHash);

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/media").WithTags("Media").RequireAuthorization();

        group.MapGet("/avatars/{userId:guid}", GetAvatar);
        // Framework form-token check off; the CsrfHeaderMiddleware covers this (dev-plan 3.4).
        group.MapPut("/avatars/me", UploadOwnAvatar).DisableAntiforgery();
        group.MapDelete("/avatars/me", DeleteOwnAvatar);
        group.MapPut("/avatars/me/variant", SetOwnAvatarVariant);

        // Space icons (dev-plan 6). Addressed by space key, the way spaces are
        // addressed everywhere else; reading follows the space's own
        // visibility, so a public space's icon is readable by anyone (5.3).
        group.MapGet("/space-icons/{key}", GetSpaceIcon).AllowAnonymous();
        group.MapPut("/space-icons/{key}", UploadSpaceIcon).DisableAntiforgery();
        group.MapDelete("/space-icons/{key}", DeleteSpaceIcon);

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
    /// A space's uploaded icon. Unlike an avatar this is not readable by every
    /// signed-in user: a space nobody may see must not confirm its own
    /// existence through its icon, so the space's view rule applies and the
    /// answer is 404 either way.
    /// </summary>
    private static async Task<IResult> GetSpaceIcon(
        string key, AppDbContext db, IPermissionService perms, IProfileMediaService media)
    {
        var space = await db.Spaces.AsNoTracking()
            .Where(s => s.Key == key.ToUpperInvariant())
            .Select(s => new { s.Id, s.IconKind })
            .FirstOrDefaultAsync();
        if (space is null || space.IconKind != SpaceIconKind.Image) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();

        var stream = media.OpenRead(media.KeyFor(ProfileMediaKind.SpaceIcon, space.Id));
        if (stream is null) return Results.NotFound();

        return Results.Stream(stream, "image/webp", enableRangeProcessing: false);
    }

    private static async Task<IResult> UploadSpaceIcon(
        string key, IFormFile? file, AppDbContext db, IPermissionService perms, IProfileMediaService media)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        if (file is null || file.Length == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["An image file is required."] });

        StoredMedia stored;
        try
        {
            await using var source = file.OpenReadStream();
            stored = await media.StoreAsync(ProfileMediaKind.SpaceIcon, space.Id, source);
        }
        catch (ProfileMediaException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [ex.Message] });
        }

        // The hash is what the client puts on the URL; the key is derived from
        // the space id, so it is not worth a column of its own.
        space.IconKind = SpaceIconKind.Image;
        space.IconValue = stored.ContentHash;
        await db.SaveChangesAsync();

        return Results.Ok(new SpaceIconResponse(stored.ContentHash));
    }

    private static async Task<IResult> DeleteSpaceIcon(
        string key, AppDbContext db, IPermissionService perms, IProfileMediaService media)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        Features.Spaces.SpaceIcons.Clear(space, media);
        await db.SaveChangesAsync();
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
