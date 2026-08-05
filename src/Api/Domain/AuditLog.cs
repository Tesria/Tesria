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
}
