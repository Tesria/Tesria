namespace Tesria.Api.Domain;

/// <summary>
/// An outbound HTTP subscription: when a matching event happens in a space,
/// its JSON payload is POSTed to <see cref="Url"/>, signed with
/// <see cref="Secret"/> so the receiver can verify authenticity. Scoped to a
/// space and managed by its admins, since a webhook can leak that space's
/// content changes to an external system.
/// </summary>
public class Webhook
{
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }

    public required string Url { get; set; }

    /// <summary>Signs delivered payloads (HMAC-SHA256); shown to the user once, at creation.</summary>
    public required string Secret { get; set; }

    /// <summary>Comma-separated action names (e.g. "page.updated,comment.created"), or "*" for all.</summary>
    public required string Events { get; set; }

    public bool Enabled { get; set; } = true;

    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
