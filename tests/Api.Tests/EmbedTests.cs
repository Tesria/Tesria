using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Features.Embeds;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Host matching is the whole of the embed trust decision, so it gets the
/// hostile cases: a suffix match that is not on a label boundary would let
/// <c>evil-youtube.com</c> through the allowlist and into a frame.
/// </summary>
public class EmbedAllowlistTests
{
    private static readonly string[] Entries = EmbedAllowlist.Parse(".youtube.com\ndocs.google.com\n");

    [Theory]
    [InlineData("youtube.com")]
    [InlineData("www.youtube.com")]
    [InlineData("music.youtube.com")]
    [InlineData("WWW.YouTube.COM")]
    [InlineData("www.youtube.com.")]
    [InlineData("docs.google.com")]
    public void Allows_the_listed_domain_and_its_subdomains(string host) =>
        Assert.True(EmbedAllowlist.IsAllowed(host, Entries));

    [Theory]
    [InlineData("evil-youtube.com")]        // suffix, but not on a label boundary
    [InlineData("youtube.com.attacker.net")] // the real domain is attacker.net
    [InlineData("notyoutube.com")]
    [InlineData("google.com")]              // exact entry does not imply the parent
    [InlineData("evil.docs.google.com")]    // ...nor a subdomain of an exact entry
    [InlineData("")]
    [InlineData(null)]
    public void Refuses_everything_else(string? host) =>
        Assert.False(EmbedAllowlist.IsAllowed(host, Entries));

    [Fact]
    public void Parsing_accepts_lines_or_commas_and_normalises()
    {
        var entries = EmbedAllowlist.Parse(" .YouTube.com. ,vimeo.com\n\n , .youtube.com ");
        Assert.Equal([".youtube.com", "vimeo.com"], entries);
    }

    [Fact]
    public void An_empty_allowlist_allows_nothing()
    {
        var entries = EmbedAllowlist.Parse("   \n  ");
        Assert.Empty(entries);
        Assert.False(EmbedAllowlist.IsAllowed("youtube.com", entries));
        Assert.Empty(EmbedAllowlist.CspSources(entries));
    }

    [Fact]
    public void Csp_sources_mirror_the_entries()
    {
        Assert.Equal(["https://*.youtube.com", "https://docs.google.com"], EmbedAllowlist.CspSources(Entries));
    }
}

public class EmbedProviderTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/347119375", "https://player.vimeo.com/video/347119375")]
    [InlineData("https://www.loom.com/share/abc123def456", "https://www.loom.com/embed/abc123def456")]
    [InlineData("https://miro.com/app/board/uXjVM123=/", "https://miro.com/app/live-embed/uXjVM123=")]
    [InlineData("https://codepen.io/ana/pen/abcDEF", "https://codepen.io/ana/embed/abcDEF")]
    [InlineData("https://docs.google.com/document/d/abc123/edit", "https://docs.google.com/document/d/abc123/preview")]
    public void Narrows_a_known_provider_to_its_embed_form(string input, string expected) =>
        Assert.Equal(expected, EmbedProviders.Resolve(new Uri(input))?.Url);

    [Fact]
    public void A_host_with_no_provider_rule_frames_as_given()
    {
        // What makes "allowlist our internal Grafana" work with no code.
        Assert.Null(EmbedProviders.Resolve(new Uri("https://grafana.internal.example/d/abc")));
    }

    [Fact]
    public void A_provider_url_with_no_usable_id_is_refused_rather_than_guessed()
    {
        Assert.Null(EmbedProviders.Resolve(new Uri("https://www.youtube.com/results?search_query=cats")));
        Assert.Null(EmbedProviders.Resolve(new Uri("https://vimeo.com/about")));
    }

    [Fact]
    public void A_host_beginning_with_w_is_not_mangled()
    {
        // TrimStart('w', '.') would turn wiki.example.com into iki.example.com.
        Assert.Null(EmbedProviders.Resolve(new Uri("https://wiki.example.com/page")));
    }
}

