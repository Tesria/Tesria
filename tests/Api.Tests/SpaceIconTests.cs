using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tesria.Api.Features.Spaces;
using SkiaSharp;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>Space icons (dev-plan 6): emoji, uploaded pictures, and who may change them.</summary>
public class SpaceIconTests
{
    private record RegisteredDto(Guid Id);
    private record SpaceDto(Guid Id, string Key, string Name, int IconKind, string? IconValue, int? IconColor);
    private record IconDto(string IconHash);

    private const int None = 0, Emoji = 1, Image = 2;
    private const int UserPrincipal = 0, AdminOperation = 2;

    // Written as escapes rather than literals: a test file should not carry
    // invisible zero-width joiners and variation selectors.
    private const string Dragon = "\U0001F409";
    private const string Astronaut = "\U0001F469\U0001F3FD\u200D\U0001F680";   // ZWJ sequence with a skin tone
    private const string Keycap = "1\uFE0F\u20E3";         // contains an ASCII digit, and is still an emoji
    private const string Check = "\u2705";
    private const string Unknown = "\U0001FAE9";        // a codepoint this app has never heard of

    private static async Task<RegisteredDto> RegisterAsync(HttpClient client, string email) =>
        (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<RegisteredDto>())!;

    private static Task<HttpResponseMessage> SetIconAsync(HttpClient c, string key, object icon) =>
        c.PutAsJsonAsync($"/api/spaces/{key}", icon);

