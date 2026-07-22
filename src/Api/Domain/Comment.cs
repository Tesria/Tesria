namespace ConfluenceClone.Api.Domain;

/// <summary>
/// A comment on a page. Supports both footer comments (page-level discussion)
/// and inline comments anchored to a selection in the document, plus threaded
/// replies via <see cref="ParentCommentId"/> (PLAN §2, comments promoted into
/// the MVP). Soft-deleted (<see cref="DeletedAt"/>) so a deleted parent does not
/// orphan its replies.
/// </summary>
public class Comment
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }
    public Page? Page { get; set; }

    /// <summary>Parent comment for threaded replies; null for a top-level comment.</summary>
    public Guid? ParentCommentId { get; set; }
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = new List<Comment>();

    public required string Body { get; set; }

    /// <summary>
    /// Inline-anchor payload (JSON) locating the comment in the document, or null
    /// for a footer comment. Stored as jsonb.
    /// </summary>
    public string? AnchorJson { get; set; }

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set when soft-deleted; the row is retained to preserve threads.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
