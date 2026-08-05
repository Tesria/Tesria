namespace Tesria.Api.Domain;

/// <summary>
/// A reusable starting point for new pages (a "blueprint"). A template with
/// <see cref="SpaceId"/> null is available instance-wide; one scoped to a space
/// is only offered when creating pages in that space.
/// </summary>
public class PageTemplate
{
    public Guid Id { get; set; }

    /// <summary>Null for an instance-wide template; otherwise scoped to one space.</summary>
    public Guid? SpaceId { get; set; }
    public Space? Space { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>ProseMirror document JSON used to seed a new page's first version.</summary>
    public required string ContentJson { get; set; }

    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
