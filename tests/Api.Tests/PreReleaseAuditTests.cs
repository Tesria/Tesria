using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The findings listed under dev-plan 14.1, each fixed and held here: API
/// token expiry and what a token may not do, render tokens held to their
/// page or space, drafts and trash kept from readers, refusals that no longer
/// reveal hidden spaces, email addresses, administrators acting on each
/// other, instance-wide templates, the two-factor rule on every admin check,
/// and the audit list.
/// </summary>
public class PreReleaseAuditTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"secret pineapple"}]}]}""";
    private const int UserPrincipal = 0, View = 0, Admin = 1;

    private record TokenDto(Guid Id, string Name, DateTimeOffset? ExpiresAt, string? Token);
    private record SpaceDto(Guid Id, string Key, string Name);
    private record PageDto(Guid Id, Guid SpaceId, string Title);
    private record DirectoryDto(Guid Id, string? Email, string DisplayName);
    private record UserDto(Guid Id, string Email, string DisplayName);
    private record RoleDto(Guid Id, string? Key, int Tier, List<string> Permissions);
    private record MatrixDto(List<RoleDto> Roles);

    private static async Task<UserDto> RegisterAsync(HttpClient c, string email)
    {
        var res = await c.PostAsJsonAsync("/api/auth/register", new { Email = email, DisplayName = email.Split('@')[0], Password = "supersecret" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private static HttpClient Bearer(TestAppFactory f, string token)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<PageDto> NewPageAsync(HttpClient c, Guid spaceId, string title = "Page") =>
        (await (await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDto>())!;

    /// <summary>Makes a space private to its creator, the way the permissions tab does.</summary>
    private static async Task CloseAsync(HttpClient c, string key, Guid userId) =>
        (await c.PostAsJsonAsync($"/api/spaces/{key}/permissions", new { PrincipalType = UserPrincipal, PrincipalId = userId, Operation = View }))
            .EnsureSuccessStatusCode();

    // -- API tokens -----------------------------------------------------------

    [Fact]
    public async Task A_token_lasts_90_days_unless_chosen_and_stops_when_it_expires()
    {
        using var f = new TestAppFactory();
        var c = f.CreateClient();
        await c.RegisterAndSignInAsync();

        var standard = await (await c.PostAsJsonAsync("/api/api-tokens", new { Name = "ci" })).Content.ReadFromJsonAsync<TokenDto>();
        Assert.InRange(standard!.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddDays(89), DateTimeOffset.UtcNow.AddDays(91));
        var forever = await (await c.PostAsJsonAsync("/api/api-tokens", new { Name = "kiosk", ExpiresInDays = 0 })).Content.ReadFromJsonAsync<TokenDto>();
        Assert.Null(forever!.ExpiresAt);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/api-tokens", new { Name = "x", ExpiresInDays = 5000 })).StatusCode);

        // A route that needs someone signed in: an anonymous-friendly one would
        // answer an unknown token as it answers nobody.
        var bearer = Bearer(f, standard.Token!);
        (await bearer.GetAsync("/api/notifications")).EnsureSuccessStatusCode();

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.ApiTokens.SingleAsync(t => t.Id == standard.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/notifications")).StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_manage_the_account_it_belongs_to()
    {
        using var f = new TestAppFactory();
        var c = f.CreateClient();
        await c.RegisterAndSignInAsync();
        var token = (await (await c.PostAsJsonAsync("/api/api-tokens", new { Name = "full" })).Content.ReadFromJsonAsync<TokenDto>())!.Token!;
        var bearer = Bearer(f, token);

        // Who it is, yes.
        (await bearer.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        // More tokens, the sessions, the profile (which used to hand back a
        // fresh browser session), the password: no.
        foreach (var res in new[]
        {
            await bearer.PostAsJsonAsync("/api/api-tokens", new { Name = "another" }),
            await bearer.DeleteAsync("/api/auth/me/sessions/others"),
            await bearer.GetAsync("/api/auth/me/sessions"),
            await bearer.PutAsJsonAsync("/api/auth/me", new { DisplayName = "Renamed" }),
            await bearer.PutAsJsonAsync("/api/auth/me/password", new { CurrentPassword = "supersecret", NewPassword = "anothersecret1" }),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Contains("token_not_allowed", await res.Content.ReadAsStringAsync());
            Assert.False(res.Headers.Contains("Set-Cookie"));
        }
        // The browser session is untouched.
        (await c.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Owners_are_told_a_week_before_a_token_expires_once()
    {
        using var f = new TestAppFactory();
        var c = f.CreateClient();
        await c.RegisterAndSignInAsync();
        await c.PostAsJsonAsync("/api/api-tokens", new { Name = "nightly", ExpiresInDays = 3 });
        await c.PostAsJsonAsync("/api/api-tokens", new { Name = "later", ExpiresInDays = 30 });

        var notifier = f.Services.GetRequiredService<TokenExpiryNotifier>();
        Assert.Equal(1, await notifier.RunOnceAsync());
        Assert.Equal(0, await notifier.RunOnceAsync());

        var notes = await c.GetStringAsync("/api/notifications");
        Assert.Contains("token.expiring", notes);
        Assert.Contains("nightly", notes);
        Assert.DoesNotContain("later", notes);
    }

    // -- Render tokens --------------------------------------------------------

    private static TestAppFactory RenderApp() =>
        new(new Dictionary<string, string?> { ["Pdf:SharedSecret"] = "a-test-secret-that-is-long-enough-for-hmac" });

    [Fact]
    public async Task A_render_token_reads_its_own_page_and_space_and_nothing_else()
    {
        using var f = RenderApp();
        var c = f.CreateClient();
        var me = await c.RegisterAndSignInAsync();
        var spaceA = await c.CreateSpaceAsync("RENDA");
        var spaceB = await c.CreateSpaceAsync("RENDB");
        var page = await NewPageAsync(c, spaceA, "Mine");
        var sibling = await NewPageAsync(c, spaceA, "Sibling");
        var elsewhere = await NewPageAsync(c, spaceB, "Elsewhere");
        var tokens = f.Services.GetRequiredService<IRenderTokens>();

        var pageToken = Bearer(f, tokens.IssueForPage(page.Id, me));
        (await pageToken.GetAsync($"/api/pages/{page.Id}")).EnsureSuccessStatusCode();
        (await pageToken.GetAsync("/api/spaces/RENDA")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await pageToken.GetAsync($"/api/pages/{sibling.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pageToken.GetAsync("/api/spaces/RENDB")).StatusCode);

        var spaceToken = Bearer(f, tokens.IssueForSpace(spaceA, me));
        (await spaceToken.GetAsync($"/api/pages/{sibling.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await spaceToken.GetAsync($"/api/pages/{elsewhere.Id}")).StatusCode);

        // Everything that is not a capture's business.
        foreach (var path in new[] { "/api/search?q=secret", "/api/notifications", "/api/audit", "/api/auth/me/sessions", "/api/admin/settings" })
            Assert.Equal(HttpStatusCode.Forbidden, (await spaceToken.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await spaceToken.PostAsync("/mcp", new StringContent("{}"))).StatusCode);
    }

    // -- Drafts, trash and the publish shortcut -------------------------------

    [Fact]
    public async Task Publishing_an_already_published_page_does_not_hand_it_to_just_anyone()
    {
        using var f = new TestAppFactory();
        var alice = f.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await alice.CreateSpaceAsync("SECRET");
        var page = await NewPageAsync(alice, space, "Board minutes");
        await CloseAsync(alice, "SECRET", aliceId);

        var bob = f.CreateClient();
        await bob.RegisterAndSignInAsync();
        var res = await bob.PostAsJsonAsync($"/api/pages/{page.Id}/publish", new { Title = "x", ContentJson = Doc });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain("pineapple", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_draft_and_what_hangs_off_it_are_for_its_author_and_editors()
    {
        using var f = new TestAppFactory();
        var alice = f.CreateClient();
        await alice.RegisterAndSignInAsync();
        var space = await alice.CreateSpaceAsync("DRAFTS");
        var draft = await (await alice.PostAsJsonAsync("/api/pages/draft", new { SpaceId = space, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<PageDto>();
        (await alice.GetAsync($"/api/pages/{draft!.Id}/versions")).EnsureSuccessStatusCode();

        // Bob can see the space, but not edit it: the draft is not his.
        var bob = f.CreateClient();
        var bobId = await bob.RegisterAndSignInAsync();
        var aliceId = (await alice.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id;
        (await alice.PostAsJsonAsync("/api/spaces/DRAFTS/permissions", new { PrincipalType = UserPrincipal, PrincipalId = aliceId, Operation = 2 }))
            .EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync("/api/spaces/DRAFTS/permissions", new { PrincipalType = UserPrincipal, PrincipalId = bobId, Operation = View }))
            .EnsureSuccessStatusCode();
        foreach (var path in new[] { "versions", "versions/1", "attachments", "labels", "comments" })
            Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/pages/{draft.Id}/{path}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/pages/{draft.Id}/comments",
            new { Body = "hi", ParentCommentId = (Guid?)null, AnchorJson = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/pages/{draft.Id}/draft")).StatusCode);
    }

    [Fact]
    public async Task A_trashed_pages_history_is_gone_like_the_page()
    {
        using var f = new TestAppFactory();
        var alice = f.CreateClient();
        await alice.RegisterAndSignInAsync();
        var space = await alice.CreateSpaceAsync("TRASHED");
        var page = await NewPageAsync(alice, space, "Old");
        (await alice.DeleteAsync($"/api/pages/{page.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/pages/{page.Id}/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/pages/{page.Id}/comments")).StatusCode);
        // Restoring still works for those who may.
        (await alice.PostAsync($"/api/pages/{page.Id}/restore", null)).EnsureSuccessStatusCode();
    }

    // -- Hidden spaces answer 404 ---------------------------------------------

    [Fact]
    public async Task A_hidden_space_is_not_found_rather_than_forbidden()
    {
        using var f = new TestAppFactory();
        var alice = f.CreateClient();
        var aliceId = await alice.RegisterAndSignInAsync();
        var space = await alice.CreateSpaceAsync("HIDDEN");
        var page = await NewPageAsync(alice, space);
        await CloseAsync(alice, "HIDDEN", aliceId);
        (await alice.DeleteAsync($"/api/pages/{page.Id}")).EnsureSuccessStatusCode();

        var bob = f.CreateClient();
        await bob.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/spaces/HIDDEN/webhooks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/spaces/HIDDEN/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/pages/{page.Id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/pages/{page.Id}/purge")).StatusCode);
    }

    // -- Email addresses ------------------------------------------------------

    [Fact]
    public async Task Only_those_who_may_see_the_user_list_see_email_addresses()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        var member = f.CreateClient();
        var me = await RegisterAsync(member, "member@example.com");

        var seen = await member.GetFromJsonAsync<List<DirectoryDto>>("/api/users");
        Assert.Null(seen!.Single(u => u.DisplayName == "owner").Email);
        Assert.Equal("member@example.com", seen.Single(u => u.Id == me.Id).Email);

        var byAdmin = await owner.GetFromJsonAsync<List<DirectoryDto>>("/api/users");
        Assert.Equal("member@example.com", byAdmin!.Single(u => u.Id == me.Id).Email);
    }

    // -- Administrators acting on each other ----------------------------------

    [Fact]
    public async Task Administrators_reach_each_other_only_with_the_right_the_owner_gives_and_never_the_owner()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        var ownerUser = await RegisterAsync(owner, "owner@example.com");
        var adminA = f.CreateClient();
        var a = await RegisterAsync(adminA, "a@example.com");
        var adminB = f.CreateClient();
        var b = await RegisterAsync(adminB, "b@example.com");
        foreach (var id in new[] { a.Id, b.Id })
            (await owner.PutAsJsonAsync($"/api/admin/users/{id}/role", new { Role = Admin })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await adminA.PostAsync($"/api/admin/users/{b.Id}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminA.PostAsync($"/api/admin/users/{b.Id}/revoke-sessions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminA.PutAsJsonAsync($"/api/admin/users/{b.Id}/status", new { Status = 1 })).StatusCode);

        // The owner grants the right to the administrator role.
        var matrix = await owner.GetFromJsonAsync<MatrixDto>("/api/admin/roles");
        var adminRole = matrix!.Roles.Single(r => r.Key == "admin");
        (await owner.PutAsJsonAsync($"/api/admin/roles/{adminRole.Id}/permissions",
            new { Permissions = adminRole.Permissions.Append("users.manage_admins").ToList() })).EnsureSuccessStatusCode();

        (await adminA.PostAsync($"/api/admin/users/{b.Id}/reset-password", null)).EnsureSuccessStatusCode();
        // Never the owner.
        Assert.Equal(HttpStatusCode.Forbidden, (await adminA.PostAsync($"/api/admin/users/{ownerUser.Id}/reset-password", null)).StatusCode);
    }

    // -- Instance-wide templates ----------------------------------------------

    [Fact]
    public async Task Instance_wide_templates_take_their_own_right()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        var member = f.CreateClient();
        await RegisterAsync(member, "member@example.com");
        var space = await member.CreateSpaceAsync("TPL");

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/templates",
            new { SpaceId = (Guid?)null, Name = "Everywhere", ContentJson = Doc })).StatusCode);
        (await member.PostAsJsonAsync("/api/templates", new { SpaceId = space, Name = "Here", ContentJson = Doc })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/templates", new { SpaceId = (Guid?)null, Name = "Everywhere", ContentJson = Doc })).EnsureSuccessStatusCode();
    }

    // -- Two-factor for administrators, on every admin check ------------------

    [Fact]
    public async Task An_administrator_without_required_two_factor_cannot_use_handler_checked_routes()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        var adminClient = f.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Admin })).EnsureSuccessStatusCode();
        (await adminClient.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Before" })).EnsureSuccessStatusCode();

        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Tesria.Api.Infrastructure.Settings.ISiteSettingsService>()
                .UpdateAsync(s => s.RequireTotpForAdmins = true, null);

        // The settings form checks its rights itself; it used to let this through.
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "After" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync("/api/admin/settings", new { RequireTotpForAdmins = false })).StatusCode);
        // What they hold is still reported, so the admin screens can show the setup step.
        Assert.Contains("settings.instance", await adminClient.GetStringAsync("/api/auth/me"));
    }

    // -- The audit list -------------------------------------------------------

    [Fact]
    public async Task The_audit_list_fills_its_page_with_entries_the_caller_may_see()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        var admin = f.CreateClient();
        var adminUser = await RegisterAsync(admin, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminUser.Id}/role", new { Role = Admin })).EnsureSuccessStatusCode();

        // Old entries the administrator may see, then many the administrator may not.
        var visible = await admin.CreateSpaceAsync("SEEN");
        for (var i = 0; i < 5; i++) await NewPageAsync(admin, visible, $"Seen {i}");
        var ownerId = (await owner.GetFromJsonAsync<UserDto>("/api/auth/me"))!.Id;
        var hidden = await owner.CreateSpaceAsync("UNSEEN");
        await CloseAsync(owner, "UNSEEN", ownerId);
        for (var i = 0; i < 12; i++) await NewPageAsync(owner, hidden, $"Unseen {i}");

        var entries = JsonDocument.Parse(await admin.GetStringAsync("/api/audit?take=5")).RootElement;
        Assert.Equal(5, entries.GetArrayLength());
    }
}
