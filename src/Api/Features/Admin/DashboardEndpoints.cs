using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// The admin dashboard's numbers (dev-plan 2.5).
///
/// One endpoint returning everything, rather than a page that fires a dozen
/// requests: the counts are cheap individually but the round trips are not,
/// and a single response means the whole dashboard is consistent with itself
/// rather than assembled from twelve different instants.
/// </summary>
public static class DashboardEndpoints
{
    public record DailyPoint(DateOnly Date, int Count);

    public record PeopleStats(
        int Total, int Admins, int Suspended, int ActiveLast7Days, int ActiveLast30Days,
        int NewInRange, IReadOnlyList<DailyPoint> LoginsPerDay, IReadOnlyList<DailyPoint> FailedLoginsPerDay);

    public record ContentStats(
        int Spaces, int Pages, int Versions, int Comments, int Attachments,
        long StorageBytes, IReadOnlyList<DailyPoint> PagesCreatedPerDay);

    public record UsageStats(
        int ViewsInRange, IReadOnlyList<DailyPoint> ViewsPerDay,
        IReadOnlyList<TopPage> TopPages, IReadOnlyList<TopEditor> TopEditors);

    public record TopPage(Guid PageId, string Title, string SpaceKey, int Views);
    public record TopEditor(Guid UserId, string DisplayName, int Versions);

    /// <summary>The Health tiles 2.5 promised; they needed dev-plan 9.1 to have anything to read.</summary>
    public record HealthStats(IReadOnlyList<BackupEndpoints.Health> Backups);

    public record DashboardResponse(
        int RangeDays, DateTimeOffset GeneratedAt,
        PeopleStats People, ContentStats Content, UsageStats Usage, HealthStats Health);

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/admin/dashboard", GetDashboard)
            .WithTags("Admin")
            .RequireAuthorization(AuthPolicies.RequireAdmin);
        return routes;
    }

    private static async Task<IResult> GetDashboard(AppDbContext db, int? rangeDays)
    {
        var days = Math.Clamp(rangeDays ?? 30, 1, 365);
        var now = DateTimeOffset.UtcNow;
        // The range is whole UTC days ending with today: `days` of them, today
        // last. Starting from `now - days` instead gave a window whose final
        // day was yesterday, so everything that happened today was counted but
        // had no bucket to land in — every chart read zero for the current day.
        var firstDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-(days - 1));
        var since = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Timestamps are compared and grouped in memory throughout. The SQLite
        // provider used by the tests can neither compare nor ORDER BY a
        // DateTimeOffset, and the rows involved are bounded by the range.
        var users = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.DisplayName, u.Role, u.Status, u.LastSeenAt, u.CreatedAt })
            .ToListAsync();

        var auditInRange = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "user.login" || a.Action == "user.login_failed")
            .Select(a => new { a.Action, a.CreatedAt })
            .ToListAsync();

        var people = new PeopleStats(
            Total: users.Count,
            Admins: users.Count(u => u.Role == UserRole.Admin),
            Suspended: users.Count(u => u.Status == UserStatus.Suspended),
            ActiveLast7Days: users.Count(u => u.LastSeenAt >= now.AddDays(-7)),
            ActiveLast30Days: users.Count(u => u.LastSeenAt >= now.AddDays(-30)),
            NewInRange: users.Count(u => u.CreatedAt >= since),
            LoginsPerDay: Daily(auditInRange.Where(a => a.Action == "user.login" && a.CreatedAt >= since)
                .Select(a => a.CreatedAt), since, days),
            FailedLoginsPerDay: Daily(auditInRange.Where(a => a.Action == "user.login_failed" && a.CreatedAt >= since)
                .Select(a => a.CreatedAt), since, days));

        var pageCreatedAt = await db.Pages.AsNoTracking()
            .Where(p => p.Status == PageStatus.Current)
            .Select(p => p.CreatedAt)
            .ToListAsync();

        var content = new ContentStats(
            Spaces: await db.Spaces.CountAsync(),
            Pages: pageCreatedAt.Count,
            Versions: await db.PageVersions.CountAsync(),
            Comments: await db.Comments.CountAsync(),
            Attachments: await db.Attachments.CountAsync(),
            StorageBytes: await db.Attachments.SumAsync(a => (long?)a.Size) ?? 0L,
            PagesCreatedPerDay: Daily(pageCreatedAt.Where(c => c >= since), since, days));

        var views = await db.PageViews.AsNoTracking()
            .Select(v => new { v.PageId, v.ViewedAt })
            .ToListAsync();
        var viewsInRange = views.Where(v => v.ViewedAt >= since).ToList();

        var topPageIds = viewsInRange
            .GroupBy(v => v.PageId)
            .Select(g => new { PageId = g.Key, Views = g.Count() })
            .OrderByDescending(x => x.Views)
            .Take(10)
            .ToList();

        // Titles resolved in one query for the ten that matter, rather than
        // joining every view row to its page.
        var ids = topPageIds.Select(t => t.PageId).ToList();
        var pageInfo = await db.Pages.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, SpaceKey = p.Space!.Key })
            .ToListAsync();

        var topPages = topPageIds
            .Select(t =>
            {
                var info = pageInfo.FirstOrDefault(p => p.Id == t.PageId);
                return new TopPage(t.PageId, info?.Title ?? "(deleted)", info?.SpaceKey ?? "", t.Views);
            })
            .ToList();

        var versionAuthors = await db.PageVersions.AsNoTracking()
            .Select(v => new { v.AuthorId, v.CreatedAt })
            .ToListAsync();

        var topEditors = versionAuthors
            .Where(v => v.CreatedAt >= since)
            .GroupBy(v => v.AuthorId)
            .Select(g => new { AuthorId = g.Key, Versions = g.Count() })
            .OrderByDescending(x => x.Versions)
            .Take(10)
            .Select(x => new TopEditor(
                x.AuthorId,
                users.FirstOrDefault(u => u.Id == x.AuthorId)?.DisplayName ?? "Deleted user",
                x.Versions))
            .ToList();

        var usage = new UsageStats(
            ViewsInRange: viewsInRange.Count,
            ViewsPerDay: Daily(viewsInRange.Select(v => v.ViewedAt), since, days),
            TopPages: topPages,
            TopEditors: topEditors);

        var health = new HealthStats(await BackupEndpoints.HealthAsync(db));

        return Results.Ok(new DashboardResponse(days, now, people, content, usage, health));
    }

    /// <summary>
    /// Buckets timestamps into one point per day across the whole range,
    /// including days with nothing.
    ///
    /// The zero days matter: a sparkline built only from days that had activity
    /// silently compresses a quiet week into a single point and reads as steady
    /// use when the truth is the opposite.
    ///
    /// <paramref name="since"/> must be midnight UTC of the first day, so the
    /// <paramref name="days"/> buckets end on today (see GetDashboard).
    /// </summary>
    private static List<DailyPoint> Daily(
        IEnumerable<DateTimeOffset> timestamps, DateTimeOffset since, int days)
    {
        var counts = timestamps
            .GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        var start = DateOnly.FromDateTime(since.UtcDateTime);
        return Enumerable.Range(0, days)
            .Select(offset =>
            {
                var date = start.AddDays(offset);
                return new DailyPoint(date, counts.GetValueOrDefault(date));
            })
            .ToList();
    }
}
