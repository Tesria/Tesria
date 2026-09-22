using System.Threading.RateLimiting;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.RateLimiting;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Request rate limits (dev-plan 3.2), keyed on the client address that 3.0
/// made real.
///
/// Three limiters: a tight one on the endpoints that take a credential
/// (sign-in, registration, recovery), a per-account one on API-token minting,
/// and a generous global one for callers with no session at all, which is
/// what stands between a public-read space (Phase 5) and a scraper. Signed-in
/// callers are not globally limited: their identity is the accountability,
/// and the account lockout in <see cref="AuthLockout"/> covers the credential
/// path.
///
/// Every limit is a site setting. The limit value is part of the partition
/// key, so changing it starts fresh windows immediately rather than waiting
/// for old partitions to idle out.
/// </summary>
public static class RateLimits
{
    public const string AuthPolicy = "auth";
    public const string TokenMintPolicy = "token-mint";
    public const string ImportPolicy = "import";

    /// <summary>Default when settings have not loaded yet: the same defaults <see cref="SiteSettings"/> declares.</summary>
    private static readonly SiteSettings Defaults = new();

    public static void Configure(RateLimiterOptions options, SiteSettingsCache cache)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, ct) =>
        {
            var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                : 60;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsync(
                $$"""{"title":"Too many requests","status":429,"retryAfterSeconds":{{seconds}}}""", ct);
        };

        options.AddPolicy(AuthPolicy, http =>
        {
            var limit = Math.Max(1, Settings(cache).LoginRateLimitPerMinute);
            return RateLimitPartition.GetSlidingWindowLimiter($"{Address(http)}|{limit}", _ => new()
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
        });

        options.AddPolicy(TokenMintPolicy, http =>
        {
            var limit = Math.Max(1, Settings(cache).TokenMintLimitPerHour);
            var user = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? Address(http);
            return RateLimitPartition.GetSlidingWindowLimiter($"{user}|{limit}", _ => new()
            {
                PermitLimit = limit,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 12,
                QueueLimit = 0,
            });
        });

        // Importing a pack (dev-plan 8.5) is expensive and nobody does it
        // often. Ten an hour is past anything a person does by hand and short
        // of anything a script would find worth writing, and unlike the limits
        // above it is not a site setting, because nobody would ever tune it.
        options.AddPolicy(ImportPolicy, http =>
        {
            var user = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? Address(http);
            return RateLimitPartition.GetSlidingWindowLimiter($"import|{user}", _ => new()
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 12,
                QueueLimit = 0,
            });
        });

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            if (http.User.Identity?.IsAuthenticated == true)
                return RateLimitPartition.GetNoLimiter("authenticated");

            var limit = Math.Max(1, Settings(cache).AnonymousRateLimitPerMinute);
            return RateLimitPartition.GetSlidingWindowLimiter($"anon|{Address(http)}|{limit}", _ => new()
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
        });
    }

    private static SiteSettings Settings(SiteSettingsCache cache) => cache.Peek() ?? Defaults;

    private static string Address(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>
/// Per-account lockout after repeated failed sign-ins (dev-plan 3.2). The
/// address limiter above bounds one attacker; this bounds one *target*, so
/// a distributed guess against a single account still runs out of road.
/// </summary>
public static class AuthLockout
{
    public static bool IsLocked(User user, DateTimeOffset now) =>
        user.LockedUntil is { } until && until > now;

    /// <summary>Records a failure and, past the threshold, locks with doubling backoff up to the cap.</summary>
    public static void RecordFailure(User user, SiteSettings settings, DateTimeOffset now)
    {
        user.FailedLoginCount++;
        var over = user.FailedLoginCount - Math.Max(1, settings.LockoutThreshold);
        if (over < 0) return;

        var seconds = Math.Min(
            settings.LockoutMaxSeconds,
            settings.LockoutBaseSeconds * Math.Pow(2, Math.Min(over, 20)));
        user.LockedUntil = now.AddSeconds(Math.Max(1, seconds));
    }

    public static void Reset(User user)
    {
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
    }
}
