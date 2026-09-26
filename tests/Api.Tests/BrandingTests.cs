using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using Tesria.Api.Domain;
using Tesria.Api.Features.Export;
using Tesria.Api.Features.Public;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Branding;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Instance branding (dev-plan 13.1). Three things matter most and get the
/// most tests: that nothing changes until someone sets it on purpose, that
/// an uploaded SVG comes out inert, and that the page's inline script is
/// byte-identical whatever the branding, because the CSP allows it by hash.
/// </summary>
public class BrandingTests
{
    private record BrandLogoDto(string Url, string Format, int? Width, int? Height);
    private record BrandingDto(
        string Name, bool HasCustomName, bool HasIdentity, string Display, string SignInArrangement,
        BrandLogoDto? Logo, BrandLogoDto? LogoDark, bool HasFavicon,
        string ThemePolicy, string AccentPolicy, string? AccentName, string? AccentLight, string? AccentDark);
    private record InstanceDto(string InstanceName, BrandingDto Branding);
    private record CheckDto(string Mode, string Color, double PrimaryVsBackground, double OnPrimaryVsPrimary, bool Passes, string? Suggested);
    private record SettingsDto(
        string? BrandName, string Display, string SignInArrangement, string ThemePolicy, string AccentPolicy,
        string? AccentName, string? AccentLight, string? AccentDark, BrandLogoDto? Logo, BrandLogoDto? LogoDark,
        string? FaviconHash, bool FaviconHasSvg, bool IsCustomized, List<CheckDto> Checks);

    private static object Form(
        string? name = null, string display = "logo-and-name", string arrangement = "side-by-side",
        string theme = "any", string accentPolicy = "any", string? accent = null,
        string? light = null, string? dark = null) => new
    {
        BrandName = name, Display = display, SignInArrangement = arrangement,
        ThemePolicy = theme, AccentPolicy = accentPolicy, AccentName = accent,
        AccentLight = light, AccentDark = dark,
    };

    private static async Task<HttpClient> OwnerAsync(TestAppFactory factory)
    {
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        return owner;
    }

