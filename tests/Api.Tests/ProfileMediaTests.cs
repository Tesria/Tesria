using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Profile media (dev-plan 0.4). The point of the pipeline is that the stored
/// file is never the uploaded one: re-encoding strips EXIF, defeats polyglot
/// files, and bounds decoded dimensions.
/// </summary>
public class ProfileMediaTests
{
    private record AvatarDto(string AvatarHash);
    private record UserDto(Guid Id, string Email, string DisplayName, int Role, string? AvatarHash, int? AvatarVariant);

    /// <summary>An <paramref name="width"/>×<paramref name="height"/> PNG.</summary>
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string filename, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", filename } };
    }

    [Fact]
    public async Task An_uploaded_avatar_is_re_encoded_to_a_fixed_square_and_served_back()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();

        // Deliberately non-square and larger than the output size.
        var res = await client.PutAsync("/api/media/avatars/me", Upload(Png(600, 400), "me.png", "image/png"));
        res.EnsureSuccessStatusCode();
        var hash = (await res.Content.ReadFromJsonAsync<AvatarDto>())!.AvatarHash;
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var fetched = await client.GetAsync($"/api/media/avatars/{userId}");
        fetched.EnsureSuccessStatusCode();
        Assert.Equal("image/webp", fetched.Content.Headers.ContentType?.MediaType);

        var served = await fetched.Content.ReadAsByteArrayAsync();
        using var decoded = SKBitmap.Decode(served);
        Assert.Equal(ProfileMediaService.OutputSize, decoded.Width);
        Assert.Equal(ProfileMediaService.OutputSize, decoded.Height);

        // The stored bytes are this server's WebP, not the PNG that was sent.
        Assert.NotEqual(Png(600, 400), served);
    }

    [Fact]
    public async Task The_avatar_hash_changes_when_the_image_does_so_the_url_busts_its_cache()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var first = await (await client.PutAsync("/api/media/avatars/me",
            Upload(Png(300, 300), "a.png", "image/png"))).Content.ReadFromJsonAsync<AvatarDto>();
        var second = await (await client.PutAsync("/api/media/avatars/me",
            Upload(Png(301, 240), "b.png", "image/png"))).Content.ReadFromJsonAsync<AvatarDto>();

        Assert.NotEqual(first!.AvatarHash, second!.AvatarHash);

        // /auth/me carries it, so the SPA can build the URL from the session.
        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal(second.AvatarHash, me!.AvatarHash);
    }

    [Fact]
    public async Task Replacing_an_avatar_overwrites_rather_than_accumulating_files()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();

        await client.PutAsync("/api/media/avatars/me", Upload(Png(300, 300), "a.png", "image/png"));
        await client.PutAsync("/api/media/avatars/me", Upload(Png(400, 400), "b.png", "image/png"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).AvatarKey;

        // One deterministic key per user, so the second upload replaced the first.
        Assert.Equal($"avatars/{userId}.webp", key);
    }

    [Fact]
    public async Task Svg_is_rejected_however_it_is_labelled()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var svg = System.Text.Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        // Content type is attacker-controlled, so the check sniffs the bytes:
        // claiming image/png must not get an SVG past it.
        var lying = await client.PutAsync("/api/media/avatars/me", Upload(svg, "x.png", "image/png"));
        Assert.Equal(HttpStatusCode.BadRequest, lying.StatusCode);

        var honest = await client.PutAsync("/api/media/avatars/me", Upload(svg, "x.svg", "image/svg+xml"));
        Assert.Equal(HttpStatusCode.BadRequest, honest.StatusCode);

        var declared = System.Text.Encoding.UTF8.GetBytes(
            """<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg"></svg>""");
        var withProlog = await client.PutAsync("/api/media/avatars/me", Upload(declared, "x.png", "image/png"));
        Assert.Equal(HttpStatusCode.BadRequest, withProlog.StatusCode);
    }

    [Fact]
    public async Task Non_images_and_oversized_uploads_are_rejected()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        var notAnImage = await client.PutAsync("/api/media/avatars/me",
            Upload("this is not an image"u8.ToArray(), "x.png", "image/png"));
        Assert.Equal(HttpStatusCode.BadRequest, notAnImage.StatusCode);

        var tooBig = await client.PutAsync("/api/media/avatars/me",
            Upload(new byte[ProfileMediaService.MaxBytes + 1], "big.png", "image/png"));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_avatar_clears_it_and_serving_falls_back_to_not_found()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();

        await client.PutAsync("/api/media/avatars/me", Upload(Png(300, 300), "a.png", "image/png"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/media/avatars/{userId}")).StatusCode);

        (await client.DeleteAsync("/api/media/avatars/me")).EnsureSuccessStatusCode();

        // 404 so the client falls back to the generated default (dev-plan 1.2).
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/media/avatars/{userId}")).StatusCode);

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Null(me!.AvatarHash);
    }

    [Fact]
    public async Task An_avatar_is_readable_by_other_signed_in_users_but_not_anonymously()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        await owner.PutAsync("/api/media/avatars/me", Upload(Png(300, 300), "a.png", "image/png"));

        // Shown next to every comment and page version, so gating per viewer
        // would gate nothing while costing a check on each render.
        var other = factory.CreateClient();
        await other.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync($"/api/media/avatars/{ownerId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync($"/api/media/avatars/{ownerId}")).StatusCode);
    }

    [Fact]
    public async Task A_generated_avatar_variant_can_be_chosen_and_cleared()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        // Null by default: the client derives one from the user id, so every
        // account has an avatar with no row written and no file stored.
        Assert.Null((await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.AvatarVariant);

        (await client.PutAsJsonAsync("/api/media/avatars/me/variant", new { Variant = 7 }))
            .EnsureSuccessStatusCode();
        Assert.Equal(7, (await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.AvatarVariant);

        // Clearing returns to the derived one.
        (await client.PutAsJsonAsync("/api/media/avatars/me/variant", new { Variant = (int?)null }))
            .EnsureSuccessStatusCode();
        Assert.Null((await client.GetFromJsonAsync<UserDto>("/api/auth/me"))!.AvatarVariant);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/media/avatars/me/variant", new { Variant = -1 })).StatusCode);
    }

    [Fact]
    public async Task An_uploaded_picture_wins_over_a_chosen_variant()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();

        await client.PutAsJsonAsync("/api/media/avatars/me/variant", new { Variant = 3 });
        await client.PutAsync("/api/media/avatars/me", Upload(Png(300, 300), "a.png", "image/png"));

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        // Both are reported; the client prefers the hash, and the variant is
        // kept so removing the picture returns to the colour they picked.
        Assert.NotNull(me!.AvatarHash);
        Assert.Equal(3, me.AvatarVariant);

        (await client.DeleteAsync("/api/media/avatars/me")).EnsureSuccessStatusCode();
        var after = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Null(after!.AvatarHash);
        Assert.Equal(3, after.AvatarVariant);
    }

    [Fact]
    public async Task The_user_directory_carries_avatars_so_pickers_can_show_them()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();
        await client.PutAsync("/api/media/avatars/me", Upload(Png(300, 300), "a.png", "image/png"));

        var directory = await client.GetFromJsonAsync<List<DirectoryDto>>("/api/users");
        var self = directory!.Single(u => u.Id == userId);
        Assert.False(string.IsNullOrEmpty(self.AvatarHash));
    }

    private record DirectoryDto(Guid Id, string Email, string DisplayName, string? AvatarHash, int? AvatarVariant);

    [Fact]
    public async Task A_user_with_no_avatar_and_an_unknown_user_are_indistinguishable()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();

        // Both 404: this endpoint must not become a probe for which ids exist.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/media/avatars/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/media/avatars/{Guid.NewGuid()}")).StatusCode);
    }
}
