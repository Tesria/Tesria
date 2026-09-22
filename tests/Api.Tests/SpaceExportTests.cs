using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Turning a space's exports off, format by format (dev-plan 12.3). What
/// matters: every format is on until someone turns it off; only holders of
/// the right can; once off it is refused for everyone, the owner included, on
/// every route that produces it; and the change is audited.
/// </summary>
public class SpaceExportTests
{
    private record Exports(bool Markdown, bool Html, bool Pdf, bool Site, bool Pack);
    private record SpaceDto(Guid Id, string Key, string Name, Exports Exports);
    private record PageDto(Guid Id);
    private record ErrorDto(string Code, string Message);

    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"secret"}]}]}""";

    private static readonly Exports AllOn = new(true, true, true, true, true);

    /// <summary>The owner, a space called SENS with one page in it.</summary>
    private static async Task<(HttpClient Owner, SpaceDto Space, Guid PageId)> SpaceAsync(TestAppFactory factory)
    {
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync("SENS");
        var page = await (await owner.PostAsJsonAsync("/api/pages",
            new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = "Plans", ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDto>();
        var space = await owner.GetFromJsonAsync<SpaceDto>("/api/spaces/SENS");
        return (owner, space!, page!.Id);
    }

    [Fact]
    public void The_right_is_the_administrators_by_default()
    {
        var right = InstancePermissions.All.Single(p => p.Key == InstancePermissions.SpacesExports);
        Assert.Equal(UserRole.Admin, right.DefaultFrom);
        Assert.Equal(PermissionScope.Administration, right.Scope);
    }

    [Fact]
    public async Task Every_export_is_on_until_someone_turns_it_off()
    {
        using var factory = new TestAppFactory();
        var (owner, space, pageId) = await SpaceAsync(factory);

        Assert.Equal(AllOn, space.Exports);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/pages/{pageId}/export?format=markdown")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/spaces/SENS/export/pack")).StatusCode);
    }

    [Fact]
    public async Task A_member_cannot_change_them_even_for_a_space_they_created()
    {
        using var factory = new TestAppFactory();
        await SpaceAsync(factory);
        var member = factory.CreateClient();
        await member.RegisterAndSignInAsync();
        await member.CreateSpaceAsync("MINE");

        var res = await member.PutAsJsonAsync("/api/spaces/MINE/exports", AllOn with { Markdown = false });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_format_turned_off_is_refused_for_everyone_the_owner_included()
    {
        using var factory = new TestAppFactory();
        var (owner, _, pageId) = await SpaceAsync(factory);

        var saved = await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Markdown = false });
        saved.EnsureSuccessStatusCode();
        Assert.False((await saved.Content.ReadFromJsonAsync<SpaceDto>())!.Exports.Markdown);

        var res = await owner.GetAsync($"/api/pages/{pageId}/export?format=markdown");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<ErrorDto>();
        Assert.Equal("export_disabled", body!.Code);
        Assert.Contains("Markdown", body.Message);

        // The other page formats are untouched. No renderer runs in tests, so
        // "not refused" is 503, not 200.
        Assert.NotEqual(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/pages/{pageId}/export?format=html")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/pages/{pageId}/export?format=pdf")).StatusCode);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("pdf")]
    public async Task Html_and_pdf_are_refused_before_the_renderer_is_asked(string format)
    {
        using var factory = new TestAppFactory();
        var (owner, _, pageId) = await SpaceAsync(factory);
        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Html = false, Pdf = false })).EnsureSuccessStatusCode();

        var res = await owner.GetAsync($"/api/pages/{pageId}/export?format={format}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("export_disabled", (await res.Content.ReadFromJsonAsync<ErrorDto>())!.Code);
    }

    [Fact]
    public async Task The_website_and_the_pack_are_refused_when_off()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await SpaceAsync(factory);
        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Site = false, Pack = false })).EnsureSuccessStatusCode();

        var site = await owner.GetAsync("/api/spaces/SENS/export/site?audience=me");
        Assert.Equal(HttpStatusCode.Forbidden, site.StatusCode);
        Assert.Equal("export_disabled", (await site.Content.ReadFromJsonAsync<ErrorDto>())!.Code);

        var pack = await owner.GetAsync("/api/spaces/SENS/export/pack");
        Assert.Equal(HttpStatusCode.Forbidden, pack.StatusCode);

        // Turned back on, it works again.
        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/spaces/SENS/export/pack")).StatusCode);
    }

    [Fact]
    public async Task Another_space_is_not_affected()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await SpaceAsync(factory);
        await owner.CreateSpaceAsync("OPEN");
        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Pack = false })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/spaces/OPEN/export/pack")).StatusCode);
    }

    [Fact]
    public async Task A_change_is_audited_with_before_and_after()
    {
        using var factory = new TestAppFactory();
        var (owner, space, _) = await SpaceAsync(factory);

        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Site = false })).EnsureSuccessStatusCode();
        // Saving the same thing again changes nothing, and records nothing.
        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Site = false })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.AuditLogs.Where(a => a.Action == "space.exports_changed").ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(space.Id, entry.TargetId);
        Assert.Contains("\"Site\":false", entry.MetadataJson);
        Assert.Contains("\"Site\":true", entry.MetadataJson);
    }

    [Fact]
    public async Task A_space_the_caller_cannot_see_is_not_found()
    {
        using var factory = new TestAppFactory();
        var (owner, _, _) = await SpaceAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync("/api/spaces/NOSUCH/exports", AllOn)).StatusCode);
    }

    [Fact]
    public async Task An_anonymous_reader_of_a_public_space_is_refused_too()
    {
        using var factory = new TestAppFactory();
        var (owner, _, pageId) = await SpaceAsync(factory);
        (await owner.PutAsJsonAsync("/api/admin/settings", new { AllowPublicSpaces = true })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync("/api/admin/spaces/SENS/public", new { IsPublic = true })).EnsureSuccessStatusCode();
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/pages/{pageId}/export?format=markdown")).StatusCode);

        (await owner.PutAsJsonAsync("/api/spaces/SENS/exports", AllOn with { Markdown = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/pages/{pageId}/export?format=markdown")).StatusCode);
    }
}