public class EmbedEndpointTests
{
    private record EmbedResponse(bool Allowed, string? Url, string? Provider, string? AspectRatio, string? Reason);

    [Fact]
    public async Task Resolve_narrows_an_allowed_address_and_refuses_the_rest()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var ok = await client.GetFromJsonAsync<EmbedResponse>(
            "/api/embeds/resolve?url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.True(ok!.Allowed);
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", ok.Url);
        Assert.Equal("16 / 9", ok.AspectRatio);

        var refused = await client.GetFromJsonAsync<EmbedResponse>(
            "/api/embeds/resolve?url=" + Uri.EscapeDataString("https://evil-youtube.com/watch?v=x"));
        Assert.False(refused!.Allowed);
        Assert.Null(refused.Url);
        Assert.Contains("allowlist", refused.Reason);

        // Not a web address at all.
        var bad = await client.GetFromJsonAsync<EmbedResponse>(
            "/api/embeds/resolve?url=" + Uri.EscapeDataString("javascript:alert(1)"));
        Assert.False(bad!.Allowed);
    }

    [Fact]
    public async Task Emptying_the_allowlist_turns_embeds_off()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await admin.RegisterAndSignInAsync(); // the first account is the admin

        (await admin.PutAsJsonAsync("/api/admin/settings", new { EmbedAllowlist = "" })).EnsureSuccessStatusCode();

        var refused = await admin.GetFromJsonAsync<EmbedResponse>(
            "/api/embeds/resolve?url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
        Assert.False(refused!.Allowed);
    }

    [Fact]
    public async Task The_csp_frame_src_is_built_from_the_allowlist()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await admin.RegisterAndSignInAsync();

        var before = (await admin.GetAsync("/api/health")).Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-src 'self' https://*.youtube.com", before);

        (await admin.PutAsJsonAsync("/api/admin/settings", new { EmbedAllowlist = "example.test" })).EnsureSuccessStatusCode();

        var after = (await admin.GetAsync("/api/health")).Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-src 'self' https://example.test", after);
        Assert.DoesNotContain("youtube", after);
    }

    [Fact]
    public async Task Unfurling_needs_an_account()
    {
        using var factory = new TestAppFactory();
        var anon = factory.CreateClient();
        // Unfurling makes an outbound request; an anonymous visitor cannot.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/embeds/unfurl?url=https%3A%2F%2Fexample.com")).StatusCode);
    }

    [Fact]
    public async Task Unfurling_a_private_address_is_refused_by_the_egress_guard()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var preview = await client.GetFromJsonAsync<EmbedEndpoints.LinkPreviewResponse>(
            "/api/embeds/unfurl?url=" + Uri.EscapeDataString("http://169.254.169.254/latest/meta-data/"));
        Assert.NotNull(preview!.Error);
        Assert.Null(preview.Title);
    }
}

public class EmbedExportTests
{
    private const string Doc = """
    {"type":"doc","content":[
      {"type":"embed","attrs":{"url":"https://www.youtube.com/watch?v=dQw4w9WgXcQ"}},
      {"type":"smartLink","attrs":{"url":"https://example.com/post","display":"card"}},
      {"type":"embed","attrs":{"url":"javascript:alert(1)"}},
      {"type":"attachmentBlock","attrs":{"attachmentId":"11111111-1111-1111-1111-111111111111"}},
      {"type":"gallery","content":[{"type":"paragraph","content":[
        {"type":"image","attrs":{"src":"/api/attachments/x/download","alt":"one"}}]}]}
    ]}
    """;

    [Fact]
    public void An_export_turns_frames_and_previews_into_plain_links()
    {
        var html = ProseMirrorRenderer.ToHtml(Doc, null, "https://wiki.example");
        // Never an iframe in a file that will be opened outside the app.
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("https://www.youtube.com/watch?v=dQw4w9WgXcQ", html);
        Assert.Contains("https://example.com/post", html);
        Assert.Contains("https://wiki.example/api/attachments/11111111-1111-1111-1111-111111111111/download", html);
        // A gallery is a layout: its images are ordinary images.
        Assert.Contains("alt=\"one\"", html);
    }

