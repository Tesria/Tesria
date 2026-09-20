using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Publishing a space as a static site (dev-plan 12.2).
///
/// The pages themselves are captured in a browser, so what is worth testing
/// here is everything that is the *site*: where a page ends up, what a link
/// means once pages are files, and which pages are in it at all. The last of
/// those is the one that matters most, because getting it wrong puts a
/// private page on the internet.
/// </summary>
public class SiteExportTests
{
    private static SiteExport.PageNode Node(string title, params SiteExport.PageNode[] children) =>
        new(Guid.NewGuid(), title, children);

    // --- Naming.

    [Theory]
    [InlineData("Getting started", "getting-started")]
    [InlineData("  Spaces & pages  ", "spaces-pages")]
    [InlineData("C# for beginners", "c-for-beginners")]
    [InlineData("2026 Roadmap", "2026-roadmap")]
    public void A_title_becomes_a_readable_directory_name(string title, string expected) =>
        Assert.Equal(expected, SiteExport.Slug(title));

    [Fact]
    public void A_title_with_nothing_usable_in_it_still_gets_a_name()
    {
        // An emoji-only title, or one in a script this does not transliterate.
        // "page" plus sibling de-duplication is better than an empty path.
        Assert.Equal("page", SiteExport.Slug("🎉"));
        Assert.Equal("page", SiteExport.Slug("...."));
    }

    [Fact]
    public void Siblings_with_the_same_title_are_kept_apart()
    {
        var placed = SiteExport.Place([Node("Overview"), Node("Overview"), Node("Overview")]);

        Assert.Equal(["overview", "overview-2", "overview-3"], placed.Select(p => p.Path));
    }

    [Fact]
    public void The_same_title_under_different_parents_is_not_a_conflict()
    {
        // Two pages called Overview in different sections are not ambiguous,
        // and renaming one of them would be surprising.
        var placed = SiteExport.Place([
            Node("Guides", Node("Overview")),
            Node("Reference", Node("Overview")),
        ]);

        Assert.Contains("guides/overview", placed.Select(p => p.Path));
        Assert.Contains("reference/overview", placed.Select(p => p.Path));
    }

    [Fact]
    public void The_tree_becomes_directories_that_mirror_it()
    {
        var placed = SiteExport.Place([Node("Guides", Node("Install", Node("Docker")))]);

        Assert.Equal(["guides", "guides/install", "guides/install/docker"], placed.Select(p => p.Path));
        Assert.Equal([0, 1, 2], placed.Select(p => p.Depth));
    }

    // --- Links, once pages are files.

    [Fact]
    public void A_link_to_a_page_in_the_site_becomes_a_relative_path()
    {
        var target = Guid.NewGuid();
        var paths = new Dictionary<Guid, string> { [target] = "guides/install" };
        var html = $"""<a href="/spaces/DOCS/pages/{target}">Install</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "reference/api", paths, new Dictionary<Guid, string>());

        Assert.Contains("../../guides/install/", rewritten);
        Assert.DoesNotContain("/spaces/DOCS/pages/", rewritten);
    }

    [Fact]
    public void A_heading_anchor_survives_the_rewrite()
    {
        var target = Guid.NewGuid();
        var paths = new Dictionary<Guid, string> { [target] = "guides/install" };
        var html = $"""<a href="/spaces/DOCS/pages/{target}#requirements">Requirements</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "", paths, new Dictionary<Guid, string>());

        Assert.Contains("./guides/install/#requirements", rewritten);
    }

    [Fact]
    public void A_link_to_a_page_that_is_not_in_the_site_stops_being_a_link()
    {
        // A restricted page, or one in another space. A static site that 404s
        // on its own navigation is worse than one that plainly goes nowhere.
        var html = $"""<a href="/spaces/DOCS/pages/{Guid.NewGuid()}">Secret</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "guides", new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

        Assert.Contains("""href="#">Secret""", rewritten);
    }

    [Fact]
    public void An_attachment_url_becomes_the_file_beside_the_page()
    {
        var file = Guid.NewGuid();
        var assets = new Dictionary<Guid, string> { [file] = $"{file:N}-diagram.png" };
        var html = $"""<img src="/api/attachments/{file}/download" />""";

        var rewritten = SiteExport.RewriteLinks(html, "guides/install", new Dictionary<Guid, string>(), assets);

        Assert.Contains($"../../assets/{file:N}-diagram.png", rewritten);
        Assert.DoesNotContain("/api/attachments/", rewritten);
    }

    [Fact]
    public void An_external_link_is_left_exactly_as_it_was()
    {
        var html = """<a href="https://example.com/handbook">The handbook</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "guides", new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

        Assert.Equal(html, rewritten);
    }

    [Fact]
    public void The_navigation_marks_the_page_it_is_on()
    {
        var placed = SiteExport.Place([Node("Guides", Node("Install"))]);

        var nav = SiteExport.Nav(placed, "guides/install");

        Assert.Contains("aria-current=\"page\"", nav);
        Assert.Contains("is-current", nav);
    }

    // --- Who the site is for.

    [Fact]
    public async Task An_anonymous_site_of_an_unpublished_space_is_refused_rather_than_empty()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync("DOCS");
        _ = spaceId;

        var res = await client.GetAsync("/api/spaces/DOCS/export/site?audience=anonymous");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not public", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_space_nobody_may_see_is_masked()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync("PRIV");
        // Close the space: spaces are open until somebody says otherwise.
        (await owner.PostAsJsonAsync($"/api/spaces/PRIV/permissions",
            new { PrincipalType = 0, PrincipalId = ownerId, Operation = 2 })).EnsureSuccessStatusCode();
        _ = spaceId;

        var stranger = factory.CreateClient();
        await stranger.RegisterAndSignInAsync();

        var res = await stranger.GetAsync("/api/spaces/PRIV/export/site?audience=me");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Without_a_renderer_a_site_cannot_be_built_and_says_so()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        await client.CreateSpaceAsync("DOCS");

        var res = await client.GetAsync("/api/spaces/DOCS/export/site?audience=me");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("renderer", await res.Content.ReadAsStringAsync());
    }
}
