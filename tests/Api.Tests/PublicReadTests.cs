using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The leak matrix for public read mode (dev-plan 5.1/5.2): every endpoint
/// opened to anonymous readers × everything they must not see. Written
/// before the routes were opened; the routes are open only as far as this
/// is green.
/// </summary>
public class PublicReadTests
{
    private record RegisteredDto(Guid Id);
    private record SpaceDto(Guid Id, string Key, string Name, bool IsPublic, bool PublicComments);
    private record AdminSpaceDto(Guid Id, string Key, bool IsPublic, bool PublicComments, int PageCount, int AttachmentCount);
    private record PageDto(Guid Id, string Title);
    private record TreeNode(Guid Id, string Title, List<TreeNode> Children);
    private record SearchHit(Guid PageId, string Title);
    private record AttachmentDto(Guid Id);
    private record AlertDto(string Kind);

    private const int User = 0, View = 0;
    private static string Doc(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    /// <summary>The world the matrix is checked against.</summary>
    private sealed class World
    {
        public required TestAppFactory Factory;
        public required HttpClient Admin;
        public required HttpClient Anon;
        public Guid AdminId, PublicSpaceId, PrivateSpaceId;
        public Guid NormalPage, RestrictedPage, ChildOfRestricted, DraftPage, TrashedPage, PrivatePage;
        public Guid NormalAttachment, RestrictedAttachment;
    }

    private static async Task<World> BuildAsync(bool publish = true, bool publicComments = false)
    {
        var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        var registered = await (await admin.PostAsJsonAsync("/api/auth/register",
            new { Email = "admin@example.com", DisplayName = "Admin", Password = "supersecret" }))
            .Content.ReadFromJsonAsync<RegisteredDto>();
        (await admin.PutAsJsonAsync("/api/admin/settings",
            new { AllowPublicSpaces = true, AnonymousRateLimitPerMinute = 100_000, LoginRateLimitPerMinute = 10_000 }))
            .EnsureSuccessStatusCode();

        var w = new World { Factory = factory, Admin = admin, Anon = factory.CreateClient(), AdminId = registered!.Id };
        w.PublicSpaceId = await admin.CreateSpaceAsync("PUB");
        w.PrivateSpaceId = await admin.CreateSpaceAsync("PRIV");

        w.NormalPage = await PageAsync(admin, w.PublicSpaceId, "Public page", "pineapple");
        w.RestrictedPage = await PageAsync(admin, w.PublicSpaceId, "Restricted page", "pineapple secret");
        (await admin.PostAsJsonAsync($"/api/pages/{w.RestrictedPage}/restrictions",
            new { PrincipalType = User, PrincipalId = w.AdminId, Operation = View })).EnsureSuccessStatusCode();
        w.ChildOfRestricted = await PageAsync(admin, w.PublicSpaceId, "Child of restricted", "pineapple child", w.RestrictedPage);
        w.DraftPage = (await (await admin.PostAsJsonAsync("/api/pages/draft", new { SpaceId = w.PublicSpaceId, ParentPageId = (Guid?)null }))
            .Content.ReadFromJsonAsync<PageDto>())!.Id;
        w.TrashedPage = await PageAsync(admin, w.PublicSpaceId, "Trashed page", "pineapple trashed");
        (await admin.DeleteAsync($"/api/pages/{w.TrashedPage}")).EnsureSuccessStatusCode();
        w.PrivatePage = await PageAsync(admin, w.PrivateSpaceId, "Private page", "pineapple private");

        w.NormalAttachment = await UploadAsync(admin, w.NormalPage);
        w.RestrictedAttachment = await UploadAsync(admin, w.RestrictedPage);
        (await admin.PostAsJsonAsync($"/api/pages/{w.NormalPage}/comments", new { Body = "a comment", ParentCommentId = (Guid?)null, AnchorJson = (string?)null }))
            .EnsureSuccessStatusCode();

        if (publish)
            (await admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = true, PublicComments = publicComments }))
                .EnsureSuccessStatusCode();
        return w;
    }

    private static async Task<Guid> PageAsync(HttpClient c, Guid spaceId, string title, string text, Guid? parent = null) =>
        (await (await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = parent, Title = title, ContentJson = Doc(text) }))
            .Content.ReadFromJsonAsync<PageDto>())!.Id;

    private static async Task<Guid> UploadAsync(HttpClient c, Guid pageId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "note.txt");
        return (await (await c.PostAsync($"/api/pages/{pageId}/attachments", form)).Content.ReadFromJsonAsync<AttachmentDto>())!.Id;
    }

    private static async Task<HttpStatusCode> Get(HttpClient c, string path) => (await c.GetAsync(path)).StatusCode;

    // ---- the matrix ---------------------------------------------------------

    [Fact]
    public async Task Anonymous_sees_only_the_public_space_and_its_unrestricted_current_pages()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;
        var anon = w.Anon;

        var spaces = await anon.GetFromJsonAsync<List<SpaceDto>>("/api/spaces");
        Assert.Equal("PUB", Assert.Single(spaces!).Key);
        Assert.Equal(HttpStatusCode.OK, await Get(anon, "/api/spaces/PUB"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, "/api/spaces/PRIV")); // masked, never 403

        var tree = await anon.GetFromJsonAsync<List<TreeNode>>($"/api/pages/tree?spaceId={w.PublicSpaceId}");
        var titles = Flatten(tree!).Select(t => t.Title).ToList();
        Assert.Contains("Public page", titles);
        Assert.DoesNotContain("Restricted page", titles);
        Assert.DoesNotContain("Child of restricted", titles); // inherited
        Assert.DoesNotContain("Trashed page", titles);
        Assert.DoesNotContain(titles, t => t.Length == 0); // no draft placeholder
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/tree?spaceId={w.PrivateSpaceId}"));

        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/pages/{w.NormalPage}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.RestrictedPage}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.ChildOfRestricted}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.DraftPage}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.TrashedPage}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.PrivatePage}"));
    }

    [Fact]
    public async Task Attachments_labels_export_and_search_follow_the_page()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;
        var anon = w.Anon;

        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/pages/{w.NormalPage}/attachments"));
        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/attachments/{w.NormalAttachment}/download"));
        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/attachments/{w.NormalAttachment}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.RestrictedPage}/attachments"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/attachments/{w.RestrictedAttachment}/download"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/attachments/{w.RestrictedAttachment}"));

        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/pages/{w.NormalPage}/labels"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.RestrictedPage}/labels"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/labels")); // across spaces: closed
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/labels/x/pages"));

        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/pages/{w.NormalPage}/export?format=markdown"));
        Assert.Equal(HttpStatusCode.OK, await Get(anon, $"/api/pages/{w.NormalPage}/export?format=markdown"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.RestrictedPage}/export"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(anon, $"/api/pages/{w.PrivatePage}/export"));

        // "pineapple" is in every page; only the public, unrestricted one comes back.
        var hits = await anon.GetFromJsonAsync<List<SearchHit>>("/api/search?q=pineapple");
        Assert.Equal(w.NormalPage, Assert.Single(hits!).PageId);
    }

    [Fact]
    public async Task History_drafts_directory_and_everything_that_writes_stay_closed()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;
        var anon = w.Anon;

        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, $"/api/pages/{w.NormalPage}/versions"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, $"/api/pages/{w.NormalPage}/versions/1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, $"/api/pages/trash?spaceId={w.PublicSpaceId}"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, $"/api/pages/{w.NormalPage}/collab-token"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, $"/api/pages/{w.NormalPage}/watch"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/users"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/groups"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/notifications"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, $"/api/media/avatars/{w.AdminId}"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/spaces/PUB/permissions"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Get(anon, "/api/spaces/PUB/webhooks"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/pages",
            new { SpaceId = w.PublicSpaceId, ParentPageId = (Guid?)null, Title = "Vandal", ContentJson = Doc("x") })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync($"/api/pages/{w.NormalPage}",
            new { Title = "Vandal", ContentJson = Doc("x"), ChangeComment = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.DeleteAsync($"/api/pages/{w.NormalPage}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/pages/{w.NormalPage}/comments",
            new { Body = "spam", ParentCommentId = (Guid?)null, AnchorJson = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/api/pages/{w.NormalPage}/watch", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/spaces",
            new { Key = "NEW", Name = "New", Description = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Comments_are_visible_only_where_the_space_allows()
    {
        var closed = await BuildAsync(publicComments: false);
        using (closed.Factory)
            Assert.Equal(HttpStatusCode.Unauthorized, await Get(closed.Anon, $"/api/pages/{closed.NormalPage}/comments"));

        var open = await BuildAsync(publicComments: true);
        using (open.Factory)
        {
            Assert.Equal(HttpStatusCode.OK, await Get(open.Anon, $"/api/pages/{open.NormalPage}/comments"));
            Assert.Equal(HttpStatusCode.NotFound, await Get(open.Anon, $"/api/pages/{open.RestrictedPage}/comments"));
        }
    }

    [Fact]
    public async Task The_instance_switch_hides_everything_and_keeps_the_flag()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;

        (await w.Admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = false })).EnsureSuccessStatusCode();
        Assert.Empty((await w.Anon.GetFromJsonAsync<List<SpaceDto>>("/api/spaces"))!);
        Assert.Equal(HttpStatusCode.NotFound, await Get(w.Anon, "/api/spaces/PUB"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(w.Anon, $"/api/pages/{w.NormalPage}"));
        Assert.Empty((await w.Anon.GetFromJsonAsync<List<SearchHit>>("/api/search?q=pineapple"))!);

        // The space still remembers it was public; re-enabling restores it.
        var rows = await w.Admin.GetFromJsonAsync<List<AdminSpaceDto>>("/api/admin/spaces");
        Assert.True(rows!.Single(r => r.Key == "PUB").IsPublic);
        (await w.Admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, await Get(w.Anon, $"/api/pages/{w.NormalPage}"));
    }

    [Fact]
    public async Task An_archived_or_unpublished_space_goes_dark()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;

        (await w.Admin.PostAsync("/api/spaces/PUB/archive", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, await Get(w.Anon, $"/api/pages/{w.NormalPage}"));
        (await w.Admin.PostAsync("/api/spaces/PUB/unarchive", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, await Get(w.Anon, $"/api/pages/{w.NormalPage}"));

        (await w.Admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, await Get(w.Anon, $"/api/pages/{w.NormalPage}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get(w.Anon, "/api/spaces/PUB"));
    }

    [Fact]
    public async Task Publishing_is_an_administrators_act_behind_the_switch_and_always_an_alert()
    {
        var w = await BuildAsync(publish: false);
        using var _ = w.Factory;

        var member = w.Factory.CreateClient();
        await member.PostAsJsonAsync("/api/auth/register", new { Email = "m@example.com", DisplayName = "M", Password = "supersecret" });
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = true })).StatusCode);

        (await w.Admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = true })).StatusCode);

        (await w.Admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true })).EnsureSuccessStatusCode();
        var row = await (await w.Admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = true }))
            .Content.ReadFromJsonAsync<AdminSpaceDto>();
        Assert.True(row!.IsPublic);
        Assert.Equal(3, row.PageCount); // current pages, restricted ones included; drafts and trash are not

        var alerts = await w.Admin.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        Assert.Contains(alerts!, a => a.Kind == "space.published");
        (await w.Admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = false })).EnsureSuccessStatusCode();
        alerts = await w.Admin.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        Assert.Contains(alerts!, a => a.Kind == "space.unpublished");
    }

    [Fact]
    public async Task Anonymous_reads_are_cacheable_for_a_minute_and_counted_without_a_user()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;

        var first = await w.Anon.GetAsync($"/api/pages/{w.NormalPage}");
        Assert.Equal("public, max-age=60", first.Headers.CacheControl!.ToString());
        var etag = first.Headers.ETag!.Tag;

        var again = new HttpRequestMessage(HttpMethod.Get, $"/api/pages/{w.NormalPage}");
        again.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await w.Anon.SendAsync(again)).StatusCode);

        // A signed-in read is never shared-cacheable.
        var mine = await w.Admin.GetAsync($"/api/pages/{w.NormalPage}");
        Assert.Contains("no-store", mine.Headers.CacheControl!.ToString());

        using var scope = w.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.PageViews.AnyAsync(v => v.PageId == w.NormalPage && v.UserId == null));
    }

    [Fact]
    public async Task Robots_sitemap_and_link_previews_describe_only_public_pages()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;

        var robots = await w.Anon.GetStringAsync("/robots.txt");
        Assert.Contains("Allow: /spaces/PUB", robots);
        Assert.DoesNotContain("PRIV", robots);
        Assert.Contains("Disallow: /api/", robots);

        var sitemap = await w.Anon.GetStringAsync("/sitemap.xml");
        Assert.Contains($"/spaces/PUB/pages/{w.NormalPage}", sitemap);
        Assert.DoesNotContain(w.RestrictedPage.ToString(), sitemap);
        Assert.DoesNotContain(w.DraftPage.ToString(), sitemap);
        Assert.DoesNotContain(w.PrivatePage.ToString(), sitemap);

        var html = new HttpRequestMessage(HttpMethod.Get, $"/spaces/PUB/pages/{w.NormalPage}");
        html.Headers.Accept.ParseAdd("text/html");
        var shell = await (await w.Anon.SendAsync(html)).Content.ReadAsStringAsync();
        // Instance - Space / Page (dev-plan 13.1); the link preview's own
        // title is still the bare page title.
        Assert.Matches("<title>Tesria - [^<]+ / Public page[^<]*</title>", shell);
        Assert.Contains("og:title\" content=\"Public page", shell);
        Assert.Contains("og:title", shell);
        Assert.Contains("pineapple", shell); // the description

        // A restricted page's title never reaches a link preview, even for
        // someone who could read it, because the check is anonymous.
        var priv = new HttpRequestMessage(HttpMethod.Get, $"/spaces/PUB/pages/{w.RestrictedPage}");
        priv.Headers.Accept.ParseAdd("text/html");
        var privShell = await (await w.Admin.SendAsync(priv)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Restricted page", privShell);
        Assert.Contains("<title>Tesria</title>", privShell);
    }

    // ---- /api/instance: opt-in twice (dev-plan 5.5) -------------------------

    private record InstanceDto(string InstanceName, bool NeedsOwner, bool PublicReading, bool AllowPublicRegistration);

    private static Task<InstanceDto?> InstanceAsync(HttpClient c) =>
        c.GetFromJsonAsync<InstanceDto>("/api/instance");

    [Fact]
    public async Task Public_reading_needs_the_instance_switch_as_well_as_a_public_space()
    {
        // Built with a public space, but the instance switch turned back off.
        var w = await BuildAsync();
        using var _ = w.Factory;
        Assert.True((await InstanceAsync(w.Anon))!.PublicReading);

        (await w.Admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = false }))
            .EnsureSuccessStatusCode();

        Assert.False((await InstanceAsync(w.Anon))!.PublicReading);
    }

    [Fact]
    public async Task Public_reading_needs_a_public_space_as_well_as_the_instance_switch()
    {
        // The switch is on throughout BuildAsync; publish nothing.
        var w = await BuildAsync(publish: false);
        using var _ = w.Factory;

        Assert.False((await InstanceAsync(w.Anon))!.PublicReading);

        (await w.Admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = true }))
            .EnsureSuccessStatusCode();

        Assert.True((await InstanceAsync(w.Anon))!.PublicReading);
    }

    [Fact]
    public async Task An_archived_public_space_does_not_count_as_publishing_anything()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;
        Assert.True((await InstanceAsync(w.Anon))!.PublicReading);

        // Archiving is how a space is taken out of circulation without
        // destroying it; it must take the public reading with it.
        (await w.Admin.PostAsJsonAsync("/api/spaces/PUB/archive", new { })).EnsureSuccessStatusCode();

        Assert.False((await InstanceAsync(w.Anon))!.PublicReading);
    }

    [Fact]
    public async Task Needs_owner_is_true_only_until_the_first_account_exists()
    {
        using var factory = new TestAppFactory();
        var anon = factory.CreateClient();

        Assert.True((await InstanceAsync(anon))!.NeedsOwner);

        (await anon.PostAsJsonAsync("/api/auth/register",
            new { Email = "first@example.com", DisplayName = "First", Password = "supersecret" }))
            .EnsureSuccessStatusCode();

        Assert.False((await InstanceAsync(factory.CreateClient()))!.NeedsOwner);
    }

    [Fact]
    public async Task The_sign_up_hint_follows_the_registration_setting()
    {
        var w = await BuildAsync(publish: false);
        using var _ = w.Factory;
        Assert.True((await InstanceAsync(w.Anon))!.AllowPublicRegistration);

        (await w.Admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicRegistration = false }))
            .EnsureSuccessStatusCode();

        Assert.False((await InstanceAsync(w.Anon))!.AllowPublicRegistration);
    }

    [Fact]
    public async Task The_instance_endpoint_says_nothing_an_anonymous_caller_should_not_know()
    {
        var w = await BuildAsync();
        using var _ = w.Factory;

        var body = await w.Anon.GetStringAsync("/api/instance");

        // The documented fields and nothing else: no space keys, no account
        // count, no addresses, no settings beyond these. "branding" (dev-plan
        // 13.1) is what every page, the sign-in page included, already shows.
        // "ownCertificate" (15.5) says what the certificate itself tells
        // anyone who connects.
        var fields = System.Text.Json.JsonDocument.Parse(body).RootElement
            .EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(
            ["allowPublicRegistration", "branding", "instanceName", "needsOwner", "ownCertificate", "publicReading", "version"],
            fields);
        Assert.DoesNotContain("PUB", body);
        Assert.DoesNotContain("admin@example.com", body);
    }

    private static IEnumerable<TreeNode> Flatten(List<TreeNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
}
