using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// A numbered or bulleted page tree (dev-plan 15.8). The markers are drawn,
/// never stored: they are worked out from the tree's order, so they follow
/// every move.
/// </summary>
public class SpaceTreeStyleTests
{
    private record SpaceDto(Guid Id, string Key, string Name, int TreeStyle);

    // The same cases as treeMarkers.test.ts, so the app and an exported site agree.
    private static readonly int[] Depths = [0, 1, 1, 0, 1, 2, 0];

    [Fact]
    public void Numbers_are_outline_numbers_restarting_under_each_parent() =>
        Assert.Equal(["1", "1.1", "1.2", "2", "2.1", "2.1.1", "3"], SiteChrome.TreeMarkers(Depths, SpaceTreeStyle.Numbered));

    [Fact]
    public void Bullets_change_with_the_level() =>
        Assert.Equal(["•", "◦", "◦", "•", "◦", "▪", "•"], SiteChrome.TreeMarkers(Depths, SpaceTreeStyle.Bulleted));

    [Fact]
    public void A_plain_tree_has_no_markers() =>
        Assert.All(SiteChrome.TreeMarkers(Depths, SpaceTreeStyle.Plain), m => Assert.Null(m));

    [Fact]
    public void A_new_level_starts_at_one_after_going_back_up() =>
        Assert.Equal(["1", "1.1", "1.1.1", "1.2", "1.2.1"], SiteChrome.TreeMarkers([0, 1, 2, 1, 2], SpaceTreeStyle.Numbered));

    [Fact]
    public void An_exported_sidebar_carries_the_numbers_and_the_page_filter()
    {
        // The filter (dev-plan 15.9) runs in the export's own script, from
        // each link's depth and title; the numbers are drawn beside the title.
        var head = new SiteChrome.SpaceHead("DOCS", "Docs", false, SpaceIconKind.None, null, null, SpaceTreeStyle.Numbered);
        List<SiteExport.Placed> pages =
        [
            new(Guid.NewGuid(), "Getting started", "getting-started", 0, null),
            new(Guid.NewGuid(), "Quick start", "getting-started/quick-start", 1, null),
        ];
        var html = SiteChrome.Sidebar(head, pages, "getting-started/quick-start");
        Assert.Contains("class=\"tree-filter__input\"", html);
        Assert.Contains("class=\"tree-filter__children is-on\" aria-pressed=\"true\"", html);
        Assert.Contains("data-depth=\"1\"", html);
        Assert.Contains("<span class=\"tree__marker\" aria-hidden=\"true\">1.1</span>", html);
        Assert.Contains("<span class=\"tree__title\">Quick start</span>", html);
        Assert.Contains("wireTreeFilter", SiteChrome.ThemeScript());
    }

    [Fact]
    public async Task A_space_administrator_sets_it_and_the_titles_stay_as_they_are()
    {
        await using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        await c.CreateSpaceAsync("TREE");

        var res = await c.PutAsJsonAsync("/api/spaces/TREE", new { Name = "Tree", TreeStyle = 1 });
        res.EnsureSuccessStatusCode();
        Assert.Equal(1, (await c.GetFromJsonAsync<SpaceDto>("/api/spaces/TREE"))!.TreeStyle);

        // Leaving it out of an update leaves it alone.
        (await c.PutAsJsonAsync("/api/spaces/TREE", new { Name = "Tree renamed" })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await c.GetFromJsonAsync<SpaceDto>("/api/spaces/TREE"))!.TreeStyle);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PutAsJsonAsync("/api/spaces/TREE", new { Name = "Tree", TreeStyle = 7 })).StatusCode);
    }

    [Fact]
    public async Task A_pack_carries_it()
    {
        await using var factory = new TestAppFactory();
        var c = factory.CreateClient();
        await c.RegisterAndSignInAsync();
        await c.CreateSpaceAsync("NUM");
        (await c.PutAsJsonAsync("/api/spaces/NUM", new { Name = "Numbered", TreeStyle = 2 })).EnsureSuccessStatusCode();

        var pack = await (await c.GetAsync("/api/spaces/NUM/export/pack")).Content.ReadAsByteArrayAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pack);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "pack.zip");
        form.Add(new StringContent("NUMCOPY"), "key");
        (await c.PostAsync("/api/spaces/import", form)).EnsureSuccessStatusCode();

        Assert.Equal(2, (await c.GetFromJsonAsync<SpaceDto>("/api/spaces/NUMCOPY"))!.TreeStyle);
    }
}
