namespace Tesria.Api.Domain;

/// <summary>
/// A named set of users, used as a permission principal so access can be
/// granted to a team rather than person-by-person (PLAN §4).
/// </summary>
public class Group
{
    public Guid Id { get; set; }

    /// <summary>Display name as entered, e.g. "Engineering Team".</summary>
    public required string Name { get; set; }

    /// <summary>Lower-cased <see cref="Name"/>; unique, so names are case-insensitive.</summary>
    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserGroup> Members { get; set; } = new List<UserGroup>();
}

/// <summary>Membership of a <see cref="User"/> in a <see cref="Group"/>.</summary>
public class UserGroup
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
