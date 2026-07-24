using System.Text.Json;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure;
using ConfluenceClone.Api.Infrastructure.Auth;
using ConfluenceClone.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace ConfluenceClone.Api.Features.Templates;

public static class TemplateEndpoints
{
    public record CreateTemplateRequest(Guid? SpaceId, string Name, string? Description, string ContentJson);
    public record TemplateResponse(
        Guid Id, Guid? SpaceId, string Name, string? Description, string ContentJson,
        Guid CreatedById, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/templates").WithTags("Templates").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapDelete("/{id:guid}", Delete);
        return routes;
    }

    /// <summary>
    /// Instance-wide templates, plus (when <paramref name="spaceId"/> is given
    /// and viewable) templates scoped to that space.
    /// </summary>
    private static async Task<IResult> List(AppDbContext db, IPermissionService perms, Guid? spaceId)
    {
        var query = db.PageTemplates.AsNoTracking().Where(t => t.SpaceId == null);
        if (spaceId is { } sid)
        {
            if (!await perms.CanViewSpaceAsync(sid)) return Results.NotFound();
            query = db.PageTemplates.AsNoTracking().Where(t => t.SpaceId == null || t.SpaceId == sid);
        }

        var templates = await query.OrderBy(t => t.Name).ToListAsync();
        return Results.Ok(templates.Select(ToResponse));
    }

    private static async Task<IResult> Create(
        CreateTemplateRequest req, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));
        if (!TryNormalizeContent(req.ContentJson, out var content))
            return Results.ValidationProblem(Error("contentJson", "Content must be valid JSON."));

        if (req.SpaceId is { } spaceId)
        {
            if (!await db.Spaces.AnyAsync(s => s.Id == spaceId))
                return Results.ValidationProblem(Error("spaceId", "Space not found."));
            // Space-scoped templates affect everyone creating pages in that
            // space, so they require the same rights as editing the space.
            if (!await perms.CanEditSpaceAsync(spaceId)) return Results.Forbid();
        }

        var template = new PageTemplate
        {
            Id = Guid.NewGuid(),
            SpaceId = req.SpaceId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            ContentJson = content,
            CreatedById = current.RequireId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PageTemplates.Add(template);
        await db.SaveChangesAsync();

        return Results.Created($"/api/templates/{template.Id}", ToResponse(template));
    }

    private static async Task<IResult> Delete(
        Guid id, AppDbContext db, CurrentUser current, IPermissionService perms)
    {
        var template = await db.PageTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null) return Results.NotFound();

        // Space-scoped: anyone who can edit the space may remove it (it's part
        // of that space's shared configuration). Instance-wide: only its author,
        // mirroring how comments restrict edit/delete to the author.
        var allowed = template.SpaceId is { } spaceId
            ? await perms.CanEditSpaceAsync(spaceId)
            : template.CreatedById == current.RequireId();
        if (!allowed) return Results.Forbid();

        db.PageTemplates.Remove(template);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static bool TryNormalizeContent(string? input, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            normalized = "";
            return false;
        }
        try
        {
            using var _ = JsonDocument.Parse(input);
            normalized = input;
            return true;
        }
        catch (JsonException)
        {
            normalized = "";
            return false;
        }
    }

    private static TemplateResponse ToResponse(PageTemplate t) =>
        new(t.Id, t.SpaceId, t.Name, t.Description, t.ContentJson, t.CreatedById, t.CreatedAt);

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