    /// <summary>A real PNG, drawn here rather than pasted as base64 so it is
    /// certainly decodable and its size is visible in the test.</summary>
    private static byte[] PngBytes(int width = 300, int height = 200)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadIconAsync(
        HttpClient c, string key, byte[]? bytes = null, string name = "icon.png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", name);
        return await c.PutAsync($"/api/media/space-icons/{key}", form);
    }

    private static async Task<HttpClient> SpaceOwnerAsync(TestAppFactory factory, string key = "DOCS")
    {
        var client = factory.CreateClient();
        await RegisterAsync(client, "a@example.com");
        await client.CreateSpaceAsync(key);
        return client;
    }

    [Fact]
    public async Task A_new_space_has_the_generated_icon()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        var space = await client.GetFromJsonAsync<SpaceDto>("/api/spaces/DOCS");
        // Not "no icon": None means the key's first letter on a tile, so a
        // space is never iconless.
        Assert.Equal(None, space!.IconKind);
        Assert.Null(space.IconValue);
        Assert.Null(space.IconColor);
    }

    [Fact]
    public async Task An_emoji_and_a_colour_are_stored_and_returned()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        var updated = await (await SetIconAsync(client, "DOCS",
            new { Name = "Docs", IconKind = Emoji, IconValue = Dragon, IconColor = 5 }))
            .Content.ReadFromJsonAsync<SpaceDto>();

        Assert.Equal(Emoji, updated!.IconKind);
        Assert.Equal(Dragon, updated.IconValue);
        Assert.Equal(5, updated.IconColor);

        // And it survives a round trip through the listing, which is where
        // most of the rendering happens.
        var listed = (await client.GetFromJsonAsync<List<SpaceDto>>("/api/spaces"))!.Single(s => s.Key == "DOCS");
        Assert.Equal(Dragon, listed.IconValue);
    }

    [Fact]
    public async Task Real_emoji_of_every_shape_are_accepted()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        foreach (var emoji in new[] { Dragon, Astronaut, Keycap, Check })
        {
            var res = await SetIconAsync(client, "DOCS", new { Name = "Docs", IconKind = Emoji, IconValue = emoji });
            res.EnsureSuccessStatusCode();
            Assert.Equal(emoji, (await res.Content.ReadFromJsonAsync<SpaceDto>())!.IconValue);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a very long string that is plainly not an icon at all")]
    public async Task Prose_and_markup_are_refused(string value)
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SetIconAsync(client, "DOCS", new { Name = "Docs", IconKind = Emoji, IconValue = value })).StatusCode);
    }

    [Fact]
    public void The_emoji_rule_is_about_shape_not_a_list_of_emoji()
    {
        Assert.NotNull(SpaceIcons.NormalizeEmoji("").Error);
        // Whitespace around a real emoji is trimmed rather than refused.
        Assert.Equal(Dragon, SpaceIcons.NormalizeEmoji("  " + Dragon + " ").Value);
        // An emoji this app has never heard of is still fine: the rule is
        // shape, not membership of a list that would go stale.
        Assert.Null(SpaceIcons.NormalizeEmoji(Unknown).Error);
    }

    [Fact]
    public async Task An_uploaded_picture_is_re_encoded_served_and_replaceable()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        var uploaded = await (await UploadIconAsync(client, "DOCS")).Content.ReadFromJsonAsync<IconDto>();
        Assert.False(string.IsNullOrWhiteSpace(uploaded!.IconHash));

        var space = await client.GetFromJsonAsync<SpaceDto>("/api/spaces/DOCS");
        Assert.Equal(Image, space!.IconKind);
        Assert.Equal(uploaded.IconHash, space.IconValue);

        // Served as WebP whatever went in - the re-encode is the security
        // control, so the stored bytes are always ones this app produced.
        var served = await client.GetAsync("/api/media/space-icons/DOCS");
        served.EnsureSuccessStatusCode();
        Assert.Equal("image/webp", served.Content.Headers.ContentType!.MediaType);

        // Switching to an emoji clears the picture, rather than leaving the
        // bytes behind for nobody.
        (await SetIconAsync(client, "DOCS", new { Name = "Docs", IconKind = Emoji, IconValue = Dragon }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/media/space-icons/DOCS")).StatusCode);
    }

    [Fact]
    public async Task Removing_the_picture_returns_the_generated_icon()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);
        (await UploadIconAsync(client, "DOCS")).EnsureSuccessStatusCode();

        (await client.DeleteAsync("/api/media/space-icons/DOCS")).EnsureSuccessStatusCode();

        var space = await client.GetFromJsonAsync<SpaceDto>("/api/spaces/DOCS");
        Assert.Equal(None, space!.IconKind);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/media/space-icons/DOCS")).StatusCode);
    }

    [Fact]
    public async Task An_svg_is_refused_like_any_other_upload()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        // Space icons go through the same pipeline as avatars, so the
        // script-bearing format is refused here too.
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>1</script></svg>"u8.ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadIconAsync(client, "DOCS", svg, "icon.svg")).StatusCode);
    }

    [Fact]
    public async Task A_picture_cannot_be_set_through_the_json_update()
    {
        using var factory = new TestAppFactory();
        var client = await SpaceOwnerAsync(factory);

        // Otherwise a space could be pointed at an arbitrary stored key.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SetIconAsync(client, "DOCS", new { Name = "Docs", IconKind = Image, IconValue = "anything" })).StatusCode);
    }

    [Fact]
    public async Task Only_a_space_administrator_may_change_the_icon()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerId = (await RegisterAsync(owner, "owner@example.com")).Id;
        await owner.CreateSpaceAsync("DOCS");

        var other = factory.CreateClient();
        await RegisterAsync(other, "other@example.com");

        // Lock the space down to its owner: until a grant exists, spaces are
        // default-open and everyone counts as an admin.
        (await owner.PostAsJsonAsync("/api/spaces/DOCS/permissions",
            new { PrincipalType = UserPrincipal, PrincipalId = ownerId, Operation = AdminOperation }))
            .EnsureSuccessStatusCode();

        // The space is now invisible to the other user, so every icon route
        // masks it rather than admitting it exists.
        Assert.Equal(HttpStatusCode.NotFound,
            (await SetIconAsync(other, "DOCS", new { Name = "Docs", IconKind = Emoji, IconValue = Dragon })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadIconAsync(other, "DOCS")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync("/api/media/space-icons/DOCS")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/media/space-icons/DOCS")).StatusCode);
    }

    [Fact]
    public async Task A_public_spaces_icon_is_readable_by_anyone()
    {
        using var factory = new TestAppFactory();
        var admin = factory.CreateClient();
        await RegisterAsync(admin, "admin@example.com");
        (await admin.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true })).EnsureSuccessStatusCode();
        await admin.CreateSpaceAsync("PUB");
        await admin.CreateSpaceAsync("PRIV");
        (await UploadIconAsync(admin, "PUB")).EnsureSuccessStatusCode();
        (await UploadIconAsync(admin, "PRIV")).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/admin/spaces/PUB/public", new { IsPublic = true })).EnsureSuccessStatusCode();

        // A reader with no account sees the published space's icon (dev-plan
        // 5.3 renders it in the public listing) and nothing of the other.
        var anon = factory.CreateClient();
        (await anon.GetAsync("/api/media/space-icons/PUB")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/media/space-icons/PRIV")).StatusCode);
    }
}
