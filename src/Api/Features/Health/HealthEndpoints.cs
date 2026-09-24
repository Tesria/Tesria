using System.Reflection;
using Tesria.Api.Infrastructure.Backups;

namespace Tesria.Api.Features.Health;

/// <summary>
/// Liveness/readiness endpoints. Kept intentionally simple for Phase 1;
/// Phase 2 adds a database readiness probe once EF Core is configured.
/// </summary>
public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder group)
    {
        // Liveness: is the process up and serving requests?
        group.MapGet("/health", (RestoreState restore) =>
        {
            var version = Infrastructure.Versioning.AppVersion.Full;

            return Results.Ok(new
            {
                status = "ok",
                service = "tesria-api",
                version,
                utc = DateTimeOffset.UtcNow,
                // Anonymous on purpose (dev-plan 9.4): this is what every
                // browser showing the maintenance overlay polls to find out
                // when the wiki is writable again, including one whose
                // session ended because the restore rolled it back. It says
                // that a restore is running and when it started, which is
                // already visible to anyone who tried to write, and nothing
                // about what is being restored.
                maintenance = restore.Current is { } pending
                    ? new { reason = "restore", startedAt = pending.StartedAt }
                    : null,
            });
        })
        .WithName("Health")
        .WithSummary("Liveness probe");

        return group;
    }
}
