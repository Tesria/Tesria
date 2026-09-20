using System.Text.RegularExpressions;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Spaces;

public static partial class SpaceEndpoints
{
    public record CreateSpaceRequest(string Key, string Name, string? Description);
    public record UpdateSpaceRequest(
        string Name, string? Description,
        SpaceIconKind? IconKind = null, string? IconValue = null, int? IconColor = null);
    public record SpaceResponse(
        Guid Id, string Key, string Name, string? Description,
        bool Archived, Guid? HomepageId, DateTimeOffset CreatedAt,
        bool IsPublic, bool PublicComments,
        SpaceIconKind IconKind, string? IconValue, int? IconColor);

    // 2–50 chars, starts with a letter, letters/digits only. Stored upper-cased.
    [GeneratedRegex("^[A-Z][A-Z0-9]{1,49}$")]
    private static partial Regex KeyPattern();

    public static IEndpointRouteBuilder MapSpaceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/spaces").WithTags("Spaces").RequireAuthorization();

        // Readable without a session (dev-plan 5.2); the permission service decides what an anonymous caller sees.
        group.MapGet("/", List).AllowAnonymous();
        group.MapPost("/", Create)
            .RequirePermission(Infrastructure.Permissions.InstancePermissions.SpacesCreate);
        group.MapGet("/{key}", GetByKey).AllowAnonymous();
        group.MapPut("/{key}", Update);
        group.MapPost("/{key}/archive",
            (string key, AppDbContext db, IAuditLogger audit, IPermissionService perms)
                => ArchiveEndpoint(key, true, db, audit, perms));
        group.MapPost("/{key}/unarchive",
            (string key, AppDbContext db, IAuditLogger audit, IPermissionService perms)
                => ArchiveEndpoint(key, false, db, audit, perms));

        return routes;
    }

    private static async Task<IResult> List(
        AppDbContext db, IPermissionService perms, bool includeArchived = false)
    {
        var query = db.Spaces.AsNoTracking();
        if (!includeArchived) query = query.Where(s => !s.Archived);
        var spaces = await query.OrderBy(s => s.Name).ToListAsync();

        // Only surface spaces the caller may view.
        var viewable = await perms.ViewableSpaceIdsAsync();
        return Results.Ok(spaces.Where(s => viewable.Contains(s.Id)).Select(ToResponse));
    }

    private static async Task<IResult> Create(
        CreateSpaceRequest req, AppDbContext db, CurrentUser current, IAuditLogger audit)
    {
        var key = (req.Key ?? "").Trim().ToUpperInvariant();
        var name = (req.Name ?? "").Trim();

        if (!KeyPattern().IsMatch(key))
            return Results.ValidationProblem(Error("key",
                "Key must be 2–50 characters, start with a letter, and contain only letters and digits."));
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));

        if (await db.Spaces.AnyAsync(s => s.Key == key))
            return Results.Conflict(new { message = $"A space with key '{key}' already exists." });

        var space = new Space
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            CreatedById = current.RequireId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Spaces.Add(space);
        audit.Record("space.created", "space", space.Id, new { space.Key, space.Name });
        await db.SaveChangesAsync();

        return Results.Created($"/api/spaces/{space.Key}", ToResponse(space));
    }

    private static async Task<IResult> GetByKey(string key, AppDbContext db, IPermissionService perms)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == normalizedKey);
        if (space is null) return Results.NotFound();
        // 404 rather than 403 so a hidden space's existence isn't disclosed.
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        return Results.Ok(ToResponse(space));
    }

    private static async Task<IResult> Update(
        string key, UpdateSpaceRequest req, AppDbContext db, IPermissionService perms,
        IProfileMediaService media)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));

        if (SpaceIcons.ValidateColor(req.IconColor) is { } colorError)
            return Results.ValidationProblem(Error("iconColor", colorError));

        // The icon (dev-plan 6). An omitted kind leaves it alone; the picture
        // case is not settable here — it needs the upload endpoint, which has
        // the bytes.
        switch (req.IconKind)
        {
            case SpaceIconKind.None:
                SpaceIcons.Clear(space, media);
                break;

            case SpaceIconKind.Emoji:
                var (emoji, emojiError) = SpaceIcons.NormalizeEmoji(req.IconValue);
                if (emojiError is not null) return Results.ValidationProblem(Error("iconValue", emojiError));
                SpaceIcons.Clear(space, media);
                space.IconKind = SpaceIconKind.Emoji;
                space.IconValue = emoji;
                break;

            case SpaceIconKind.Image:
                return Results.ValidationProblem(Error("iconKind", "Upload a picture through the icon endpoint."));
        }

        if (req.IconColor is { } color) space.IconColor = color;

        space.Name = name;
        space.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    private static async Task<IResult> ArchiveEndpoint(
        string key, bool archived, AppDbContext db, IAuditLogger audit, IPermissionService perms)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        if (!await perms.CanViewSpaceAsync(space.Id)) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();
        space.Archived = archived;
        audit.Record(archived ? "space.archived" : "space.unarchived", "space", space.Id, new { space.Key });
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    private static SpaceResponse ToResponse(Space s) =>
        new(s.Id, s.Key, s.Name, s.Description, s.Archived, s.HomepageId, s.CreatedAt, s.IsPublic, s.PublicComments,
            s.IconKind, s.IconValue, s.IconColor);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
