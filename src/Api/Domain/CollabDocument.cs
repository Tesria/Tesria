namespace Tesria.Api.Domain;

/// <summary>
/// Persisted CRDT state for a page's live editing session (Yjs update payload),
/// written by the collaboration sidecar. It lives in the main database rather
/// than the sidecar's own store so it is covered by the existing backups
/// (PLAN §5) — in-flight edits survive a restart of the collab service.
/// </summary>
public class CollabDocument
{
    /// <summary>Hocuspocus document name; the page id as a string.</summary>
    public required string DocumentName { get; set; }

    /// <summary>Opaque binary Yjs document state.</summary>
    public required byte[] State { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
