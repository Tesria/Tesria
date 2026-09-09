using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Groups;

public static class GroupEndpoints
{
    public record SaveGroupRequest(string Name, string? Description);
    public record AddMemberRequest(Guid UserId);
    public record GroupResponse(Guid Id, string Name, string? Description, int MemberCount);
    public record MemberResponse(Guid UserId, string Email, string DisplayName);
    public record UserResponse(
        Guid Id, string Email, string DisplayName, string? AvatarHash, int? AvatarVariant);

    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder routes)
    {
        var groups = routes.MapGroup("/groups").WithTags("Groups").RequireAuthorization();
        // Anyone signed in may see groups — the permission picker needs the
        // list. Shaping them is instance administration.
        groups.MapGet("/", List);
        groups.MapGet("/{id:guid}/members", Members);
        var manage = groups.MapGroup("").RequireAuthorization(Infrastructure.Auth.AuthPolicies.RequireAdmin);
        manage.MapPost("/", Create);
        manage.MapPut("/{id:guid}", Update);
        manage.MapDelete("/{id:guid}", Delete);
        manage.MapPost("/{id:guid}/members", AddMember);
        manage.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMember);

        // Directory of accounts, used when picking permission principals.
        routes.MapGet("/users", ListUsers).WithTags("Groups").RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> ListUsers(AppDbContext db)
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserResponse(u.Id, u.Email, u.DisplayName, u.AvatarHash, u.AvatarVariant))
            .ToListAsync();
        return Results.Ok(users);
    }

    private static async Task<IResult> List(AppDbContext db)
    {
        var groups = await db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupResponse(g.Id, g.Name, g.Description, g.Members.Count))
            .ToListAsync();
        return Results.Ok(groups);
    }

    private static async Task<IResult> Create(
        SaveGroupRequest req, AppDbContext db, IAuditLogger audit)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Group name is required."));

        var normalized = name.ToLowerInvariant();
        if (await db.Groups.AnyAsync(g => g.NormalizedName == normalized))
            return Results.Conflict(new { message = $"A group named '{name}' already exists." });

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalized,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Groups.Add(group);
        audit.Record("group.created", "group", group.Id, new { group.Name });
        await db.SaveChangesAsync();

        return Results.Created($"/api/groups/{group.Id}", new GroupResponse(group.Id, group.Name, group.Description, 0));
    }

    private static async Task<IResult> Update(
        Guid id, SaveGroupRequest req, AppDbContext db, IAuditLogger audit)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return Results.NotFound();

        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return Results.ValidationProblem(Error("name", "Group name is required."));

        var normalized = name.ToLowerInvariant();
        if (await db.Groups.AnyAsync(g => g.NormalizedName == normalized && g.Id != id))
            return Results.Conflict(new { message = $"A group named '{name}' already exists." });

        group.Name = name;
        group.NormalizedName = normalized;
        group.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        audit.Record("group.updated", "group", group.Id, new { group.Name });
        await db.SaveChangesAsync();

        var count = await db.UserGroups.CountAsync(ug => ug.GroupId == id);
        return Results.Ok(new GroupResponse(group.Id, group.Name, group.Description, count));
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db, IAuditLogger audit)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return Results.NotFound();

        db.Groups.Remove(group);
        audit.Record("group.deleted", "group", group.Id, new { group.Name });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Members(Guid id, AppDbContext db)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return Results.NotFound();
        var members = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.GroupId == id)
            .Select(ug => new MemberResponse(ug.UserId, ug.User!.Email, ug.User.DisplayName))
            .ToListAsync();
        return Results.Ok(members.OrderBy(m => m.DisplayName));
    }

    private static async Task<IResult> AddMember(
        Guid id, AddMemberRequest req, AppDbContext db, IAuditLogger audit)
    {
        if (!await db.Groups.AnyAsync(g => g.Id == id)) return Results.NotFound();
        if (!await db.Users.AnyAsync(u => u.Id == req.UserId))
            return Results.ValidationProblem(Error("userId", "User not found."));

        if (!await db.UserGroups.AnyAsync(ug => ug.GroupId == id && ug.UserId == req.UserId))
        {
            db.UserGroups.Add(new UserGroup
            {
                GroupId = id,
                UserId = req.UserId,
                AddedAt = DateTimeOffset.UtcNow,
            });
            audit.Record("group.member_added", "group", id, new { req.UserId });
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMember(
        Guid id, Guid userId, AppDbContext db, IAuditLogger audit)
    {
        var link = await db.UserGroups.FirstOrDefaultAsync(ug => ug.GroupId == id && ug.UserId == userId);
        if (link is null) return Results.NotFound();

        db.UserGroups.Remove(link);
        audit.Record("group.member_removed", "group", id, new { UserId = userId });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
