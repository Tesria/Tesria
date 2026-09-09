namespace Tesria.Api.Domain;

/// <summary>
/// An append-only record of a significant action (PLAN §4). Rows are written in
/// the same transaction as the change they describe, so the trail cannot drift
/// from what actually happened.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>Who acted; null for system actions.</summary>
    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }

    /// <summary>Dotted action name, e.g. <c>page.deleted</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Kind of thing acted on, e.g. <c>page</c> or <c>space</c>.</summary>
    public required string TargetType { get; set; }

    public Guid? TargetId { get; set; }

    /// <summary>Extra context as JSON (stored as jsonb), e.g. the page title.</summary>
    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Position in the hash chain (dev-plan 3.1): 1 for the first chained row,
    /// then contiguous. Assigned inside <c>SaveChanges</c> under a lock, so two
    /// concurrent writers cannot both take the same number. Null only on rows
    /// written before the chain existed and not yet backfilled.
    /// </summary>
    public long? Sequence { get; set; }

    /// <summary>The previous row's <see cref="Hash"/>; the genesis value for row 1.</summary>
    public string? PrevHash { get; set; }

    /// <summary>
    /// SHA-256 over <see cref="PrevHash"/> and this row's canonical form. Any
    /// edit or deletion changes what the next row's <see cref="PrevHash"/>
    /// should have been, which is what verification detects.
    /// </summary>
    public string? Hash { get; set; }
}
