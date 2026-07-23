using System.Text.Json;
using ConfluenceClone.Api.Domain;
using ConfluenceClone.Api.Infrastructure.Auth;

namespace ConfluenceClone.Api.Infrastructure.Audit;

/// <summary>Records audit entries for significant actions.</summary>
public interface IAuditLogger
{
    /// <summary>
    /// Queues an audit entry on the current unit of work. It is persisted by the
    /// caller's <c>SaveChangesAsync</c>, so the entry and the change it describes
    /// commit together (or not at all).
    /// </summary>
    void Record(string action, string targetType, Guid? targetId, object? metadata = null);
}

public sealed class AuditLogger(AppDbContext db, CurrentUser current) : IAuditLogger
{
    public void Record(string action, string targetType, Guid? targetId, object? metadata = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorId = current.Id,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
}
