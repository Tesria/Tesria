namespace Tesria.Api.Domain;

/// <summary>What a permission grant is given to.</summary>
public enum PrincipalType
{
    User = 0,
    Group = 1,
}

/// <summary>
/// Operations grantable on a space. Higher levels imply the lower ones:
/// Admin implies Edit implies View.
/// </summary>
public enum SpaceOperation
{
    View = 0,
    Edit = 1,
    Admin = 2,
}

/// <summary>
/// Operations restrictable on a page. Edit is implied by nothing: a View
/// restriction also gates editing, since you cannot edit what you cannot see.
/// </summary>
public enum PageOperation
{
    View = 0,
    Edit = 1,
}

/// <summary>
/// Grants a principal an operation on a space (PLAN §4).
/// <para>
/// Since dev-plan 21.1 a space with no rows is not open: what every signed-in
/// account gets is <see cref="Space.EveryoneAccess"/>, and anything more needs
/// a matching grant, directly or through a group the user belongs to. Each
/// of a space's four groups holds one row here that cannot be changed.
/// </para>
/// </summary>
public class SpacePermission
{
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }

    public PrincipalType PrincipalType { get; set; }

    /// <summary>A user id or group id, depending on <see cref="PrincipalType"/>.</summary>
    public Guid PrincipalId { get; set; }

    public SpaceOperation Operation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Restricts a page to specific principals. Restrictions are inherited by
/// descendant pages: a page is restricted if it, or any ancestor, carries a
/// restriction for the operation. Space admins bypass page restrictions so a
/// space can always be administered.
/// </summary>
public class PageRestriction
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }
    public Page? Page { get; set; }

    public PrincipalType PrincipalType { get; set; }
    public Guid PrincipalId { get; set; }

    public PageOperation Operation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
