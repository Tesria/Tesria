namespace Tesria.Api.Domain;

public enum SecuritySeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

/// <summary>
/// What a detector saw (dev-plan 3.3). Append-only: the runtime database role
/// cannot update or delete these rows, so the record of an attack cannot be
/// tidied away by the app. The mutable part (who looked at it, what they
/// did) lives on <see cref="SecurityAlert"/>.
/// </summary>
public class SecurityEvent
{
    public Guid Id { get; set; }

    /// <summary>Dotted kind, e.g. <c>login.credential_stuffing</c>. See SecurityThresholds.</summary>
    public required string Kind { get; set; }

    public SecuritySeverity Severity { get; set; }

    /// <summary>What the detector counted on: an address, an actor id, an email hash, or empty.</summary>
    public string Key { get; set; } = "";

    public string? Ip { get; set; }
    public Guid? ActorId { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum SecurityAlertStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2,
}

/// <summary>
/// An event that crossed a threshold and needs a person. Split from the event
/// so acknowledging it never requires an UPDATE on the append-only table.
/// </summary>
public class SecurityAlert
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public SecurityEvent? Event { get; set; }

    public required string Kind { get; set; }
    public SecuritySeverity Severity { get; set; }
    public string Key { get; set; } = "";
    public string? Ip { get; set; }
    public Guid? ActorId { get; set; }

    public SecurityAlertStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedById { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }
    public string? Note { get; set; }
}

/// <summary>An address or range refused at the door, before authentication.</summary>
public class BlockedNetwork
{
    public Guid Id { get; set; }

    /// <summary>CIDR notation; a bare address is stored as /32 or /128.</summary>
    public required string Cidr { get; set; }

    public string? Reason { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null means until removed.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
