namespace Tesria.Api.Domain;

/// <summary>
/// A per-recipient notification generated when something a user watches
/// changes. Shaped like <see cref="AuditLog"/> (same Action/TargetType/
/// MetadataJson convention) plus a recipient and read state.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Dotted action name, e.g. <c>page.updated</c>.</summary>
    public required string Action { get; set; }

    /// <summary>"page" or "space".</summary>
    public required string TargetType { get; set; }

    public Guid TargetId { get; set; }

    /// <summary>Who triggered the notification; null for system actions.</summary>
    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }

    /// <summary>Extra context as JSON (stored as jsonb), e.g. the page title.</summary>
    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// When this was sent (or attempted) by email, or null while it waits in
    /// the outbox (dev-plan 4.3). Set on the attempt, not on success: a dead
    /// mail server should produce one audited failure, not one a minute.
    /// </summary>
    public DateTimeOffset? EmailedAt { get; set; }
}