    private static async Task<BrandingDto> BrandingAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<InstanceDto>("/api/instance"))!.Branding;

    private static async Task<string> ShellAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/html");
        return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string filename, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", filename } };
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(0, 0, width / 2f, height, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private const string GoodSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 50"><rect width="50" height="50" fill="#0c66e4"/><text x="60" y="35">Acme</text></svg>""";

    // ---- The right --------------------------------------------------------

    [Fact]
    public void The_right_belongs_to_the_owner_by_default()
    {
        var right = InstancePermissions.All.Single(p => p.Key == InstancePermissions.SettingsBranding);
        Assert.Equal(UserRole.Owner, right.DefaultFrom);
        Assert.Equal(PermissionScope.Administration, right.Scope);
    }

    [Fact]
    public async Task An_administrator_cannot_change_the_branding_until_granted()
    {
        using var factory = new TestAppFactory();
        await OwnerAsync(factory);
        var admin = factory.CreateClient();
        var adminId = await admin.RegisterAndSignInAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Single(u => u.Id == adminId).Role = UserRole.Admin;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/branding")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/admin/branding", Form("Acme"))).StatusCode);
        // The file routes too: they were briefly missing the requirement.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync("/api/admin/branding/logo",
            Upload(Encoding.UTF8.GetBytes(GoodSvg), "logo.svg", "image/svg+xml"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync("/api/admin/branding/favicon")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/admin/branding/reset", new { })).StatusCode);
    }

    // ---- Nothing changes until it is set -----------------------------------

    [Fact]
    public async Task An_unbranded_instance_is_Tesria()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        var b = await BrandingAsync(owner);
        Assert.Equal("Tesria", b.Name);
        Assert.False(b.HasCustomName);
        Assert.False(b.HasIdentity);
        Assert.Null(b.Logo);
        Assert.Equal("any", b.ThemePolicy);
        Assert.False((await owner.GetFromJsonAsync<SettingsDto>("/api/admin/branding"))!.IsCustomized);
    }

    [Fact]
    public async Task Renaming_the_instance_leaves_the_brand_alone()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        (await owner.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Acme Docs" })).EnsureSuccessStatusCode();
        var b = await BrandingAsync(owner);
        Assert.Equal("Tesria", b.Name);
        Assert.False(b.HasIdentity);

        (await owner.PutAsJsonAsync("/api/admin/branding", Form("Acme"))).EnsureSuccessStatusCode();
        b = await BrandingAsync(owner);
        Assert.Equal("Acme", b.Name);
        Assert.True(b.HasIdentity);

        (await owner.PutAsJsonAsync("/api/admin/branding", Form(""))).EnsureSuccessStatusCode();
        Assert.Equal("Tesria", (await BrandingAsync(owner)).Name);
    }

    [Fact]
    public async Task A_brand_name_with_a_line_break_is_refused()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PutAsJsonAsync("/api/admin/branding", Form("Acme\nDocs"))).StatusCode);
    }

    // ---- Tab titles (the same cases as title.test.ts) ----------------------

    [Theory]
    [InlineData("Acme Docs", "Engineering", "Architecture", null, "Acme Docs - Engineering / Architecture")]
    [InlineData("Acme Docs", "Engineering", null, null, "Acme Docs - Engineering")]
    [InlineData("Acme Docs", "Engineering", "", null, "Acme Docs - Engineering")]
    [InlineData("Acme Docs", null, null, "Search", "Acme Docs - Search")]
    [InlineData("Acme Docs", null, null, null, "Acme Docs")]
    [InlineData("  ", null, null, "Search", "Tesria - Search")]
    [InlineData("A-B", "R&D / Ops", "Q3 - plan", null, "A-B - R&D / Ops / Q3 - plan")]
    [InlineData("X", "S", null, "Search", "X - S")]
    public void Title_follows_the_owners_pattern(string instance, string? space, string? page, string? section, string expected) =>
        Assert.Equal(expected, BrandTitle.Format(instance, space, page, section));

    [Fact]
    public void Title_sections_match_the_spa()
    {
        Assert.Equal("Spaces", BrandTitle.SectionFor("/spaces"));
        Assert.Equal("Administration", BrandTitle.SectionFor("/admin/branding"));
        Assert.Equal("Sign in", BrandTitle.SectionFor("/login"));
        Assert.Equal("Reset your password", BrandTitle.SectionFor("/reset"));
        Assert.Null(BrandTitle.SectionFor("/spaces/ENG/pages/1"));
        Assert.Null(BrandTitle.SectionFor("/"));
    }

    [Fact]
    public async Task The_shell_is_titled_by_section_and_instance()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        (await owner.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Acme Docs" })).EnsureSuccessStatusCode();

        Assert.Contains("<title>Acme Docs - Search</title>", await ShellAsync(owner, "/search"));
        Assert.Contains("<title>Acme Docs</title>", await ShellAsync(owner, "/"));
        // A direct request for the file is the same page as "/".
        Assert.Contains("<title>Acme Docs</title>", await ShellAsync(owner, "/index.html"));
    }

    [Fact]
    public async Task An_unknown_api_route_is_404_not_the_shell()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        var res = await owner.GetAsync("/api/no-such-thing");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ---- The shell and the CSP hash ----------------------------------------

    /// <summary>The real index.html, found by walking up from the test binary to the repository.</summary>
    private static string RealIndexHtml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "web", "index.html"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "web", "index.html"));
    }

    private static List<string> Scripts(string html) =>
        Regex.Matches(html, "<script>([\\s\\S]*?)</script>").Select(m => m.Groups[1].Value).ToList();

    private static BrandView FullyBranded() => BrandView.From(new SiteSettings
    {
        BrandName = "Acme",
        BrandLogoHash = "abc", BrandLogoFormat = "svg", BrandLogoWidth = 200, BrandLogoHeight = 50,
        BrandFaviconHash = "def", BrandFaviconHasSvg = true,
        ThemePolicy = "dark", AccentPolicy = "locked", AccentName = "brand",
        BrandAccentLight = "#7a1fa2", BrandAccentDark = "#d49cf0",
    });

    [Fact]
    public void Branding_never_changes_the_inline_script()
    {
        var shell = RealIndexHtml();
        var plain = SpaShell.Render(shell, "Tesria", null, BrandView.From(new SiteSettings()));
        var branded = SpaShell.Render(shell, "Acme - Search", null, FullyBranded());

        var original = Scripts(shell);
        Assert.NotEmpty(original);
        // Byte for byte, or the browser refuses to run it under the CSP.
        Assert.Equal(original, Scripts(plain));
        Assert.Equal(original, Scripts(branded));
    }

    [Fact]
    public void The_shell_carries_the_branding()
    {
        var html = SpaShell.Render(RealIndexHtml(), "Acme - Search", null, FullyBranded());

        Assert.Contains("<title>Acme - Search</title>", html);
        Assert.Contains("data-theme-lock=\"dark\"", html);
        Assert.Contains("data-accent-lock=\"brand\"", html);
        Assert.Contains("data-brand-favicon=\"1\"", html);
        Assert.Contains("/api/branding/favicon.svg?v=def", html);
        Assert.DoesNotContain("href=\"/favicon.svg\"", html);
        Assert.Contains("<style id=\"brand-accent\">", html);
        Assert.Contains("--primary:#d49cf0", html);
    }

    [Fact]
    public void An_unbranded_shell_carries_nothing_extra()
    {
        var shell = RealIndexHtml();
        var html = SpaShell.Render(shell, "Tesria", null, BrandView.From(new SiteSettings()));

        // The root element is untouched (the attribute names do appear, in a
        // comment explaining them, which is why this looks at the tag).
        Assert.Contains("<html lang=\"en\">", html);
        Assert.DoesNotContain("<style id=\"brand-accent\">", html);
        Assert.Contains("href=\"/favicon.svg\"", html);
    }

    // ---- Accents -----------------------------------------------------------

    [Theory]
    [InlineData("#0C66E4", "#0c66e4")]
    [InlineData("0c66e4", "#0c66e4")]
    [InlineData("#abc", "#aabbcc")]
    [InlineData("red", null)]
    [InlineData("#0c66e4;} body{display:none", null)]
    [InlineData("url(x)", null)]
    public void Colors_are_normalized_or_refused(string input, string? expected) =>
        Assert.Equal(expected, AccentColors.Normalize(input));

    [Fact]
    public void The_built_in_accents_pass_their_own_checks()
    {
        foreach (var light in new[] { "#0c66e4", "#0b6b82", "#1a6c45", "#5b47ba", "#9a4d00", "#a53a7f" })
            Assert.True(AccentColors.Check(light, dark: false).Passes, light);
        foreach (var dark in new[] { "#579dff", "#6cc3e0", "#4bce97", "#b8acf6", "#fea362", "#f797d2" })
            Assert.True(AccentColors.Check(dark, dark: true).Passes, dark);
    }

    [Fact]
    public void Derivation_is_stable()
    {
        Assert.Equal(AccentColors.Derive("#7a1fa2", dark: false), AccentColors.Derive("#7a1fa2", dark: false));
        Assert.Equal(AccentColors.Derive("#d49cf0", dark: true), AccentColors.Derive("#d49cf0", dark: true));
    }

    [Theory]
    [InlineData("#ffd400", false)] // yellow on white
    [InlineData("#5a6b80", true)]  // slate on the dark background
    public void A_hard_to_read_color_gets_a_shade_that_passes(string color, bool dark)
    {
        var check = AccentColors.Check(color, dark);
        Assert.False(check.Passes);
        Assert.NotNull(check.Suggested);
        Assert.True(AccentColors.Check(check.Suggested!, dark).Passes);
    }

    [Fact]
    public async Task A_hard_to_read_color_can_still_be_kept_and_is_recorded()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        var saved = await owner.PutAsJsonAsync("/api/admin/branding",
            Form(accent: "brand", theme: "light", light: "#ffd400"));
        saved.EnsureSuccessStatusCode();

        var settings = await saved.Content.ReadFromJsonAsync<SettingsDto>();
        Assert.Equal("#ffd400", settings!.AccentLight);
        Assert.False(settings.Checks.Single().Passes);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogs.Where(a => a.Action == "branding.changed").ToListAsync();
        Assert.Contains(entry, e => e.MetadataJson!.Contains("ContrastOverridden"));
    }

    [Fact]
    public async Task A_custom_accent_needs_a_color_for_each_mode_people_can_be_in()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        // Both themes allowed, only a light color: refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/admin/branding",
            Form(accent: "brand", light: "#7a1fa2"))).StatusCode);
        // Light only: the light color is enough.
        (await owner.PutAsJsonAsync("/api/admin/branding",
            Form(accent: "brand", theme: "light", light: "#7a1fa2"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_color_that_is_not_a_color_is_refused()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/admin/branding",
            Form(accent: "brand", theme: "light", light: "#123;}*{color:red"))).StatusCode);
    }

    [Fact]
    public void The_accent_stylesheet_contains_only_colors()
    {
        var css = AccentColors.Stylesheet("#7a1fa2", "#d49cf0");
        var values = Regex.Matches(css, ":(#[0-9a-f]{6});").Count;
        Assert.True(values >= 14);
        Assert.DoesNotContain("url(", css);
        Assert.DoesNotContain("@import", css);
    }

    // ---- SVG ---------------------------------------------------------------

    public static TheoryData<string> HostileSvgs => new()
    {
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><script>alert(1)</script><rect width="1" height="1"/></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)" viewBox="0 0 1 1"><rect width="1" height="1"/></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><foreignObject><body xmlns="http://www.w3.org/1999/xhtml"><script>alert(1)</script></body></foreignObject></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1 1"><a xlink:href="javascript:alert(1)"><rect width="1" height="1"/></a></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 1 1"><use xlink:href="https://evil.example/x.svg#a"/></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><rect width="1" height="1" style="fill:url(https://evil.example/track)"/></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><style>@import url(https://evil.example/x.css);</style><rect width="1" height="1"/></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><image href="https://evil.example/pixel.png" width="1" height="1"/></svg>""",
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1"><rect width="1" height="1" fill="url(data:image/svg+xml;base64,AAAA)"/></svg>""",
    };

    [Theory]
    [MemberData(nameof(HostileSvgs))]
    public void A_hostile_svg_comes_out_inert(string svg)
    {
        var clean = SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(svg)).Svg;
        foreach (var bad in new[] { "script", "onload", "foreignObject", "javascript:", "evil.example", "@import", "<image", "data:" })
            Assert.DoesNotContain(bad, clean, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><svg xmlns="http://www.w3.org/2000/svg"><text>&xxe;</text></svg>""")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;">]><svg xmlns="http://www.w3.org/2000/svg"><text>&lol2;</text></svg>""")]
    [InlineData("""<html><body><svg xmlns="http://www.w3.org/2000/svg"></svg></body></html>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><rect></svg>""")]
    public void A_document_that_is_not_a_plain_svg_is_refused(string svg) =>
        Assert.Throws<SvgRejectedException>(() => SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(svg)));

    [Fact]
    public void A_benign_logo_keeps_its_shapes_and_size()
    {
        var result = SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(GoodSvg));
        Assert.Contains("<rect", result.Svg);
        Assert.Contains("Acme", result.Svg);
        Assert.Contains("fill=\"#0c66e4\"", result.Svg);
        Assert.Equal(200, result.Width);
        Assert.Equal(50, result.Height);
    }

    [Fact]
    public void Internal_references_survive()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 10 10"><defs><linearGradient id="g"><stop offset="0" stop-color="#fff"/></linearGradient><path id="p" d="M0 0h1"/></defs><rect width="10" height="10" fill="url(#g)"/><use xlink:href="#p"/></svg>""";
        var clean = SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(svg)).Svg;
        Assert.Contains("url(#g)", clean);
        Assert.Contains("#p", clean);
    }

    // ---- Uploads and serving -----------------------------------------------

    [Fact]
    public async Task An_svg_logo_is_served_sandboxed()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        (await owner.PutAsync("/api/admin/branding/logo", Upload(Encoding.UTF8.GetBytes(GoodSvg), "logo.svg", "image/svg+xml")))
            .EnsureSuccessStatusCode();
        var b = await BrandingAsync(owner);
        Assert.Equal("svg", b.Logo!.Format);
        Assert.True(b.HasIdentity);

        var anonymous = factory.CreateClient();
        var res = await anonymous.GetAsync(b.Logo.Url);
        res.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", res.Content.Headers.ContentType!.MediaType);
        Assert.Contains("sandbox", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task A_raster_logo_keeps_its_shape_and_is_re_encoded()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        var upload = await owner.PutAsync("/api/admin/branding/logo", Upload(Png(800, 200), "logo.png", "image/png"));
        upload.EnsureSuccessStatusCode();
        var b = await BrandingAsync(owner);
        Assert.Equal("webp", b.Logo!.Format);
        // 800x200 fits within 1024x256 unchanged; wider than tall stays so.
        Assert.Equal(800, b.Logo.Width);
        Assert.Equal(200, b.Logo.Height);

        var served = await (await owner.GetAsync(b.Logo.Url)).Content.ReadAsByteArrayAsync();
        using var decoded = SKBitmap.Decode(served);
        Assert.Equal(800, decoded.Width);
        Assert.Equal(200, decoded.Height);
    }

    [Fact]
    public async Task A_large_raster_logo_is_scaled_into_the_box()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        (await owner.PutAsync("/api/admin/branding/logo", Upload(Png(2000, 1000), "logo.png", "image/png"))).EnsureSuccessStatusCode();
        var b = await BrandingAsync(owner);
        Assert.Equal(512, b.Logo!.Width);
        Assert.Equal(256, b.Logo.Height);
    }

    [Fact]
    public async Task A_gif_is_refused()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        // "GIF89a", a 1x1 logical screen, and a terminator: enough for the codec to say GIF.
        byte[] gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0, 0, 0, 0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0, 0x3b];
        var res = await owner.PutAsync("/api/admin/branding/logo", Upload(gif, "logo.gif", "image/gif"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_decompression_bomb_is_refused_before_it_is_decoded()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        var res = await owner.PutAsync("/api/admin/branding/logo", Upload(PngHeader(20_000, 20_000), "bomb.png", "image/png"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_svg_favicon_is_kept_and_drawn_at_every_size()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);

        var favicon = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#a53a7f"/></svg>""";
        (await owner.PutAsync("/api/admin/branding/favicon", Upload(Encoding.UTF8.GetBytes(favicon), "icon.svg", "image/svg+xml")))
            .EnsureSuccessStatusCode();

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/branding/favicon.svg")).StatusCode);
        foreach (var size in new[] { 32, 180, 512 })
        {
            var png = await (await anonymous.GetAsync($"/api/branding/favicon-{size}.png")).Content.ReadAsByteArrayAsync();
            using var decoded = SKBitmap.Decode(png);
            Assert.Equal(size, decoded.Width);
            // Drawn, not blank: the center is the circle's color.
            Assert.NotEqual(0, decoded.GetPixel(size / 2, size / 2).Alpha);
        }
    }

    [Fact]
    public async Task A_png_favicon_has_no_svg()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        (await owner.PutAsync("/api/admin/branding/favicon", Upload(Png(64, 64), "icon.png", "image/png"))).EnsureSuccessStatusCode();

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/branding/favicon.svg")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/branding/favicon-32.png")).StatusCode);
    }

    [Fact]
    public async Task Reset_puts_back_Tesria_and_keeps_the_instance_name()
    {
        using var factory = new TestAppFactory();
        var owner = await OwnerAsync(factory);
        (await owner.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Acme Docs" })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync("/api/admin/branding", Form("Acme", theme: "dark"))).EnsureSuccessStatusCode();
        (await owner.PutAsync("/api/admin/branding/logo", Upload(Encoding.UTF8.GetBytes(GoodSvg), "logo.svg", "image/svg+xml")))
            .EnsureSuccessStatusCode();

        (await owner.PostAsJsonAsync("/api/admin/branding/reset", new { })).EnsureSuccessStatusCode();

        var instance = await owner.GetFromJsonAsync<InstanceDto>("/api/instance");
        Assert.Equal("Acme Docs", instance!.InstanceName);
        Assert.Equal("Tesria", instance.Branding.Name);
        Assert.Null(instance.Branding.Logo);
        Assert.Equal("any", instance.Branding.ThemePolicy);
        // The file is gone, not just unreferenced.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/branding/logo")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "branding.reset"));
    }

    // ---- Exports -----------------------------------------------------------

    [Fact]
    public void An_export_shows_the_logo_as_an_image_never_as_markup()
    {
        var brand = new SiteChrome.Brand("Acme", "data:image/svg+xml;base64,AAAA")
        {
            Display = "logo", LogoWidth = 200, LogoHeight = 50,
        };
        var bar = SiteChrome.Topbar(brand, homeHref: null);
        Assert.Contains("<img class=\"brand__logo\" src=\"data:image/svg+xml;base64,AAAA\"", bar);
        Assert.Contains("alt=\"Acme\"", bar);
        // Logo only: the name is not printed beside it.
        Assert.DoesNotContain("brand__word", bar);
    }

    [Fact]
    public void An_export_hides_the_appearance_menu_when_everything_is_locked()
    {
        var brand = new SiteChrome.Brand("Acme")
        {
            Attributes = [("data-theme-lock", "dark"), ("data-accent-lock", "green")],
        };
        Assert.DoesNotContain("theme-menu", SiteChrome.Topbar(brand, homeHref: null));
    }

    [Fact]
    public void A_retired_accent_reads_as_blue()
    {
        // Teal was an accent until 2026-09-26; an instance that chose it
        // gets the default rather than no accent at all.
        Assert.Equal("blue", BrandView.From(new SiteSettings { AccentName = "teal" }).EffectiveAccent);
        Assert.Equal("green", BrandView.From(new SiteSettings { AccentName = "green" }).EffectiveAccent);
    }

    [Fact]
    public void Tesrias_own_name_is_its_wordmark_and_an_instances_name_is_its_own()
    {
        Assert.Contains("brand__word--tesria", SiteChrome.Topbar(new SiteChrome.Brand("Tesria"), homeHref: null));
        var acme = SiteChrome.Topbar(new SiteChrome.Brand("Acme"), homeHref: null);
        Assert.Contains("class=\"brand__word\"", acme);
        Assert.DoesNotContain("brand__word--tesria", acme);
    }

    [Fact]
    public void An_export_replaces_whatever_branding_the_capture_carried()
    {
        var captured = """<!doctype html><html lang="en" data-theme="dark" data-accent="brand" data-theme-lock="dark" data-brand-favicon="1"><head><title>Tesria</title><link rel="icon" type="image/svg+xml" href="/api/branding/favicon.svg?v=1" /><style id="brand-accent">:root{--primary:#000000;}</style></head><body><div class="export"></div></body></html>""";
        var brand = new SiteChrome.Brand("Acme")
        {
            FaviconHref = "assets/favicon.svg", FaviconType = "image/svg+xml",
            AccentCss = ":root{--primary:#7a1fa2;}",
            Attributes = [("data-accent-default", "brand")],
        };

        var html = SiteChrome.ApplyToDocument(captured, brand, "Acme Docs - Eng / Page", "eng/page");

        Assert.Contains("<title>Acme Docs - Eng / Page</title>", html);
        Assert.DoesNotContain("/api/branding", html);
        Assert.DoesNotContain("#000000", html);
        Assert.Contains("#7a1fa2", html);
        Assert.Contains("href=\"../../assets/favicon.svg\"", html);
        Assert.Contains("data-accent-default=\"brand\"", html);
        Assert.DoesNotContain("data-theme-lock", html);
        Assert.DoesNotContain("data-theme=\"dark\"", html);
        Assert.Single(Regex.Matches(html, "brand-accent"));
    }

    /// <summary>A PNG signature and an IHDR chunk claiming the given size, with no pixel data at all.</summary>
    private static byte[] PngHeader(int width, int height)
    {
        var ihdr = new List<byte>();
        ihdr.AddRange("IHDR"u8.ToArray());
        ihdr.AddRange(BigEndian(width));
        ihdr.AddRange(BigEndian(height));
        ihdr.AddRange(new byte[] { 8, 6, 0, 0, 0 });
        var bytes = new List<byte> { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        bytes.AddRange(BigEndian(13));
        bytes.AddRange(ihdr);
        bytes.AddRange(BigEndian((int)Crc32(ihdr.ToArray())));
        return bytes.ToArray();

        static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        static uint Crc32(byte[] data)
        {
            var crc = 0xffffffffu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
