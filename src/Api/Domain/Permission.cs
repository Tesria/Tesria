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
/// Default-open: a space with <b>no</b> permission rows is accessible to every
/// authenticated user. As soon as one row exists, access requires a matching
/// grant: directly, or through a group the user belongs to.
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
