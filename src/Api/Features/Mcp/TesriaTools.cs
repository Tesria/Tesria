using System.ComponentModel;
using ModelContextProtocol.Server;
using Tesria.Api.Domain;
using Tesria.Api.Features.Blocks;
using Tesria.Api.Features.Export;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// The MCP tool surface (architecture.md, "MCP server"). Every tool goes
/// through the same <see cref="IPermissionService"/> the REST endpoints do,
/// so an assistant sees and changes exactly what its token's owner could.
/// </summary>
[McpServerToolType]
public sealed class TesriaTools
{
    public sealed record SpaceSummary(string Key, string Name, string? Description, bool IsPublic);
    public sealed record PageContent(
        Guid Id, string Title, string SpaceKey, Guid? ParentPageId, IReadOnlyList<string> Labels,
        int Version, DateTimeOffset UpdatedAt, string Url, string Format, string Content);

    [McpServerTool(Name = "list_spaces"), Description("The spaces this token's owner may view.")]
    public static async Task<IReadOnlyList<SpaceSummary>> ListSpaces(
        AppDbContext db, IPermissionService perms, CancellationToken ct)
    {
        var visible = await perms.ViewableSpaceIdsAsync();
        var spaces = await db.Spaces.AsNoTracking()
            .Where(s => !s.Archived && visible.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new SpaceSummary(s.Key, s.Name, s.Description, s.IsPublic))
            .ToListAsync(ct);
        return spaces;
    }

    [McpServerTool(Name = "get_page"), Description(
        "A page by id. Returns its content as Markdown — the same Markdown the export produces, with live " +
        "blocks (children lists, recently-updated tables, task reports…) resolved as this token's owner would see " +
        "them. Ask for format 'json' to get the editor's ProseMirror document instead, e.g. to copy a page exactly. " +
        "'Not found' can mean the page does not exist or that you may not see it; the two are deliberately indistinguishable.")]
    public static async Task<PageContent> GetPage(
        [Description("The page id (a GUID).")] Guid pageId,
        AppDbContext db, IPermissionService perms, IDynamicBlockService blocks,
        ISiteSettingsService settings, IConfiguration config, CancellationToken ct,
        [Description("'markdown' (default) or 'json'.")] string format = "markdown")
    {
        var page = await db.Pages.AsNoTracking()
            .Include(p => p.CurrentVersion).Include(p => p.Space)
            .FirstOrDefaultAsync(p => p.Id == pageId, ct);
        // Existence and permission are one answer, as they are for REST.
        if (page?.CurrentVersion is null || page.Space is null || !await perms.CanViewPageAsync(pageId))
            throw McpAccess.NotFound("Page");

        var labels = await db.PageLabels.AsNoTracking()
            .Where(pl => pl.PageId == pageId)
            .Select(pl => pl.Label!.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        var json = page.CurrentVersion.ContentJson;
        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
        var wantJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
        var content = wantJson
            ? json
            : ProseMirrorRenderer.ToMarkdown(json, await PageSnapshots.BlocksAsync(page.Id, json, blocks, ct), baseUrl);

        return new PageContent(
            page.Id, page.Title, page.Space.Key, page.ParentPageId, labels,
            page.CurrentVersion.VersionNumber, page.UpdatedAt,
            $"{baseUrl}/spaces/{page.Space.Key}/pages/{page.Id}",
            wantJson ? "json" : "markdown", content);
    }
}
