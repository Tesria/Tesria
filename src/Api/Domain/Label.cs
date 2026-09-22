namespace Tesria.Api.Domain;

/// <summary>
/// A tag that can be applied to pages (PLAN §4). Labels are global to the
/// instance, the same label can be used across spaces, and their names are
/// normalised to lower case so "Runbook" and "runbook" are the same label.
/// </summary>
public class Label
{
    public Guid Id { get; set; }

    /// <summary>Normalised (lower-case) label name; unique across the instance.</summary>
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<PageLabel> PageLabels { get; set; } = new List<PageLabel>();
}

/// <summary>Join row applying a <see cref="Label"/> to a <see cref="Page"/>.</summary>
public class PageLabel
{
    public Guid PageId { get; set; }
    public Page? Page { get; set; }

    public Guid LabelId { get; set; }
    public Label? Label { get; set; }

    public Guid AddedById { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
