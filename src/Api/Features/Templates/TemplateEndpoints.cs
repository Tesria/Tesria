using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Templates;

public static class TemplateEndpoints
{
    public record CreateTemplateRequest(Guid? SpaceId, string Name, string? Description, string ContentJson);
    public record UpdateTemplateRequest(string Name, string? Description);

    /// <summary>
    /// <c>CanManage</c> is whether the caller may rename or delete it, so the
    /// Templates tab offers only what will work (dev-plan 10.5 step 1).
    /// </summary>
    public record TemplateResponse(
        Guid Id, Guid? SpaceId, string Name, string? Description, string ContentJson,
        Guid CreatedById, DateTimeOffset CreatedAt, string? CreatedByName = null, bool CanManage = false);

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/templates").WithTags("Templates").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        return routes;
    }

    /// <summary>
    /// Instance-wide templates, plus (when <paramref name="spaceId"/> is given
    /// and viewable) templates scoped to that space.
    /// </summary>
    private static async Task<IResult> List(
        AppDbContext db, IPermissionService perms, IInstancePermissions rights, CurrentUser current, Guid? spaceId)
    {
        var query = db.PageTemplates.AsNoTracking().Where(t => t.SpaceId == null);
        if (spaceId is { } sid)
        {
            if (!await perms.CanViewSpaceAsync(sid)) return Results.NotFound();
            query = db.PageTemplates.AsNoTracking().Where(t => t.SpaceId == null || t.SpaceId == sid);
        }

        var templates = await query.OrderBy(t => t.Name).ToListAsync();
        var authorIds = templates.Select(t => t.CreatedById).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var result = new List<TemplateResponse>();
        foreach (var t in templates)
            result.Add(ToResponse(t) with
            {
                CreatedByName = names.GetValueOrDefault(t.CreatedById),
                CanManage = await CanManageAsync(t, current, perms, rights),
            });
        return Results.Ok(result);
    }

    /// <summary>
    /// Who may rename or delete a template. A space's templates are part of
    /// that space's shared setup, so anyone who can edit the space. An
    /// instance-wide one is offered in every space, so it takes "Manage
    /// instance-wide templates" (dev-plan 14.1), held by administrators by
    /// default; before, any user could create one, and its author could
    /// always change it.
    /// </summary>
    private static async Task<bool> CanManageAsync(
        PageTemplate t, CurrentUser current, IPermissionService perms, IInstancePermissions rights) =>
        t.SpaceId is { } spaceId
            ? await perms.CanEditSpaceAsync(spaceId)
            : await rights.HasAsync(InstancePermissions.TemplatesInstance);

    private static async Task<IResult> Update(
        Guid id, UpdateTemplateRequest req, AppDbContext db, CurrentUser current,
        IPermissionService perms, IInstancePermissions rights)
    {
        var template = await db.PageTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null) return Results.NotFound();
        if (template.SpaceId is { } sid && !await perms.CanViewSpaceAsync(sid)) return Results.NotFound();
        if (!await CanManageAsync(template, current, perms, rights)) return Results.Forbid();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));
        template.Name = name;
        template.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(template) with { CanManage = true });
    }

    private static async Task<IResult> Create(
        CreateTemplateRequest req, AppDbContext db, CurrentUser current, IPermissionService perms,
        IInstancePermissions rights)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Name is required."));
        if (!TryNormalizeContent(req.ContentJson, out var content))
            return Results.ValidationProblem(Error("contentJson", "Content must be valid JSON."));

        if (req.SpaceId is { } spaceId)
        {
            // A space the caller cannot see is as missing as one that does
            // not exist (dev-plan 14.1).
            if (!await db.Spaces.AnyAsync(s => s.Id == spaceId) || !await perms.CanViewSpaceAsync(spaceId))
                return Results.ValidationProblem(Error("spaceId", "Space not found."));
            // Space-scoped templates affect everyone creating pages in that
            // space, so they require the same rights as editing the space.
            if (!await perms.CanEditSpaceAsync(spaceId)) return Results.Forbid();
        }
        else if (!await rights.HasAsync(InstancePermissions.TemplatesInstance))
        {
            return Results.Json(new
            {
                code = "permission_required",
                permission = InstancePermissions.TemplatesInstance,
                message = "Your role does not have the right to manage instance-wide templates. Save it as a template of this space instead.",
            }, statusCode: StatusCodes.Status403Forbidden);
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
        Guid id, AppDbContext db, CurrentUser current, IPermissionService perms, IInstancePermissions rights)
    {
        var template = await db.PageTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null) return Results.NotFound();
        if (template.SpaceId is { } sid && !await perms.CanViewSpaceAsync(sid)) return Results.NotFound();

        if (!await CanManageAsync(template, current, perms, rights)) return Results.Forbid();

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