    [Fact]
    public void A_hostile_url_never_becomes_a_link()
    {
        var html = ProseMirrorRenderer.ToHtml(Doc);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("<em>[link]</em>", html);

        var md = ProseMirrorRenderer.ToMarkdown(Doc);
        Assert.DoesNotContain("javascript:", md);
        Assert.Contains("<https://example.com/post>", md);
    }
}

/// <summary>Wave F: what a diagram, an equation and a chart become outside the app.</summary>
public class TechnicalContentExportTests
{
    private const string Doc = """
    {"type":"doc","content":[
      {"type":"codeBlock","attrs":{"language":"mermaid"},"content":[{"type":"text","text":"flowchart LR\n A-->B"}]},
      {"type":"paragraph","content":[
        {"type":"text","text":"where "},
        {"type":"math","attrs":{"latex":"e = mc^2","display":false}},
        {"type":"text","text":" holds."}]},
      {"type":"math","attrs":{"latex":"\\sum_{i=1}^{n} i","display":true}},
      {"type":"chart","attrs":{"source":2,"chartType":"pie","title":"Spend"}}
    ]}
    """;

    [Fact]
    public void A_mermaid_block_exports_its_source_in_the_shape_a_renderer_looks_for()
    {
        var html = ProseMirrorRenderer.ToHtml(Doc, null, null, out var usedMermaid);
        Assert.True(usedMermaid);
        Assert.Contains("<pre class=\"mermaid\">flowchart LR", html);
        // Not a highlighted code block — it is meant to be drawn.
        Assert.DoesNotContain("language-mermaid", html);

        // Markdown keeps it a fenced block, which GitHub and others render.
        var md = ProseMirrorRenderer.ToMarkdown(Doc);
        Assert.Contains("```mermaid", md);
        Assert.Contains("flowchart LR", md);
    }

    [Fact]
    public async Task An_exported_diagram_carries_its_renderer_rather_than_fetching_one()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await (await client.PostAsJsonAsync("/api/pages", new
        {
            SpaceId = spaceId,
            ParentPageId = (Guid?)null,
            Title = "Diagram",
            ContentJson = Doc,
        })).Content.ReadFromJsonAsync<PageRef>();

        var html = await (await client.GetAsync($"/api/pages/{page!.Id}/export?format=html")).Content.ReadAsStringAsync();

        // Nothing in an exported file may reach the network.
        Assert.DoesNotContain("cdn.jsdelivr.net", html);
        Assert.DoesNotContain("https://cdn", html);
        Assert.DoesNotContain("<script src=", html);
        // The source is present either way — the test web root has no built
        // bundle, so this export ships the diagram as readable text alone.
        Assert.Contains("<pre class=\"mermaid\">flowchart LR", html);
    }

    private record PageRef(Guid Id);

    [Fact]
    public void A_page_with_no_diagram_does_not_claim_to_need_a_renderer()
    {
        ProseMirrorRenderer.ToHtml("""{"type":"doc","content":[{"type":"paragraph"}]}""", null, null, out var used);
        Assert.False(used);
    }

    [Fact]
    public void Maths_exports_as_its_latex_source_in_the_usual_delimiters()
    {
        var html = ProseMirrorRenderer.ToHtml(Doc);
        Assert.Contains("<span class=\"math\">$e = mc^2$</span>", html);
        Assert.Contains("$$\\sum_{i=1}^{n} i$$", html);

        var md = ProseMirrorRenderer.ToMarkdown(Doc);
        Assert.Contains("where $e = mc^2$ holds.", md);
        Assert.Contains("$$\n\\sum_{i=1}^{n} i\n$$", md);
    }

    [Fact]
    public void A_chart_names_the_table_it_charts_rather_than_copying_the_data()
    {
        var html = ProseMirrorRenderer.ToHtml(Doc);
        Assert.Contains("[Chart of table 2: Spend]", html);
        Assert.Contains("[Chart of table 2]", ProseMirrorRenderer.ToMarkdown(Doc));
    }
}
