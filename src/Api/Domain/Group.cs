namespace Tesria.Api.Domain;

/// <summary>
/// A named set of users, used as a permission principal so access can be
/// granted to a team rather than person-by-person (PLAN §4).
/// <para>
/// Three kinds share the table (dev-plan 21.1): the built-in groups
/// (<see cref="Infrastructure.Permissions.BuiltInGroups"/>), the custom
/// groups an administrator makes, and a space's own four groups, which
/// carry <see cref="SpaceId"/> and <see cref="SpaceRole"/>, belong to that
/// space, and go when it does.
/// </para>
/// </summary>
public class Group
{
    public Guid Id { get; set; }

    /// <summary>
    /// Display name as entered, e.g. "Engineering Team". For a space's group
    /// it is only the role ("Viewers"): the name people see is derived from
    /// the space's name when it is read, so two spaces of one name, and a
    /// renamed space, need nothing (21.1).
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Lower-cased <see cref="Name"/>; unique among the groups that are not a
    /// space's, so names are case-insensitive. A space's group stores a key
    /// of its own (<c>space:&lt;id&gt;:viewers</c>) that the unique index
    /// leaves out.
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>The space this group belongs to (dev-plan 21.1), or null for every other group.</summary>
    public Guid? SpaceId { get; set; }
    public Space? Space { get; set; }

    /// <summary>Which of its space's four groups this is; set exactly when <see cref="SpaceId"/> is.</summary>
    public SpaceGroupRole? SpaceRole { get; set; }

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

/// <summary>
/// A space's four groups (dev-plan 21.1). Each holds one grant on its space
/// that cannot be changed or revoked: Viewers View, Editors Edit, Admins
/// Admin, and Reviewers View until review mode (22.4) gives them more.
/// </summary>
public enum SpaceGroupRole
{
    Viewers = 0,
    Editors = 1,
    Admins = 2,
    Reviewers = 3,
}

/// <summary>
/// A grant to one person that the 21.1 seed moved into one of its space's
/// groups. The original row is gone; this keeps what it was (its id, level
/// and when it was made), so the move can be reversed by hand. Append-only,
/// and no foreign keys: it is history, and outlives the space.
/// </summary>
public class SpaceGrantMove
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    /// <summary>The id the moved <see cref="SpacePermission"/> row had.</summary>
    public Guid GrantId { get; set; }
    public Guid UserId { get; set; }
    public SpaceOperation Operation { get; set; }
    /// <summary>When the original grant was made.</summary>
    public DateTimeOffset GrantCreatedAt { get; set; }
    /// <summary>The space group the person was added to instead.</summary>
    public Guid GroupId { get; set; }
    public DateTimeOffset MovedAt { get; set; }
}
