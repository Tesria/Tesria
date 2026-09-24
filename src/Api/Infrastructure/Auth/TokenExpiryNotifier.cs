using Microsoft.EntityFrameworkCore;
using Tesria.Api.Infrastructure.Notifications;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Tells people a week before one of their API tokens expires (dev-plan
/// 14.1), once per token, in the bell and by email like other
/// notifications, so a script does not stop without warning. Hourly is far
/// finer than a week needs; it is cheap, and it means a token made with a
/// short life is still warned about in time.
/// </summary>
public sealed class TokenExpiryNotifier(IServiceScopeFactory scopes, ILogger<TokenExpiryNotifier> logger)
    : BackgroundService
{
    public static readonly TimeSpan WarnAhead = TimeSpan.FromDays(7);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Checking for API tokens about to expire failed");
            }
            // The same hourly pass forgets token use older than 90 days
            // (the admin API tokens tab).
            try
            {
                using var scope = scopes.CreateScope();
                await TokenUsage.PruneAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Removing old API token use failed");
            }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass: every token expiring within a week and not yet warned about. Public for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = DateTimeOffset.UtcNow;
        // Compared in memory: the SQLite test provider cannot compare
        // DateTimeOffset in SQL, and tokens are few.
        var candidates = await db.ApiTokens.Where(t => t.ExpiresAt != null && t.ExpiryWarnedAt == null).ToListAsync(ct);
        var due = candidates.Where(t => t.ExpiresAt > now && t.ExpiresAt <= now + WarnAhead).ToList();
        foreach (var token in due)
        {
            await notifications.NotifySystemAsync(token.UserId, "token.expiring", "token", token.Id,
                new { Name = token.Name, ExpiresAt = token.ExpiresAt!.Value.ToString("O") });
            token.ExpiryWarnedAt = now;
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
