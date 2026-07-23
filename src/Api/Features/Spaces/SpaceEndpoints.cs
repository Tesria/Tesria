using System.Text.RegularExpressions;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Audit;
using ConfluenceClone.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Spaces;

public static partial class SpaceEndpoints
{
    public record CreateSpaceRequest(string Key, string Name, string? Description);
    public record UpdateSpaceRequest(string Name, string? Description);
    public record SpaceResponse(
        Guid Id, string Key, string Name, string? Description,
        bool Archived, Guid? HomepageId, DateTimeOffset CreatedAt);

    // 2–50 chars, starts with a letter, letters/digits only. Stored upper-cased.
    [GeneratedRegex("^[A-Z][A-Z0-9]{1,49}$")]
    private static partial Regex KeyPattern();

    public static IEndpointRouteBuilder MapSpaceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/spaces").WithTags("Spaces").RequireAuthorization();

        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapGet("/{key}", GetByKey);
        group.MapPut("/{key}", Update);
        group.MapPost("/{key}/archive",
            (string key, AppDbContext db, IAuditLogger audit) => ArchiveEndpoint(key, true, db, audit));
        group.MapPost("/{key}/unarchive",
            (string key, AppDbContext db, IAuditLogger audit) => ArchiveEndpoint(key, false, db, audit));

        return routes;
    }

    private static async Task<IResult> List(AppDbContext db, bool includeArchived = false)
    {
        var query = db.Spaces.AsNoTracking();
        if (!includeArchived) query = query.Where(s => !s.Archived);
        var spaces = await query.OrderBy(s => s.Name).ToListAsync();
        return Results.Ok(spaces.Select(ToResponse));
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

    private static async Task<IResult> GetByKey(string key, AppDbContext db)
    {
        var normalizedKey = key.ToUpperInvariant();
        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == normalizedKey);
        return space is null ? Results.NotFound() : Results.Ok(ToResponse(space));
    }

    private static async Task<IResult> Update(string key, UpdateSpaceRequest req, AppDbContext db)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));

        space.Name = name;
        space.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    private static async Task<IResult> ArchiveEndpoint(
        string key, bool archived, AppDbContext db, IAuditLogger audit)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Key == key.ToUpperInvariant());
        if (space is null) return Results.NotFound();
        space.Archived = archived;
        audit.Record(archived ? "space.archived" : "space.unarchived", "space", space.Id, new { space.Key });
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(space));
    }

    private static SpaceResponse ToResponse(Space s) =>
        new(s.Id, s.Key, s.Name, s.Description, s.Archived, s.HomepageId, s.CreatedAt);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
