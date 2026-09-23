namespace Tesria.Api.Infrastructure.Backups;

/// <summary>
/// Whether a restore is in progress, held in this process (dev-plan 9.4).
///
/// The same fact is on <c>SiteSettings</c>, and both are needed. The
/// database row is what survives the app restarting mid-restore, and it is
/// what the sidecar can see. This object is what the app can still read
/// during the seconds of the swap and the minutes of a point-in-time
/// restore, when the database is renamed away or stopped entirely and the
/// settings cache would throw rather than answer.
///
/// Set before the request that starts a restore returns, and cleared only by
/// the app restarting, which is how every restore ends.
/// </summary>
public sealed class RestoreState
{
    private volatile Pending? _pending;

    /// <summary>The restore this process knows is running, or null.</summary>
    public Pending? Current => _pending;

    public bool InProgress => _pending is not null;

    public void Begin(Guid jobId, DateTimeOffset startedAt, string mode, string describes) =>
        _pending = new Pending(jobId, startedAt, mode, describes);

    /// <summary>
    /// Used when a restore ends without this process restarting: it was
    /// canceled before it began, or the sidecar failed it.
    /// </summary>
    public void Clear() => _pending = null;

    public sealed record Pending(Guid JobId, DateTimeOffset StartedAt, string Mode, string Describes);
}
