using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OtpNet;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Deleting a space (dev-plan 11.3). The interesting part is not the cascade
/// but the two gates in front of it: an instance right rather than a space
/// permission, and a password checked inside this very request, because the
/// ordinary sudo window is not a strong enough answer to "you mean it".
/// </summary>
public class SpaceDeleteTests
{
    private record UserDto(Guid Id, string Email, string DisplayName, int Role);
    private record SpaceDto(Guid Id, string Key, string Name, bool Archived);
    private record PageDto(Guid Id, Guid SpaceId, Guid? ParentPageId, string Title);
    private record AttachmentDto(Guid Id, Guid PageId, string Filename, string ContentType, long Size);
    private record PreviewDto(string Key, string Name, int Pages, int Attachments, long Bytes, bool IsPublic);
    private record TemplateDto(Guid Id, Guid? SpaceId, string Name);
    private record TotpSetupDto(string Secret, string OtpauthUri);

    private static string CodeFor(string base32, int stepOffset = 0) =>
        new Totp(Base32Encoding.ToBytes(base32)).ComputeTotp(DateTime.UtcNow.AddSeconds(30 * stepOffset));

    private const string Doc = """{"type":"doc","content":[]}""";
    private const string Password = "supersecret";
    private const int Admin = 1;

    private static async Task<UserDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password }))
            .Content.ReadFromJsonAsync<UserDto>())!;

    private static async Task<T> InScopeAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>The owner, plus an administrator: the caller who holds the right by default.</summary>
    private static async Task<(TestAppFactory Factory, HttpClient Owner, HttpClient Admin)> InstanceAsync()
    {
        var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Admin }))
            .EnsureSuccessStatusCode();
        return (factory, owner, adminClient);
    }

    private static async Task<SpaceDto> CreateSpaceAsync(HttpClient client, string key) =>
        (await (await client.PostAsJsonAsync("/api/spaces",
            new { Key = key, Name = $"The {key} space", Description = (string?)null }))
            .Content.ReadFromJsonAsync<SpaceDto>())!;

    private static async Task<PageDto> CreatePageAsync(HttpClient client, Guid spaceId, string title) =>
        (await (await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDto>())!;

    private static async Task<AttachmentDto> AttachAsync(HttpClient client, Guid pageId, string name, string text)
    {
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var res = await client.PostAsync($"/api/pages/{pageId}/attachments",
            new MultipartFormDataContent { { bytes, "file", name } });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AttachmentDto>())!;
    }

    private static Task<HttpResponseMessage> DeleteAsync(
        HttpClient client, string key, string? confirmKey = null, string? password = Password, string? code = null) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/spaces/{key}")
        {
            Content = JsonContent.Create(new { ConfirmKey = confirmKey ?? key, Password = password, Code = code }),
        });

    // --- Who may.

    [Fact]
    public async Task A_member_cannot_delete_a_space_even_the_one_they_created()
    {
        var (factory, owner, _) = await InstanceAsync();
        using var _f = factory;
        var memberClient = factory.CreateClient();
        await RegisterAsync(memberClient, "member@example.com");

        // Their own space: creating one makes you its administrator, which is
        // not the same as being allowed to destroy it.
        var space = await CreateSpaceAsync(memberClient, "MINE");

        var res = await DeleteAsync(memberClient, space.Key);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.True(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Key == "MINE")));

        // Archiving is the reversible thing they can do instead.
        (await memberClient.PostAsJsonAsync($"/api/spaces/{space.Key}/archive", new { }))
            .EnsureSuccessStatusCode();
        _ = owner;
    }

    [Fact]
    public async Task An_administrator_deletes_a_space_and_everything_under_it()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "DOOMED");

        var kept = await CreatePageAsync(owner, space.Id, "Kept");
        var trashed = await CreatePageAsync(owner, space.Id, "Trashed");
        (await owner.DeleteAsync($"/api/pages/{trashed.Id}")).EnsureSuccessStatusCode();
        // A draft: created but never published, so it is invisible to every
        // normal query and would be easy to leave behind.
        var draft = (await (await owner.PostAsJsonAsync("/api/pages/draft",
            new { SpaceId = space.Id, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<PageDto>())!;
        var file = await AttachAsync(owner, kept.Id, "notes.txt", "hello world");

        var res = await DeleteAsync(admin, "DOOMED");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.False(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Id == space.Id)));
        Assert.False(await InScopeAsync(factory, db =>
            db.Pages.IgnoreQueryFilters().AnyAsync(p => p.SpaceId == space.Id)));
        foreach (var id in new[] { kept.Id, trashed.Id, draft.Id })
            Assert.False(await InScopeAsync(factory, db =>
                db.Pages.IgnoreQueryFilters().AnyAsync(p => p.Id == id)));
        Assert.False(await InScopeAsync(factory, db => db.Attachments.AnyAsync(a => a.Id == file.Id)));
        Assert.False(await InScopeAsync(factory, db => db.PageVersions.AnyAsync(v => v.PageId == kept.Id)));
    }

    [Fact]
    public async Task The_audit_entry_keeps_what_the_rows_no_longer_can()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "RECORD");
        var page = await CreatePageAsync(owner, space.Id, "One");
        await AttachAsync(owner, page.Id, "a.txt", "12345");

        (await DeleteAsync(admin, "RECORD")).EnsureSuccessStatusCode();

        var entry = await InScopeAsync(factory, db => db.AuditLogs
            .Where(a => a.Action == "space.deleted").OrderByDescending(a => a.Sequence).FirstAsync());
        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal("RECORD", root.GetProperty("Key").GetString());
        Assert.Equal("The RECORD space", root.GetProperty("Name").GetString());
        Assert.Equal(1, root.GetProperty("Pages").GetInt32());
        Assert.Equal(1, root.GetProperty("Attachments").GetInt32());
        Assert.Equal(5, root.GetProperty("Bytes").GetInt64());
        Assert.False(root.GetProperty("WasPublic").GetBoolean());
    }

    [Fact]
    public async Task Deleting_a_space_raises_a_critical_alert()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        await CreateSpaceAsync(owner, "LOUD");

        (await DeleteAsync(admin, "LOUD")).EnsureSuccessStatusCode();

        var alert = await InScopeAsync(factory, db => db.SecurityAlerts
            .Include(a => a.Event)
            .FirstOrDefaultAsync(a => a.Event!.Kind == "space.deleted"));
        Assert.NotNull(alert);
        Assert.Equal(SecuritySeverity.Critical, alert!.Event!.Severity);
    }

    // --- The two answers it asks for.

    [Fact]
    public async Task The_wrong_key_is_refused_and_nothing_is_touched()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "SAFE");
        await CreatePageAsync(owner, space.Id, "Still here");

        // The near miss that matters: the right space, the wrong case.
        var res = await DeleteAsync(admin, "SAFE", confirmKey: "safe");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        Assert.True(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Id == space.Id)));
        Assert.Equal(1, await InScopeAsync(factory, db =>
            db.Pages.IgnoreQueryFilters().CountAsync(p => p.SpaceId == space.Id)));
    }

    [Fact]
    public async Task The_wrong_password_is_refused_and_counts_toward_lockout()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "GUARDED");

        var res = await DeleteAsync(admin, "GUARDED", password: "not-the-password");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.True(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Id == space.Id)));

        // A guess here is a guess at the password, so it is recorded like one.
        var failures = await InScopeAsync(factory, db => db.Users
            .Where(u => u.Email == "admin@example.com").Select(u => u.FailedLoginCount).FirstAsync());
        Assert.Equal(1, failures);
    }

    [Fact]
    public async Task A_recent_sign_in_is_not_enough_on_its_own()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        await CreateSpaceAsync(owner, "FRESH");

        // The administrator signed in moments ago, so the ordinary sudo window
        // is wide open. It does not stand in for the password here.
        var res = await DeleteAsync(admin, "FRESH", password: null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.True(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Key == "FRESH")));
    }

    [Fact]
    public async Task An_account_without_a_password_confirms_with_a_code()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        await CreateSpaceAsync(owner, "SSO");

        // Enrol two-factor through the real endpoints, then take the password
        // away: what is left is the shape OIDC provisioning leaves an account
        // in, where a code is the only answer available.
        var setup = await (await admin.PostAsJsonAsync("/api/auth/me/totp/setup", new { }))
            .Content.ReadFromJsonAsync<TotpSetupDto>();
        (await admin.PostAsJsonAsync("/api/auth/me/totp/enable", new { Code = CodeFor(setup!.Secret) }))
            .EnsureSuccessStatusCode();
        await InScopeAsync(factory, async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Email == "admin@example.com");
            user.PasswordHash = null;
            return await db.SaveChangesAsync();
        });

        // The next step, not the one enrolment just consumed: a code that
        // matches a time step already used is refused, which is the point of it.
        var res = await DeleteAsync(admin, "SSO", password: null, code: CodeFor(setup.Secret, 1));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.False(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Key == "SSO")));
    }

    // --- What else goes with it.

    [Fact]
    public async Task Attachment_files_are_removed_from_storage()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "FILES");
        var page = await CreatePageAsync(owner, space.Id, "With a file");
        var file = await AttachAsync(owner, page.Id, "notes.txt", "hello world");

        var storageKey = await InScopeAsync(factory, db => db.Attachments
            .Where(a => a.Id == file.Id).Select(a => a.StorageKey).FirstAsync());
        var storage = factory.Services.GetRequiredService<IAttachmentStorage>();
        Assert.NotNull(storage.OpenRead(storageKey));

        (await DeleteAsync(admin, "FILES")).EnsureSuccessStatusCode();

        Assert.Null(storage.OpenRead(storageKey));
    }

    [Fact]
    public async Task Watches_on_the_space_and_its_pages_are_gone()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "WATCHED");
        var page = await CreatePageAsync(owner, space.Id, "Followed");
        var other = await CreateSpaceAsync(owner, "KEPT");

        (await owner.PostAsJsonAsync($"/api/spaces/{space.Key}/watch", new { })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/pages/{page.Id}/watch", new { })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/spaces/{other.Key}/watch", new { })).EnsureSuccessStatusCode();

        (await DeleteAsync(admin, "WATCHED")).EnsureSuccessStatusCode();

        Assert.False(await InScopeAsync(factory, db => db.Watches.AnyAsync(w => w.TargetId == space.Id)));
        Assert.False(await InScopeAsync(factory, db => db.Watches.AnyAsync(w => w.TargetId == page.Id)));
        // A watch on an unrelated space is somebody's own setting, not debris.
        Assert.True(await InScopeAsync(factory, db => db.Watches.AnyAsync(w => w.TargetId == other.Id)));
    }

    [Fact]
    public async Task A_template_scoped_to_the_space_goes_and_an_instance_wide_one_stays()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "TEMPLATED");

        var scoped = (await (await owner.PostAsJsonAsync("/api/templates",
            new { SpaceId = space.Id, Name = "Meeting notes", Description = (string?)null, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<TemplateDto>())!;
        var global = (await (await owner.PostAsJsonAsync("/api/templates",
            new { SpaceId = (Guid?)null, Name = "Anything", Description = (string?)null, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<TemplateDto>())!;

        (await DeleteAsync(admin, "TEMPLATED")).EnsureSuccessStatusCode();

        Assert.False(await InScopeAsync(factory, db => db.PageTemplates.AnyAsync(t => t.Id == scoped.Id)));
        Assert.True(await InScopeAsync(factory, db => db.PageTemplates.AnyAsync(t => t.Id == global.Id)));
    }

    [Fact]
    public async Task Collaboration_documents_for_the_deleted_pages_go_too()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "LIVE");
        var page = await CreatePageAsync(owner, space.Id, "Being edited");

        // The sidecar writes these, keyed by the page id as a string; there is
        // no foreign key to cascade from, so the endpoint has to do it.
        await InScopeAsync(factory, async db =>
        {
            db.CollabDocuments.Add(new CollabDocument
            {
                DocumentName = page.Id.ToString(), State = [1, 2, 3], UpdatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        (await DeleteAsync(admin, "LIVE")).EnsureSuccessStatusCode();

        Assert.False(await InScopeAsync(factory, db =>
            db.CollabDocuments.AnyAsync(d => d.DocumentName == page.Id.ToString())));
    }

    [Fact]
    public async Task An_archived_space_can_be_deleted_without_unarchiving_it()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "OLD");
        (await owner.PostAsJsonAsync($"/api/spaces/{space.Key}/archive", new { })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, "OLD")).StatusCode);
        Assert.False(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Id == space.Id)));
    }

    // --- The right, and its limits.

    [Fact]
    public async Task Taking_the_right_away_takes_the_ability_with_it()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        await CreateSpaceAsync(owner, "REVOKED");

        var adminRole = await InScopeAsync(factory, db =>
            db.Roles.FirstAsync(r => r.Tier == UserRole.Admin));
        var without = InstancePermissions.DefaultsFor(UserRole.Admin)
            .Where(k => k != InstancePermissions.SpacesDelete).ToArray();
        (await owner.PutAsJsonAsync($"/api/admin/roles/{adminRole.Id}/permissions", new { Permissions = without }))
            .EnsureSuccessStatusCode();

        var res = await DeleteAsync(admin, "REVOKED");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.True(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Key == "REVOKED")));
    }

    [Fact]
    public async Task The_right_is_not_a_way_into_a_space_you_cannot_see()
    {
        var (factory, owner, admin) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "PRIVATE");

        // Spaces are open until someone says otherwise, so close this one by
        // naming the owner as its only viewer.
        var ownerId = await InScopeAsync(factory, db => db.Users
            .Where(u => u.Email == "owner@example.com").Select(u => u.Id).FirstAsync());
        (await owner.PostAsJsonAsync($"/api/spaces/{space.Key}/permissions",
            new { PrincipalType = 0, PrincipalId = ownerId, Operation = 2 }))
            .EnsureSuccessStatusCode();

        // 404, not 403: a space they cannot see should not be confirmed to exist.
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(admin, "PRIVATE")).StatusCode);
        Assert.True(await InScopeAsync(factory, db => db.Spaces.AnyAsync(s => s.Id == space.Id)));

        // The owner, who can see it, still can.
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(owner, "PRIVATE")).StatusCode);
    }

    // --- The preview the dialog is built from.

    [Fact]
    public async Task The_preview_counts_drafts_and_trashed_pages_because_they_go_too()
    {
        var (factory, owner, _) = await InstanceAsync();
        using var _f = factory;
        var space = await CreateSpaceAsync(owner, "COUNTED");
        var page = await CreatePageAsync(owner, space.Id, "Visible");
        var trashed = await CreatePageAsync(owner, space.Id, "Trashed");
        (await owner.DeleteAsync($"/api/pages/{trashed.Id}")).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/pages/draft", new { SpaceId = space.Id, ParentPageId = (Guid?)null }))
            .EnsureSuccessStatusCode();
        await AttachAsync(owner, page.Id, "a.txt", "12345");

        var preview = await owner.GetFromJsonAsync<PreviewDto>($"/api/spaces/{space.Key}/deletion-preview");

        Assert.Equal(3, preview!.Pages);
        Assert.Equal(1, preview.Attachments);
        Assert.Equal(5, preview.Bytes);
    }
}
