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
        Assert.Contains("admin.promoted", mail.Text);
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
}
