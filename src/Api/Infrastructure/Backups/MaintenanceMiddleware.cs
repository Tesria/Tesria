using Tesria.Api.Infrastructure.Backups;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// While a restore is running the wiki is read-only (dev-plan 9.4).
///
/// One place, by HTTP method, for the reason the token-scope middleware
/// gives: a rule enforced per endpoint is a rule that is forgotten on the
/// next endpoint. Reads pass, so people can keep reading the wiki right up
/// to the moment the database is swapped; writes are refused with 503 and a
/// body the SPA recognises, so nothing is written to a database that is
/// about to be replaced and silently lost.
///
/// The exceptions are the restore's own endpoints: the page that started
/// the restore has to be able to watch it and cancel it, and the health
/// endpoint is what everyone else polls to find out when it is over.
///
/// This reads <see cref="RestoreState"/>, not the settings cache: during the
/// swap the database cannot be read at all, and a maintenance check that
/// needs the database is no use in exactly the minutes it exists for.
/// </summary>
public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> Safe = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    /// <summary>
    /// Paths that keep working during a restore. Cancel and the job reads are
    /// how it is watched and stopped; undo and discard are refused, because
    /// they are restores themselves and one at a time is the rule.
    /// </summary>
    private static readonly string[] Allowed =
    [
        "/api/health",
    ];

    /// <summary>
    /// The restore's own endpoints answer for themselves. They have precise
    /// refusals of their own ("a restore is already running"), and a blanket
    /// "the wiki is read-only" in front of them would be both less useful and
    /// slightly absurd: the thing making it read-only is what is being asked
    /// about. Each still holds its own gate, so nothing is loosened here.
    /// </summary>
    private const string RestorePrefix = "/api/admin/backups/restore";

    public Task InvokeAsync(HttpContext context, RestoreState restore)
    {
        if (restore.Current is not { } pending) return next(context);
        if (Safe.Contains(context.Request.Method)) return next(context);

        var path = context.Request.Path.Value ?? "";
        if (Allowed.Any(a => path.Equals(a, StringComparison.OrdinalIgnoreCase))) return next(context);
        if (path.StartsWith(RestorePrefix, StringComparison.OrdinalIgnoreCase)) return next(context);
        // A second "restore this backup" while one runs: let the endpoint say
        // so with a 409, which names the reason.
        if (path.StartsWith("/api/admin/backups/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/restore", StringComparison.OrdinalIgnoreCase)) return next(context);

        // Not just the SPA: API tokens and the MCP server get the same answer,
        // and 503 with Retry-After is what a well-behaved client already
        // understands without being taught anything new.
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "30";
        return context.Response.WriteAsJsonAsync(new
        {
            code = "maintenance",
            message = "A restore is in progress. The wiki is read-only until it finishes.",
            maintenance = new
            {
                reason = "restore",
                jobId = pending.JobId,
                startedAt = pending.StartedAt,
                mode = pending.Mode,
                describes = pending.Describes,
            },
        });
    }
}
