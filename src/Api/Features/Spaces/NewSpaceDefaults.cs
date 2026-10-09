using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Features.Groups;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Audit;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Spaces;

/// <summary>
/// Default access for new spaces (dev-plan 21.6), after Confluence's
/// "Defaults for new spaces": what everyone signed in gets, and which groups
/// are granted what. A recommended starting point, not a limit. The New Space
/// wizard starts from it and its creator may change any of it; a create call
/// that leaves a part out gets this part. Administration, Spaces sets it.
/// <para>
/// The groups it may name are Tesria's administrators (the built-in Admins
/// group) and custom groups. Not a space's own groups, which belong to one
/// space; not Users, which is what "everyone signed in" already says; not
/// Owner, a group of one; and not the global groups, which read every space
/// whatever it grants.
/// </para>
/// </summary>
public static class NewSpaceDefaults
{
    public record GroupLevel(Guid GroupId, SpaceOperation Level);

    /// <param name="Kind">"builtin" for Tesria's administrators, "custom" otherwise (<see cref="GroupOverview.Kinds"/>).</param>
    public record DefaultGroup(Guid GroupId, string Name, string Kind, SpaceOperation Level);

    public record DefaultsResponse(SpaceOperation? EveryoneAccess, List<DefaultGroup> Groups);

    public record DefaultsRequest(SpaceOperation? EveryoneAccess, List<GroupLevel>? Groups);

    /// <summary>More than enough for a default; a longer list is a mistake.</summary>
    public const int MaxGroups = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapNewSpaceDefaultEndpoints(this IEndpointRouteBuilder routes)
    {
        // Anyone signed in may read it: the wizard starts from it.
        routes.MapGet("/space-defaults", Get).WithTags("Spaces").RequireAuthorization()
            .Produces<DefaultsResponse>();
        routes.MapPut("/admin/space-defaults", Put).WithTags("Admin").RequireAuthorization()
            .RequirePermission(InstancePermissions.SpacesManage)
            .Produces<DefaultsResponse>();
        return routes;
    }

    /// <summary>The stored groups, unresolved; a row that cannot be read is skipped.</summary>
    public static List<GroupLevel> Parse(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<GroupLevel>>(json ?? "[]", Json) ?? [])
                .Where(g => Enum.IsDefined(g.Level))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The defaults as they stand, each group checked to still exist and still be allowed.</summary>
    public static async Task<DefaultsResponse> ReadAsync(AppDbContext db, ISiteSettingsService settings)
    {
        var s = await settings.GetAsync();
        var stored = Parse(s.NewSpaceGroupsJson);
        var ids = stored.Select(g => g.GroupId).ToList();
        var groups = await db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.SpaceId }).ToDictionaryAsync(g => g.Id);
        var resolved = stored
            .Where(g => groups.TryGetValue(g.GroupId, out var row) && Allowed(row.Id, row.SpaceId))
            .GroupBy(g => g.GroupId).Select(g => g.First())
            .Select(g => new DefaultGroup(g.GroupId, NameOf(g.GroupId, groups[g.GroupId].Name), KindOf(g.GroupId), g.Level))
            .ToList();
        return new DefaultsResponse(s.NewSpaceEveryoneAccess, resolved);
    }

    /// <summary>Whether a group may be named in the defaults, or granted when a space is made.</summary>
    public static bool Allowed(Guid groupId, Guid? spaceId) =>
        spaceId is null && (groupId == BuiltInGroups.AdminsId || !BuiltInGroups.IsBuiltIn(groupId));

    private static string KindOf(Guid groupId) =>
        groupId == BuiltInGroups.AdminsId ? GroupOverview.Kinds.BuiltIn : GroupOverview.Kinds.Custom;

    /// <summary>The built-in Admins group reads as what it is here.</summary>
    private static string NameOf(Guid groupId, string name) =>
        groupId == BuiltInGroups.AdminsId ? "Tesria administrators" : name;

    /// <summary>
    /// Why these groups cannot be granted, or null: each must exist and be
    /// Tesria's administrators or a custom group, at a real level, once.
    /// </summary>
    public static async Task<string?> ProblemAsync(AppDbContext db, IReadOnlyCollection<GroupLevel> groups)
    {
        if (groups.Count > MaxGroups) return $"Choose at most {MaxGroups} groups.";
        if (groups.Any(g => !Enum.IsDefined(g.Level))) return "Choose View, Edit or Administer for each group.";
        if (groups.Select(g => g.GroupId).Distinct().Count() != groups.Count) return "Each group may be named once.";
        var ids = groups.Select(g => g.GroupId).ToList();
        var found = await db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id))
            .Select(g => new { g.Id, g.SpaceId }).ToListAsync();
        if (found.Count != ids.Count) return "A group chosen no longer exists. Choose again.";
        if (found.Any(g => !Allowed(g.Id, g.SpaceId)))
            return "Only Tesria administrators and custom groups can be given access here: everyone signed in is set on its own, and the global groups already read every space.";
        return null;
    }

    private static async Task<IResult> Get(AppDbContext db, ISiteSettingsService settings) =>
        Results.Ok(await ReadAsync(db, settings));

    private static async Task<IResult> Put(
        DefaultsRequest req, AppDbContext db, ISiteSettingsService settings, IAuditLogger audit, CurrentUser current)
    {
        if (req.EveryoneAccess is { } level && !Enum.IsDefined(level))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["everyoneAccess"] = ["Choose No Access, View, Edit or Administer."],
            });
        var groups = req.Groups ?? [];
        if (await ProblemAsync(db, groups) is { } problem)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["groups"] = [problem] });

        var before = await ReadAsync(db, settings);
        await settings.UpdateAsync(s =>
        {
            s.NewSpaceEveryoneAccess = req.EveryoneAccess;
            s.NewSpaceGroupsJson = JsonSerializer.Serialize(groups, Json);
        }, current.Id);
        var after = await ReadAsync(db, settings);
        // Nobody's access changes: only spaces made from now on start here.
        audit.Record("settings.space_defaults_changed", "instance", null, new
        {
            Before = new { Everyone = before.EveryoneAccess?.ToString() ?? "None", Groups = before.Groups.Select(g => $"{g.Name}: {g.Level}") },
            After = new { Everyone = after.EveryoneAccess?.ToString() ?? "None", Groups = after.Groups.Select(g => $"{g.Name}: {g.Level}") },
        });
        await db.SaveChangesAsync();
        return Results.Ok(after);
    }
}
