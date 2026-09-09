using System.Collections.Concurrent;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Telemetry;

/// <summary>
/// Throttles <see cref="Domain.User.LastSeenAt"/> writes.
///
/// "Active in the last 7 days" needs only coarse resolution, so writing on
/// every request would be one UPDATE per request to learn almost nothing. A
/// singleton map of the last write per user means the common case costs a
/// dictionary lookup and no database work at all.
///
/// In-process, like the settings cache: after a restart every user is bumped
/// once more than strictly needed, which is harmless. The map is pruned when
/// it grows past <see cref="MaxTracked"/> so a long-lived process with many
/// users cannot grow it without bound.
/// </summary>
public sealed class LastSeenTracker
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private const int MaxTracked = 10_000;

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastWritten = new();

    /// <summary>
    /// True at most once per <see cref="Interval"/> per user — the caller should
    /// write only when this returns true. Marks the user as written immediately,
    /// so concurrent requests don't all decide to write.
    /// </summary>
    public bool ShouldWrite(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var due = true;

        _lastWritten.AddOrUpdate(
            userId,
            _ => now,
            (_, previous) =>
            {
                if (now - previous < Interval) { due = false; return previous; }
                return now;
            });

        if (due && _lastWritten.Count > MaxTracked) Prune(now);
        return due;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (userId, written) in _lastWritten)
            if (now - written > Interval) _lastWritten.TryRemove(userId, out _);
    }
}

/// <summary>
/// Stamps <c>LastSeenAt</c> for the signed-in caller, at most once per
/// <see cref="LastSeenTracker.Interval"/>.
///
/// Runs after authentication and updates by primary key with no prior read, so
/// the throttled case does nothing and the unthrottled case is a single
/// statement. Failures are swallowed: knowing when someone was last active is
/// never worth failing their request over.
/// </summary>
public sealed class LastSeenMiddleware(RequestDelegate next, LastSeenTracker tracker)
{
    public async Task InvokeAsync(HttpContext context, CurrentUser current, AppDbContext db)
    {
        await next(context);

        if (current.Id is not { } userId || !tracker.ShouldWrite(userId)) return;

        try
        {
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow));
        }
        catch (Exception)
        {
            // Telemetry must never take down a request that already succeeded.
        }
    }
}
