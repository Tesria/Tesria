using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Email;

/// <summary>
/// Turns queued notifications into email (dev-plan 4.3). An outbox: rows are
/// written by the request that caused them and picked up here, so sending
/// never happens inside a request's transaction and a slow mail server
/// slows nobody down.
///
/// Three rules, in order of importance:
/// <list type="number">
/// <item><b>Security alerts to administrators go immediately</b>, whatever
/// the recipient's preference (this is the "email the admin group"
/// requirement, and it is deliberately the first email notification.</item>
/// <item>People on <see cref="EmailNotificationMode.Immediate"/> get each
/// tick's notifications in one message.</item>
/// <item>People on <see cref="EmailNotificationMode.DailyDigest"/> get one
/// message a day, when there is something in it.</item>
/// </list>
/// Nothing is attempted while <c>EmailEnabled</c> is off, and nothing older
/// than a day is ever sent) turning email on must not flood inboxes with
/// last month's history.
/// </summary>
public sealed class NotificationEmailService(
    IServiceScopeFactory scopes, IConfiguration config, ILogger<NotificationEmailService> logger)
    : BackgroundService
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan DigestInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(config.GetValue("Notifications:EmailPollSeconds", 60));
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification email pass failed");
            }
            try { await Task.Delay(period, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass over the outbox. Public so tests can drive it.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await scope.ServiceProvider.GetRequiredService<ISiteSettingsService>().GetAsync(ct);
        if (!settings.EmailEnabled) return 0;

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var baseUrl = SiteUrl.Resolve(settings, config);
        var now = DateTimeOffset.UtcNow;

        // The whole outbox, then filtered in memory: it is small (rows leave
        // it on every pass) and the SQLite test provider cannot compare
        // DateTimeOffset in SQL.
        var pending = (await db.Notifications
                .Include(n => n.User)
                .Include(n => n.Actor)
                .Where(n => n.EmailedAt == null)
                .ToListAsync(ct))
            .Where(n => n.User is { Status: UserStatus.Active })
            .ToList();
        if (pending.Count == 0) return 0;

        var stale = pending.Where(n => now - n.CreatedAt > MaxAge).ToList();
        foreach (var n in stale) n.EmailedAt = now; // too old to be news; retired unsent
        pending = pending.Except(stale).ToList();

        var pageInfo = await PageLinksAsync(db, pending, ct);
        var sent = 0;

        foreach (var group in pending.GroupBy(n => n.UserId))
        {
            var user = group.First().User!;
            var alerts = group.Where(n => n.Action == "security.alert").ToList();
            var rest = group.Except(alerts).ToList();

            if (alerts.Count > 0 && user.Role >= UserRole.Admin)
            {
                var text = Describe(settings.InstanceName, baseUrl, alerts, pageInfo,
                    alerts.Count == 1 ? "A security alert needs your attention." : $"{alerts.Count} security alerts need your attention.");
                var subject = $"[{settings.InstanceName}] Security alert" + (alerts.Count > 1 ? $"s ({alerts.Count})" : "");
                await sender.SendAsync(new EmailMessage(user.Email, subject, text), ct);
                foreach (var n in alerts) n.EmailedAt = now;
                sent++;
            }
            else
            {
                // An alert to a non-admin should not exist; retire it quietly.
                foreach (var n in alerts) n.EmailedAt = now;
            }

            if (rest.Count == 0) continue;
            switch (user.EmailNotifications)
            {
                case EmailNotificationMode.Immediate:
                    await sender.SendAsync(new EmailMessage(user.Email,
                        $"[{settings.InstanceName}] " + (rest.Count == 1 ? Headline(rest[0]) : $"{rest.Count} updates"),
                        Describe(settings.InstanceName, baseUrl, rest, pageInfo, null)), ct);
                    foreach (var n in rest) n.EmailedAt = now;
                    sent++;
                    break;

                case EmailNotificationMode.DailyDigest:
                    if (user.LastDigestAt is { } last && now - last < DigestInterval) break;
                    await sender.SendAsync(new EmailMessage(user.Email,
                        $"[{settings.InstanceName}] Daily digest: {rest.Count} update{(rest.Count == 1 ? "" : "s")}",
                        Describe(settings.InstanceName, baseUrl, rest, pageInfo, "Here is what changed since your last digest.")), ct);
                    foreach (var n in rest) n.EmailedAt = now;
                    user.LastDigestAt = now;
                    sent++;
                    break;

                default:
                    // Off: leave the in-app copy, retire the email copy.
                    foreach (var n in rest) n.EmailedAt = now;
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    private sealed record PageLink(string Title, string SpaceKey);

    private static async Task<Dictionary<Guid, PageLink>> PageLinksAsync(AppDbContext db, List<Notification> rows, CancellationToken ct)
    {
        var ids = rows.Where(n => n.TargetType == "page").Select(n => n.TargetId).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Pages.AsNoTracking().IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.Space!.Key })
            .ToDictionaryAsync(p => p.Id, p => new PageLink(p.Title, p.Key), ct);
    }

    private static string Headline(Notification n) => n.Action switch
    {
        "page.created" => "A page was created",
        "page.updated" => "A page was updated",
        "comment.created" => "New comment",
        "user.mentioned" => "You were mentioned",
        "token.expiring" => "An API token expires soon",
        "token.revoked" => "An administrator revoked an API token",
        _ => n.Action,
    };

    private static string Describe(string instance, string baseUrl, List<Notification> rows,
        Dictionary<Guid, PageLink> pages, string? intro)
    {
        var lines = new List<string>();
        if (intro is not null) lines.Add(intro + "\n");
        foreach (var n in rows.OrderBy(n => n.CreatedAt))
        {
            var who = n.Actor?.DisplayName ?? "Someone";
            var meta = Meta(n.MetadataJson);
            if (n.Action == "security.alert")
            {
                lines.Add($"- Security {meta.GetValueOrDefault("Severity", "alert").ToLowerInvariant()}: {(meta.TryGetValue("Kind", out var kind) ? Security.AlertKinds.Label(kind) : "see the Security page")}");
                lines.Add($"  {baseUrl}/admin/security");
            }
            else if (n.Action == "token.expiring")
            {
                var when = DateTimeOffset.TryParse(meta.GetValueOrDefault("ExpiresAt"), out var at)
                    ? at.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : "soon";
                lines.Add($"- Your API token \"{meta.GetValueOrDefault("Name", "")}\" expires on {when}. Make a new one before then, or scripts using it stop working.");
                lines.Add($"  {baseUrl}/profile#api-tokens");
            }
            else if (n.Action == "token.revoked")
            {
                lines.Add($"- An administrator revoked your API token \"{meta.GetValueOrDefault("Name", "")}\". Anything using it has stopped working; make a new one if you still need it.");
                lines.Add($"  {baseUrl}/profile#api-tokens");
            }
            else if (n.TargetType == "page" && pages.TryGetValue(n.TargetId, out var page))
            {
                var what = n.Action switch
                {
                    "page.created" => "created",
                    "page.updated" => "updated",
                    "comment.created" => "commented on",
                    "user.mentioned" => "mentioned you on",
                    _ => n.Action,
                };
                lines.Add($"- {who} {what} \"{page.Title}\"");
                lines.Add($"  {baseUrl}/spaces/{page.SpaceKey}/pages/{n.TargetId}");
            }
            else
            {
                lines.Add($"- {who}: {n.Action}");
            }
        }
        lines.Add("");
        lines.Add($"You can change how {instance} emails you on your profile: {baseUrl}/profile");
        return string.Join("\n", lines);
    }

    private static Dictionary<string, string> Meta(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }
        catch (JsonException) { return []; }
    }
}
