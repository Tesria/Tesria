namespace Tesria.Api.Domain;

/// <summary>
/// A user's subscription to updates on a page or space. Watching a space also
/// notifies about new pages created in it; watching a page notifies about
/// edits and new comments on that page specifically.
/// </summary>
public class Watch
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>"page" or "space" — matches <see cref="AuditLog.TargetType"/>'s convention.</summary>
    public required string TargetType { get; set; }

    public Guid TargetId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
