namespace Tesria.Api.Domain;

/// <summary>
/// An immutable snapshot of a page's content. Created on every save, giving the
/// page its version history and rollback ability (PLAN §4). Content is stored as
/// ProseMirror/TipTap JSON in a Postgres <c>jsonb</c> column; a rendered HTML
/// cache avoids re-rendering on every read.
/// </summary>
public class PageVersion
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }
    public Page? Page { get; set; }

    /// <summary>1-based, monotonically increasing per page.</summary>
    public int VersionNumber { get; set; }

    /// <summary>ProseMirror document JSON (stored as jsonb).</summary>
    public required string ContentJson { get; set; }

    /// <summary>Server-rendered HTML cache of <see cref="ContentJson"/>.</summary>
    public string? ContentHtml { get; set; }

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    /// <summary>Optional "what changed" note supplied at save time.</summary>
    public string? ChangeComment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
