namespace Tesria.Api.Domain;

/// <summary>
/// A named set of instance rights, belonging to a tier (dev-plan 11.1).
///
/// The tier (<see cref="UserRole"/>) is the ordering: who may act on whom,
/// who receives admin alerts, whom the two-factor requirement binds, and the
/// three powers reserved to the owner. The role is what the person may
/// actually do. Three built-in roles exist, one per tier; 11.2 adds custom
/// roles within the user and administrator tiers.
/// </summary>
public class Role
{
    public Guid Id { get; set; }

    /// <summary>
    /// <c>user</c>, <c>admin</c> or <c>owner</c> for the built-ins, null for a
    /// custom role. What the seed and the tests look one up by.
    /// </summary>
    public string? Key { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Which tier this role belongs to. A user's tier and their role's tier always agree.</summary>
    public UserRole Tier { get; set; }

    /// <summary>Built-ins cannot be renamed, re-tiered or deleted; their rights can be edited.</summary>
    public bool BuiltIn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];

    public static class Keys
    {
        public const string User = "user";
        public const string Admin = "admin";
        public const string Owner = "owner";

        public static string For(UserRole tier) => tier switch
        {
            UserRole.Owner => Owner,
            UserRole.Admin => Admin,
            _ => User,
        };
    }

    /// <summary>The built-in names. Fixed, because the seed recreates them by key.</summary>
    public static string NameFor(UserRole tier) => tier switch
    {
        UserRole.Owner => "Owner",
        UserRole.Admin => "Administrator",
        _ => "User",
    };
}

/// <summary>
/// One right held by one role. A row is a grant; absence is not. Keys the
/// catalogue no longer defines are ignored on read and dropped on the next
/// write, so removing a right from the code does not need a migration.
/// </summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public required string Key { get; set; }
}
