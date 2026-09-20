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
    public void An_embedded_hostile_url_never_becomes_a_link_in_markdown()
    {
        // An embed or smart link carries a url straight out of the document,
        // so it is the obvious place to smuggle `javascript:` into a file
        // somebody opens outside the app. Asserted on Markdown since 12.1;
        // in a captured export the frame is the page's own, under the app's
        // CSP and its allowlist.
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

        var md = await (await client.GetAsync($"/api/pages/{page!.Id}/export?format=markdown")).Content.ReadAsStringAsync();

        // Nothing in an exported file may reach the network. Asserted on
        // Markdown since 12.1; the captured formats inherit the same
        // guarantee from the sidecar, which aborts every request that is not
        // this app or a data: URI.
        Assert.DoesNotContain("cdn.jsdelivr.net", md);
        Assert.DoesNotContain("https://cdn", md);
        Assert.DoesNotContain("<script src=", md);
        // The diagram travels as its source, which is what Markdown can carry
        // and what any Markdown renderer knows what to do with. In a captured
        // export it is an SVG, drawn by the page before the photograph.
        Assert.Contains("```mermaid", md);
        Assert.Contains("flowchart LR", md);
    }

    private record PageRef(Guid Id);

    [Fact]
    public void A_hostile_url_never_becomes_a_link()
    {
        // The stored document can say anything; a javascript: url must not
        // come out of an export as something clickable. Asserted on Markdown
        // since 12.1, which is the only format still rendered here.
        var doc = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}],"text":"click"}]}]}
        """;

        var md = ProseMirrorRenderer.ToMarkdown(doc);

        Assert.DoesNotContain("javascript:", md);
        Assert.Contains("click", md);
    }

    [Fact]
    public void Maths_exports_as_its_latex_source_in_the_right_delimiters()
    {
        // Markdown has no maths of its own, so the source in delimiters is
        // both the honest answer and the one every renderer understands.
        // Inline maths stays in its sentence; display maths gets its own
        // block, which is the difference `display` exists to make.
        var md = ProseMirrorRenderer.ToMarkdown(Doc);

        Assert.Contains("where $e = mc^2$ holds.", md);
        Assert.Contains("$$\n\\sum_{i=1}^{n} i\n$$", md);
    }

    [Fact]
    public void A_chart_names_the_table_it_charts_rather_than_copying_the_data()
    {
        // A chart is drawn from a table already in the document. Copying the
        // numbers would let the two disagree; in a captured export the chart
        // is the real one, drawn by the page.
        Assert.Contains("[Chart of table 2]", ProseMirrorRenderer.ToMarkdown(Doc));
    }
}
