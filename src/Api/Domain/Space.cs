namespace Tesria.Api.Domain;

/// <summary>
/// Top-level container for pages (e.g. per team or project), mirroring
/// Confluence spaces (PLAN §2). Identified by a short, human-friendly
/// <see cref="Key"/> used in URLs.
/// </summary>
public class Space
{
    public Guid Id { get; set; }

    /// <summary>Short uppercase key, unique across the instance (e.g. "ENG").</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Optional page shown as the space landing page.</summary>
    public Guid? HomepageId { get; set; }
    public Page? Homepage { get; set; }

    public bool Archived { get; set; }

    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Page> Pages { get; set; } = new List<Page>();
}
