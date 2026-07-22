namespace ConfluenceClone.Api.Domain;

/// <summary>
/// A wiki page. Pages form a hierarchical tree within a space via
/// <see cref="ParentPageId"/> (self-referencing). Content is not stored here
/// directly: the live content is whichever <see cref="PageVersion"/> is pointed
/// to by <see cref="CurrentVersionId"/>, so every edit is a new version and full
/// history/rollback is preserved (PLAN §4).
/// </summary>
public class Page
{
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }

    /// <summary>Parent in the page tree; null for a top-level page.</summary>
    public Guid? ParentPageId { get; set; }
    public Page? ParentPage { get; set; }
    public ICollection<Page> Children { get; set; } = new List<Page>();

    public required string Title { get; set; }

    /// <summary>The version currently displayed. Null only before the first save.</summary>
    public Guid? CurrentVersionId { get; set; }
    public PageVersion? CurrentVersion { get; set; }

    /// <summary>Sort order among siblings (sparse; leaves gaps for reordering).</summary>
    public int Position { get; set; }

    public PageStatus Status { get; set; } = PageStatus.Current;

    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PageVersion> Versions { get; set; } = new List<PageVersion>();
}

public enum PageStatus
{
    Draft = 0,
    Current = 1,
    Archived = 2,
}
