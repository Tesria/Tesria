namespace Tesria.Api.Domain;

/// <summary>
/// A site or pack export of a space, prepared in the background (dev-plan
/// 20.2): queued when asked for, run as the person who asked, and kept for
/// them to download for a day. It keeps going when the page that asked for it
/// is left or closed.
/// </summary>
public class ExportJob
{
    public Guid Id { get; set; }

    /// <summary>Who asked: the only person who sees it or may download its file.</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }

    /// <summary>"site" or "pack".</summary>
    public required string Format { get; set; }

    /// <summary>A site's audience: "anonymous" (the default) or "me". Null for a pack.</summary>
    public string? Audience { get; set; }

    /// <summary>A site's opening look: "glass", or null for Minimal.</summary>
    public string? Style { get; set; }

    public ExportJobStatus Status { get; set; } = ExportJobStatus.Queued;

    /// <summary>Why it failed, in words for the person who asked.</summary>
    public string? Error { get; set; }

    /// <summary>The name the download gets, such as docs-site.zip.</summary>
    public string? FileName { get; set; }
    public long? FileSize { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>When a ready file is deleted.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

public enum ExportJobStatus
{
    Queued = 0,
    Running = 1,
    Ready = 2,
    Failed = 3,
    Canceled = 4,
    /// <summary>Was ready; its file has been deleted, after a day or by a restart.</summary>
    Expired = 5,
}
