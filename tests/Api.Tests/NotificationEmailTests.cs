using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Email delivery of alerts and notifications (dev-plan 4.3).</summary>
public class NotificationEmailTests
{
    private record RegisteredDto(Guid Id);
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"hi"}]}]}""";
    private const int Off = 0, Immediate = 1, Digest = 2;

    private static async Task<RegisteredDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" })).Content.ReadFromJsonAsync<RegisteredDto>())!;

    private static async Task<HttpClient> InstanceWithEmailAsync(TestAppFactory factory)
    {
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings",
            new { EmailEnabled = true, BaseUrl = "https://wiki.example.com", LoginRateLimitPerMinute = 10_000 })).EnsureSuccessStatusCode();
        return admin;
    }

    private static Task<int> RunAsync(TestAppFactory factory) =>
        factory.Services.GetRequiredService<NotificationEmailService>().RunOnceAsync();

    private static RecordingEmailSender Outbox(TestAppFactory factory) =>
        factory.Services.GetRequiredService<RecordingEmailSender>();

    /// <summary>A watcher on a page, and someone else editing it, produces one notification for the watcher.</summary>
    private static async Task<Guid> WatchedPageEditedAsync(TestAppFactory factory, HttpClient watcher, HttpClient editor)
    {
        var spaceId = await editor.CreateSpaceAsync();
        var page = await (await editor.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Watched", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var pageId = Guid.Parse(page!["id"].ToString()!);
        (await watcher.PostAsync($"/api/pages/{pageId}/watch", null)).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/pages/{pageId}",
            new { Title = "Watched", ContentJson = Doc, ChangeComment = "edit" })).EnsureSuccessStatusCode();
        return pageId;
    }

    [Fact]
    public async Task Security_alerts_reach_administrators_by_email_regardless_of_preference()
    {
        using var factory = new TestAppFactory();
        var admin = await InstanceWithEmailAsync(factory);
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");
        Outbox(factory).Sent.Clear();

        // Promotion is always an alert (3.3); the admin's preference is Off.
        (await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(1, await RunAsync(factory));

        var mail = Assert.Single(Outbox(factory).Sent);
        Assert.Equal("admin@example.com", mail.To);
        Assert.Contains("Security alert", mail.Subject);
        Assert.Contains("Administrator promoted", mail.Text);
        Assert.Contains("https://wiki.example.com/admin/security", mail.Text);

        // Once. The next pass finds nothing.
        Assert.Equal(0, await RunAsync(factory));
    }

    [Fact]
    public async Task Immediate_subscribers_get_each_update_with_a_link()
    {
        using var factory = new TestAppFactory();
        var editor = await InstanceWithEmailAsync(factory);
        var watcher = factory.CreateClient();
        await RegisterAsync(watcher, "watcher@example.com");
        (await watcher.PutAsJsonAsync("/api/auth/me/notifications", new { EmailNotifications = Immediate })).EnsureSuccessStatusCode();
        Outbox(factory).Sent.Clear();

        var pageId = await WatchedPageEditedAsync(factory, watcher, editor);
        await RunAsync(factory);

        var mail = Assert.Single(Outbox(factory).Sent, m => m.To == "watcher@example.com");
        Assert.Contains("updated \"Watched\"", mail.Text);
        Assert.Contains($"/pages/{pageId}", mail.Text);
    }

    [Fact]
    public async Task Digest_subscribers_get_one_message_a_day()
    {
        using var factory = new TestAppFactory();
        var editor = await InstanceWithEmailAsync(factory);
        var watcher = factory.CreateClient();
        await RegisterAsync(watcher, "watcher@example.com");
        (await watcher.PutAsJsonAsync("/api/auth/me/notifications", new { EmailNotifications = Digest })).EnsureSuccessStatusCode();
        Outbox(factory).Sent.Clear();

        await WatchedPageEditedAsync(factory, watcher, editor);
        await WatchedPageEditedAsync(factory, watcher, editor);
        await RunAsync(factory);

        var digest = Assert.Single(Outbox(factory).Sent, m => m.To == "watcher@example.com");
        Assert.Contains("Daily digest: 2 updates", digest.Subject);

        // More activity the same day waits for tomorrow's digest.
        await WatchedPageEditedAsync(factory, watcher, editor);
        await RunAsync(factory);
        Assert.Single(Outbox(factory).Sent, m => m.To == "watcher@example.com");
    }

    [Fact]
    public async Task A_page_restricted_before_the_email_goes_is_left_out_of_it()
    {
        using var factory = new TestAppFactory();
        var admin = await InstanceWithEmailAsync(factory);
        var watcher = factory.CreateClient();
        await RegisterAsync(watcher, "watcher@example.com");
        (await watcher.PutAsJsonAsync("/api/auth/me/notifications", new { EmailNotifications = Immediate })).EnsureSuccessStatusCode();
        var editor = factory.CreateClient();
        var editorId = (await RegisterAsync(editor, "editor@example.com")).Id;
        Outbox(factory).Sent.Clear();

        // Queued while the watcher could read the page, then restricted before the mail went.
        var pageId = await WatchedPageEditedAsync(factory, watcher, editor);
        (await editor.PutAsJsonAsync($"/api/pages/{pageId}",
            new { Title = "Secret title", ContentJson = Doc, ChangeComment = "rename" })).EnsureSuccessStatusCode();
        (await editor.PostAsJsonAsync($"/api/pages/{pageId}/restrictions",
            new { PrincipalType = 0, PrincipalId = editorId, Operation = 0 })).EnsureSuccessStatusCode();

        await RunAsync(factory);
        Assert.DoesNotContain(Outbox(factory).Sent, m => m.To == "watcher@example.com");
        // Retired, not left waiting: a later pass sends nothing either.
        await RunAsync(factory);
        Assert.DoesNotContain(Outbox(factory).Sent, m => m.To == "watcher@example.com");
    }

    [Fact]
    public async Task Off_means_off_and_the_in_app_copy_stays()
    {
        using var factory = new TestAppFactory();
        var editor = await InstanceWithEmailAsync(factory);
        var watcher = factory.CreateClient();
        await RegisterAsync(watcher, "watcher@example.com"); // default: Off
        Outbox(factory).Sent.Clear();

        await WatchedPageEditedAsync(factory, watcher, editor);
        await RunAsync(factory);

        Assert.DoesNotContain(Outbox(factory).Sent, m => m.To == "watcher@example.com");
        var bell = await watcher.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/notifications");
        Assert.NotEmpty(bell!);
    }

    [Fact]
    public async Task With_email_off_the_outbox_is_left_untouched()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        var member = await RegisterAsync(factory.CreateClient(), "member@example.com");
        await admin.PutAsJsonAsync($"/api/admin/users/{member.Id}/role", new { Role = 1 });

        Assert.Equal(0, await RunAsync(factory));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Notifications.AnyAsync(n => n.EmailedAt == null));
    }

    // t2-013: a notification whose email failed during a mail outage was
    // marked sent and never tried again, and nobody was told.
    [Fact]
    public async Task A_mail_outage_keeps_notifications_waiting_and_tells_administrators()
    {
        using var factory = new TestAppFactory();
        var editor = await InstanceWithEmailAsync(factory);
        var watcher = factory.CreateClient();
        await RegisterAsync(watcher, "watcher@example.com");
        (await watcher.PutAsJsonAsync("/api/auth/me/notifications", new { EmailNotifications = Immediate })).EnsureSuccessStatusCode();
        Outbox(factory).Sent.Clear();

        await WatchedPageEditedAsync(factory, watcher, editor);
        Outbox(factory).Fail = true;
        Assert.Equal(0, await RunAsync(factory));

        var service = factory.Services.GetRequiredService<NotificationEmailService>();
        Assert.NotNull(service.RetryAfter);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Notifications.AnyAsync(n => n.User!.Email == "watcher@example.com" && n.EmailedAt == null));
            Assert.True(await db.SecurityAlerts.AnyAsync(a => a.Kind == "mail.send_failed"));
        }

        // The server answers again: the waiting email goes, and so does the
        // administrator's alert about the outage.
        Outbox(factory).Fail = false;
        await RunAsync(factory);
        Assert.Single(Outbox(factory).Sent, m => m.To == "watcher@example.com");
        Assert.Contains(Outbox(factory).Sent, m => m.To == "admin@example.com" && m.Text.Contains("Email is not getting through"));
        Assert.Null(service.RetryAfter);

        // Once: the next pass sends nothing more.
        await RunAsync(factory);
        Assert.Single(Outbox(factory).Sent, m => m.To == "watcher@example.com");
    }

    [Fact]
    public async Task A_recipient_refused_for_good_is_not_tried_again()
    {
        using var factory = new TestAppFactory();
        var editor = await InstanceWithEmailAsync(factory);
        var watcher = factory.CreateClient();
        await RegisterAsync(watcher, "watcher@example.com");
        (await watcher.PutAsJsonAsync("/api/auth/me/notifications", new { EmailNotifications = Immediate })).EnsureSuccessStatusCode();

        await WatchedPageEditedAsync(factory, watcher, editor);
        Outbox(factory).FailPermanently = true;
        await RunAsync(factory);
        Assert.Equal(1, Outbox(factory).Attempts.Count(m => m.To == "watcher@example.com"));

        Outbox(factory).FailPermanently = false;
        await RunAsync(factory);
        Assert.Equal(1, Outbox(factory).Attempts.Count(m => m.To == "watcher@example.com"));
        Assert.Null(factory.Services.GetRequiredService<NotificationEmailService>().RetryAfter);
    }

    [Fact]
    public void Retries_wait_longer_each_time_up_to_half_an_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), NotificationEmailService.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(2), NotificationEmailService.RetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(16), NotificationEmailService.RetryDelay(5));
        Assert.Equal(TimeSpan.FromMinutes(30), NotificationEmailService.RetryDelay(6));
        Assert.Equal(TimeSpan.FromMinutes(30), NotificationEmailService.RetryDelay(500));
    }

    // t2-013: a reset link queued during an outage was lost with only a log line.
    [Fact]
    public async Task A_queued_email_is_tried_again_after_a_failure()
    {
        using var factory = new TestAppFactory();
        await InstanceWithEmailAsync(factory);
        var outbox = Outbox(factory);
        outbox.Sent.Clear();
        outbox.Fail = true;

        var queue = ActivatorUtilities.CreateInstance<EmailQueue>(factory.Services);
        queue.RetryUnit = TimeSpan.FromMilliseconds(100);
        await queue.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(new EmailMessage("reset@example.com", "Reset your password", "link"));
            for (var i = 0; i < 50 && outbox.Attempts.Count(m => m.To == "reset@example.com") < 2; i++) await Task.Delay(50);
            Assert.True(outbox.Attempts.Count(m => m.To == "reset@example.com") >= 2);
            Assert.Empty(outbox.Sent);

            outbox.Fail = false;
            for (var i = 0; i < 100 && outbox.Sent.IsEmpty; i++) await Task.Delay(50);
            Assert.Single(outbox.Sent, m => m.To == "reset@example.com");
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().SecurityAlerts
            .AnyAsync(a => a.Kind == "mail.send_failed"));
    }
}
