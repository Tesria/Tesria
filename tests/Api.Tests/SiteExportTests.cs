using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
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

        // Names index.html rather than ending at the directory, so the link
        // works from the filesystem as well as from a server.
        Assert.Contains("../../guides/install/index.html", rewritten);
        Assert.DoesNotContain("/spaces/DOCS/pages/", rewritten);
    }

    [Fact]
    public void A_heading_anchor_survives_the_rewrite()
    {
        var target = Guid.NewGuid();
        var paths = new Dictionary<Guid, string> { [target] = "guides/install" };
        var html = $"""<a href="/spaces/DOCS/pages/{target}#requirements">Requirements</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "", paths, new Dictionary<Guid, string>());

        Assert.Contains("./guides/install/index.html#requirements", rewritten);
    }

    [Fact]
    public void A_link_to_a_page_that_is_not_in_the_site_stops_being_a_link()
    {
        // A restricted page, or one in another space. A static site that 404s
        // on its own navigation is worse than one that plainly goes nowhere.
        var html = $"""<a href="/spaces/DOCS/pages/{Guid.NewGuid()}">Secret</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "guides", new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

        Assert.Contains("""href="#" title="This page is not part of this export." aria-disabled="true">Secret""", rewritten);
    }

    [Fact]
    public void A_link_into_the_app_itself_stops_being_a_link()
    {
        // A Labels list block links each label to its page in the app, which a
        // static site does not have. External and relative links are untouched.
        var html = """<a href="/labels/how-to">how-to</a> <a href="https://example.com/x">out</a> <a href="//cdn.example.com/y">cdn</a> <a href="../other/index.html">rel</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "guides", new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

        Assert.Contains("""href="#" title="This is part of the Tesria app, not this export." aria-disabled="true">how-to""", rewritten);
        Assert.Contains("""href="https://example.com/x">""", rewritten);
        Assert.Contains("""href="//cdn.example.com/y">""", rewritten);
        Assert.Contains("""href="../other/index.html">""", rewritten);
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
    public void A_framed_PDF_becomes_the_file_beside_the_page()
    {
        // The file block frames a PDF at its view address, not its download.
        var file = Guid.NewGuid();
        var assets = new Dictionary<Guid, string> { [file] = $"{file:N}-plan.pdf" };
        var html = $"""<iframe src="/api/attachments/{file}/view" title="plan.pdf"></iframe>""";

        var rewritten = SiteExport.RewriteLinks(html, "guides", new Dictionary<Guid, string>(), assets);

        Assert.Contains($"src=\"../assets/{file:N}-plan.pdf\"", rewritten);
    }

    [Fact]
    public void No_link_in_a_site_ends_at_a_directory()
    {
        // Reported by the owner, 2026-09-20: unzipped and opened from the
        // filesystem, clicking a sidebar link showed Chrome's folder listing
        // instead of the page. A server serves a directory's index file;
        // file:// has nothing to do that, so every link names the file.
        var target = Guid.NewGuid();
        var paths = new Dictionary<Guid, string> { [target] = "guides/install" };
        var placed = SiteExport.Place([Node("Guides", Node("Install"))]);

        var body = SiteExport.RewriteLinks(
            $"""<a href="/spaces/DOCS/pages/{target}">Install</a>""",
            "reference/api", paths, new Dictionary<Guid, string>());
        var tree = SiteChrome.Tree(placed, "guides");
        var brand = SiteChrome.Topbar(new SiteChrome.Brand("Tesria"), SiteExport.Root("guides/install"));

        foreach (var html in new[] { body, tree, brand })
        {
            var hrefs = System.Text.RegularExpressions.Regex
                .Matches(html, "href=\"(?<url>[^\"]*)\"")
                .Select(m => m.Groups["url"].Value)
                .ToList();
            Assert.NotEmpty(hrefs);
            Assert.All(hrefs, href => Assert.EndsWith("index.html", href));
        }
    }

    [Fact]
    public void An_external_link_is_left_exactly_as_it_was()
    {
        var html = """<a href="https://example.com/handbook">The handbook</a>""";

        var rewritten = SiteExport.RewriteLinks(html, "guides", new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

        Assert.Equal(html, rewritten);
    }

    // --- The chrome around a page.

    [Fact]
    public void The_page_tree_marks_the_page_it_is_on()
    {
        var placed = SiteExport.Place([Node("Guides", Node("Install"))]);

        var tree = SiteChrome.Tree(placed, "guides/install");

        Assert.Contains("aria-current=\"page\"", tree);
        Assert.Contains("tree__link is-active", tree);
    }

    [Fact]
    public void The_page_tree_indents_by_depth_the_way_the_application_does()
    {
        // The app sets this inline per row (8px plus 14px a level), so an
        // export has to as well: the stylesheet has no depth rules to lean on.
        var placed = SiteExport.Place([Node("Guides", Node("Install"))]);

        var tree = SiteChrome.Tree(placed, "");

        Assert.Contains("padding-left: 8px", tree);
        Assert.Contains("padding-left: 22px", tree);
    }

    [Fact]
    public void The_sidebar_carries_the_space_name_key_and_a_pages_heading()
    {
        var head = new SiteChrome.SpaceHead("DOCS", "Documentation", true, SpaceIconKind.None, null, null);

        var sidebar = SiteChrome.Sidebar(head, SiteExport.Place([Node("Install")]), "install");

        Assert.Contains("Documentation", sidebar);
        Assert.Contains("DOCS", sidebar);
        Assert.Contains("Pages", sidebar);
        // The app's own classes, which is what makes the exported stylesheet
        // lay this out with no rules written for the export.
        Assert.Contains("class=\"sidebar\"", sidebar);
        Assert.Contains("tree-section__heading", sidebar);
        // A public space says so, exactly as the app's sidebar does.
        Assert.Contains("badge--public", sidebar);
    }

    [Fact]
    public void A_generated_tile_is_drawn_in_the_theme_accent()
    {
        // Owner's request, 2026-09-20, and export-only: the app's twelve
        // per-space colors exist to tell spaces apart in a list, and an
        // export is one space. Tokens rather than the hex they resolve to, so
        // the tile follows the reader's accent and light/dark with the rest
        // of the page.
        var head = new SiteChrome.SpaceHead("DOCS", "Documentation", false, SpaceIconKind.None, null, null);

        var sidebar = SiteChrome.Sidebar(head, [], "");

        Assert.Contains(">D</text>", sidebar);
        Assert.Contains("fill=\"var(--primary)\"", sidebar);
        // The letter takes the token already tuned for contrast on a filled
        // accent in each theme, not a hardcoded white.
        Assert.Contains("fill=\"var(--on-primary)\"", sidebar);
        // None of the app's palette travels with it.
        Assert.DoesNotContain("#", sidebar);
    }

    [Fact]
    public void An_uploaded_space_icon_is_referenced_relative_to_the_page()
    {
        // Every asset in a site is a real file, and "assets/x" from two
        // directories down is not the same file.
        var head = new SiteChrome.SpaceHead(
            "DOCS", "Documentation", false, SpaceIconKind.Image, "hash", "assets/space-icon.webp");

        var deep = SiteChrome.Sidebar(head, [], "guides/install");

        Assert.Contains("../../assets/space-icon.webp", deep);
        Assert.DoesNotContain("/api/media/", deep);
    }

    [Fact]
    public void The_top_bar_carries_the_instance_name_and_the_appearance_menu()
    {
        var bar = SiteChrome.Topbar(new SiteChrome.Brand("Acme Wiki"), homeHref: "../");

        Assert.Contains("Acme Wiki", bar);
        Assert.Contains("class=\"topbar\"", bar);
        // The full menu, not a row of buttons: three modes and six accents.
        foreach (var mode in new[] { "system", "light", "dark" })
            Assert.Contains($"data-theme-mode=\"{mode}\"", bar);
        foreach (var accent in new[] { "blue", "teal", "green", "purple", "orange", "magenta" })
            Assert.Contains($"data-theme-accent=\"{accent}\"", bar);
    }

    [Fact]
    public void The_top_bar_carries_a_full_width_toggle()
    {
        // The reading view has one in its action bar; an export has no action
        // bar, so it moves to the top bar (owner's request, 2026-09-20).
        var bar = SiteChrome.Topbar(new SiteChrome.Brand("Tesria"), homeHref: null);

        Assert.Contains("data-export-width-toggle", bar);
        // Both labels ship and the script shows one: a captured export has no
        // React left to re-render the text.
        Assert.Contains("Full width", bar);
        Assert.Contains("Normal width", bar);
    }

    [Fact]
    public void The_brand_is_not_a_link_when_there_is_nowhere_to_go()
    {
        // A single-file export has no index to return to.
        var bar = SiteChrome.Topbar(new SiteChrome.Brand("Tesria"), homeHref: null);

        Assert.Contains("<span class=\"brand\">", bar);
        Assert.DoesNotContain("<a class=\"brand\"", bar);
    }

    [Fact]
    public void The_site_root_from_a_nested_page_names_the_front_page()
    {
        Assert.Equal("./index.html", SiteExport.Root(""));
        Assert.Equal("../index.html", SiteExport.Root("install"));
        Assert.Equal("../../index.html", SiteExport.Root("guides/install"));
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
