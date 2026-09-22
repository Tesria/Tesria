using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Auth;

namespace Tesria.Api.Infrastructure.Audit;

/// <summary>Records audit entries for significant actions.</summary>
public interface IAuditLogger
{
    /// <summary>
    /// Queues an audit entry on the current unit of work. It is persisted by the
    /// caller's <c>SaveChangesAsync</c>, so the entry and the change it describes
    /// commit together (or not at all).
    /// </summary>
    void Record(string action, string targetType, Guid? targetId, object? metadata = null);

    /// <summary>
    /// Records an entry attributed to <paramref name="actorId"/> rather than to
    /// the current request's principal. Needed for sign-in events, which happen
    /// before the principal exists, without this they would all be recorded
    /// with a null actor.
    /// </summary>
    void RecordAs(Guid? actorId, string action, string targetType, Guid? targetId, object? metadata = null);
}

public sealed class AuditLogger(AppDbContext db, CurrentUser current) : IAuditLogger
{
    public void Record(string action, string targetType, Guid? targetId, object? metadata = null) =>
        RecordAs(current.Id, action, targetType, targetId, metadata);

    public void RecordAs(Guid? actorId, string action, string targetType, Guid? targetId, object? metadata = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
}
