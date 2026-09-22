using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Author identity on comments and version history (dev-plan 1.5). Both
/// previously returned a bare id, so neither could show who did anything.
/// </summary>
public class AuthorIdentityTests
{
    private record PageDto(Guid Id, string Title);
    private record CommentDto(
        Guid Id, Guid PageId, Guid? ParentCommentId, string? Body, string? AnchorJson,
        Guid AuthorId, string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant,
        bool IsInline, bool IsDeleted, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private record VersionDto(
        Guid Id, int VersionNumber, string? ChangeComment, Guid AuthorId,
        string AuthorName, string? AuthorAvatarHash, int? AuthorAvatarVariant, DateTimeOffset CreatedAt);

    private const string Doc = """{"type":"doc","content":[]}""";

    private static async Task<Guid> CreatePageAsync(HttpClient client, Guid spaceId)
    {
        var res = await client.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Page", ContentJson = Doc });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PageDto>())!.Id;
    }

    [Fact]
    public async Task Comments_carry_their_author_name_and_avatar()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();
        await client.PutAsJsonAsync("/api/auth/me", new { DisplayName = "Ada Lovelace" });

        var spaceId = await client.CreateSpaceAsync();
        var pageId = await CreatePageAsync(client, spaceId);

        var created = await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "First", ParentCommentId = (Guid?)null, AnchorJson = (string?)null });
        created.EnsureSuccessStatusCode();
        // The create response is rendered immediately by the client, so it must
        // carry the author too, not only the later list.
        Assert.Equal("Ada Lovelace", (await created.Content.ReadFromJsonAsync<CommentDto>())!.AuthorName);

        var list = await client.GetFromJsonAsync<List<CommentDto>>($"/api/pages/{pageId}/comments");
        var comment = list!.Single();
        Assert.Equal(userId, comment.AuthorId);
        Assert.Equal("Ada Lovelace", comment.AuthorName);
        Assert.Null(comment.AuthorAvatarHash); // no upload; the client generates one
    }

    [Fact]
    public async Task Version_history_carries_its_author()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        await client.PutAsJsonAsync("/api/auth/me", new { DisplayName = "Grace Hopper" });

        var spaceId = await client.CreateSpaceAsync();
        var pageId = await CreatePageAsync(client, spaceId);
        await client.PutAsJsonAsync($"/api/pages/{pageId}",
            new { Title = "Page", ContentJson = Doc, ChangeComment = "tweak" });

        var versions = await client.GetFromJsonAsync<List<VersionDto>>($"/api/pages/{pageId}/versions");
        Assert.Equal(2, versions!.Count);
        Assert.All(versions, v => Assert.Equal("Grace Hopper", v.AuthorName));

        // The single-version endpoint feeds the preview pane, so it needs the
        // same fields or VersionContent's shape would be a lie.
        var one = await client.GetFromJsonAsync<VersionDto>($"/api/pages/{pageId}/versions/1");
        Assert.Equal("Grace Hopper", one!.AuthorName);
    }

    [Fact]
    public async Task An_anonymised_author_flows_through_to_the_response()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var userId = await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var pageId = await CreatePageAsync(client, spaceId);
        await client.PostAsJsonAsync($"/api/pages/{pageId}/comments",
            new { Body = "Still here", ParentCommentId = (Guid?)null, AnchorJson = (string?)null });

        // What dev-plan 2.2 will do: keep the row and blank the identity, so
        // authorship survives. It cannot orphan the comment: the foreign key
        // forbids that, which is why the "Deleted user" fallback in the
        // projection is defensive only and not the mechanism relied on here.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DisplayName, "Deleted user"));
        }

        var list = await client.GetFromJsonAsync<List<CommentDto>>($"/api/pages/{pageId}/comments");
        var comment = list!.Single();
        Assert.Equal("Deleted user", comment.AuthorName);
        // The id is retained, so history stays attributable even when the name
        // no longer identifies anyone.
        Assert.Equal(userId, comment.AuthorId);
    }
}
