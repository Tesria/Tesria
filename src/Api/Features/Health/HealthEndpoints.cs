using System.Reflection;

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
        group.MapGet("/health", () =>
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "0.1.0";

            return Results.Ok(new
            {
                status = "ok",
                service = "tesria-api",
                version,
                utc = DateTimeOffset.UtcNow
            });
        })
        .WithName("Health")
        .WithSummary("Liveness probe");

        return group;
    }
}
