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
using ModelContextProtocol;
using Tesria.Api.Features.Pages;
using Tesria.Api.Features.Search;
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
        int Version, DateTimeOffset UpdatedAt, string Url, string Format, string Content,
        IReadOnlyList<PageSections.Heading> Outline, string? Section);

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
        "A page by id. Returns its content as Markdown: the same Markdown the export produces, with live " +
        "blocks (children lists, recently-updated tables, task reports…) resolved as this token's owner would see " +
        "them, plus an `outline` of its headings. Pass `section` with a heading id from that outline to get just " +
        "that heading and everything under it, which is usually what you want on a long page. Ask for format " +
        "'json' to get the editor's ProseMirror document instead, e.g. to copy a page exactly. " +
        "'Not found' can mean the page does not exist or that you may not see it; the two are deliberately indistinguishable.")]
    public static async Task<PageContent> GetPage(
        [Description("The page id (a GUID).")] Guid pageId,
        AppDbContext db, IPermissionService perms, IDynamicBlockService blocks,
        ISiteSettingsService settings, IConfiguration config, CancellationToken ct,
        [Description("'markdown' (default) or 'json'.")] string format = "markdown",
        [Description("A heading id from this page's outline; returns only that section.")] string? section = null)
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
        var outline = PageSections.Outline(json);
        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
        var wantJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

        var wanted = section?.Trim();
        if (!string.IsNullOrEmpty(wanted))
        {
            var slice = PageSections.Extract(json, wanted);
            if (slice is null)
                throw new McpException(
                    $"This page has no top-level section '{wanted}'. Its sections are: "
                    + (outline.Count == 0 ? "(none)" : string.Join(", ", outline.Select(h => h.Id))) + ".");
            json = slice;
        }

        // Blocks are resolved against the whole page either way: a children
        // display inside a section is still that page's children.
        var content = wantJson
            ? json
            : ProseMirrorRenderer.ToMarkdown(json, await PageSnapshots.BlocksAsync(page.Id, json, blocks, ct), baseUrl);

        return new PageContent(
            page.Id, page.Title, page.Space.Key, page.ParentPageId, labels,
            page.CurrentVersion.VersionNumber, page.UpdatedAt,
            $"{baseUrl}/spaces/{page.Space.Key}/pages/{page.Id}",
            wantJson ? "json" : "markdown", content, outline,
            string.IsNullOrEmpty(wanted) ? null : wanted);
    }

    public sealed record TreeNode(Guid Id, string Title, IReadOnlyList<TreeNode> Children);
    /// <param name="Snippet">The passage that matched, with the matching words in **bold**.</param>
    /// <param name="Score">Relevance, higher is better. Null where the database cannot rank (tests).</param>
    public sealed record PageHit(Guid Id, string SpaceKey, string Title, string Snippet, double? Score);
    public sealed record PageRef(Guid Id, string SpaceKey, string Title);
    public sealed record LabelUsage(string Name, int Pages);
    public sealed record WriteResult(Guid Id, string Title, string SpaceKey, int Version, string Url);
    public sealed record LabelsResult(Guid PageId, IReadOnlyList<string> Labels);

    [McpServerTool(Name = "get_space_tree"), Description(
        "The page tree of a space, as this token's owner may see it. A page they may not view is absent, " +
        "and so is everything beneath it.")]
    public static async Task<IReadOnlyList<TreeNode>> GetSpaceTree(
        [Description("The space key, e.g. 'ENG'.")] string spaceKey,
        AppDbContext db, IPermissionService perms, CancellationToken ct)
    {
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == spaceKey.ToUpperInvariant(), ct);
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) throw McpAccess.NotFound("Space");

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.SpaceId == space.Id && p.Status == PageStatus.Current)
            .OrderBy(p => p.Position).ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.ParentPageId })
            .ToListAsync(ct);

        var visible = new HashSet<Guid>();
        foreach (var page in pages)
            if (await perms.CanViewPageAsync(page.Id)) visible.Add(page.Id);

        var byParent = pages.Where(p => visible.Contains(p.Id)).ToLookup(p => p.ParentPageId);
        List<TreeNode> Build(Guid? parent) =>
            byParent[parent].Select(p => new TreeNode(p.Id, p.Title, Build(p.Id))).ToList();
        return Build(null);
    }

    [McpServerTool(Name = "search_pages"), Description(
        "Full-text search across pages this token's owner may read. Results from spaces or pages they cannot " +
        "see are never returned.")]
    public static async Task<IReadOnlyList<PageHit>> SearchPages(
        [Description("What to search for.")] string query,
        AppDbContext db, IPermissionService perms, CancellationToken ct,
        [Description("Restrict to one space key. Omit to search everywhere you can read.")] string? spaceKey = null,
        [Description("How many results at most (1-50, default 20).")] int limit = 20)
    {
        var term = (query ?? "").Trim();
        if (term.Length == 0) return [];
        limit = Math.Clamp(limit, 1, 50);

        Guid? spaceId = null;
        if (!string.IsNullOrWhiteSpace(spaceKey))
        {
            var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == spaceKey.ToUpperInvariant(), ct);
            if (space is null || !await perms.CanViewSpaceAsync(space.Id)) throw McpAccess.NotFound("Space");
            spaceId = space.Id;
        }

        // The same two-pass filter the REST search uses: narrow by space in
        // SQL, then drop what page restrictions hide.
        var viewable = await perms.ViewableSpaceIdsAsync();
        IQueryable<Page> pages = db.Pages.AsNoTracking().Where(p => viewable.Contains(p.SpaceId));
        if (spaceId is { } id) pages = pages.Where(p => p.SpaceId == id);
        pages = db.Database.IsNpgsql()
            ? pages.Where(p => p.SearchVector!.Matches(EF.Functions.WebSearchToTsQuery("english", term)))
                   .OrderByDescending(p => p.SearchVector!.Rank(EF.Functions.WebSearchToTsQuery("english", term)))
            : pages.Where(p => EF.Functions.Like(p.SearchText, "%" + term + "%")).OrderBy(p => p.Title);

        var rows = await pages.Take(200)
            .Select(p => new { p.Id, SpaceKey = p.Space!.Key, p.Title })
            .ToListAsync(ct);

        var visible = new List<(Guid Id, string SpaceKey, string Title)>();
        foreach (var row in rows)
        {
            if (visible.Count >= limit) break;
            if (!await perms.CanViewPageAsync(row.Id)) continue;
            visible.Add((row.Id, row.SpaceKey, row.Title));
        }

        // Snippets and scores for the survivors only, one query, after the
        // permission filter, so nothing is computed for a page that will not
        // be returned.
        var matches = await SearchSnippets.ForAsync(db, visible.Select(v => v.Id).ToList(), term, ct);
        return visible
            .Select(v => matches.TryGetValue(v.Id, out var m)
                ? new PageHit(v.Id, v.SpaceKey, v.Title, m.Snippet, m.Score)
                : new PageHit(v.Id, v.SpaceKey, v.Title, "", null))
            .ToList();
    }

    [McpServerTool(Name = "find_pages_by_label"), Description("Pages carrying a label, filtered to what this token's owner may read.")]
    public static async Task<IReadOnlyList<PageRef>> FindPagesByLabel(
        [Description("The label name, lower-case.")] string label,
        AppDbContext db, IPermissionService perms, CancellationToken ct,
        [Description("Restrict to one space key.")] string? spaceKey = null)
    {
        var name = (label ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return [];

        var rows = await db.PageLabels.AsNoTracking()
            .Where(pl => pl.Label!.Name == name)
            .Join(db.Pages, pl => pl.PageId, p => p.Id, (pl, p) => p)
            .Where(p => p.Status == PageStatus.Current)
            .Select(p => new { p.Id, p.SpaceId, SpaceKey = p.Space!.Key, p.Title })
            .ToListAsync(ct);

        var viewable = await perms.ViewableSpaceIdsAsync();
        var found = new List<PageRef>();
        foreach (var row in rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!viewable.Contains(row.SpaceId)) continue;
            if (spaceKey is not null && !string.Equals(row.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!await perms.CanViewPageAsync(row.Id)) continue;
            found.Add(new PageRef(row.Id, row.SpaceKey, row.Title));
        }
        return found;
    }

    [McpServerTool(Name = "list_labels"), Description(
        "The labels in use in a space, most-used first. Counts cover only pages this token's owner may read, " +
        "so a label used solely on restricted pages does not appear at all.")]
    public static async Task<IReadOnlyList<LabelUsage>> ListLabels(
        [Description("The space key.")] string spaceKey,
        AppDbContext db, IPermissionService perms, CancellationToken ct)
    {
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == spaceKey.ToUpperInvariant(), ct);
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) throw McpAccess.NotFound("Space");

        var rows = await db.PageLabels.AsNoTracking()
            .Where(pl => pl.Page!.SpaceId == space.Id && pl.Page.Status == PageStatus.Current)
            .Select(pl => new { pl.PageId, Name = pl.Label!.Name })
            .ToListAsync(ct);

        var visible = new HashSet<Guid>();
        foreach (var pageId in rows.Select(r => r.PageId).Distinct())
            if (await perms.CanViewPageAsync(pageId)) visible.Add(pageId);

        return rows.Where(r => visible.Contains(r.PageId))
            .GroupBy(r => r.Name)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new LabelUsage(g.Key, g.Count()))
            .ToList();
    }

    // -- writes ---------------------------------------------------------------

    [McpServerTool(Name = "create_page"), Description(
        "Create a page. Give `content` as Markdown (headings, lists, tables, code fences, links, bold/italic: " +
        "the same Markdown get_page returns), or `contentJson` if you already hold an editor document. Needs a " +
        "token minted with write access.")]
    public static async Task<WriteResult> CreatePage(
        [Description("The space key to create it in.")] string spaceKey,
        [Description("The page title.")] string title,
        IPageWriter writer, AppDbContext db, IPermissionService perms, CurrentUser current,
        IHttpContextAccessor accessor, ISiteSettingsService settings, IConfiguration config, CancellationToken ct,
        [Description("Body as Markdown.")] string? content = null,
        [Description("Body as a ProseMirror JSON document. Use instead of `content`, not as well.")] string? contentJson = null,
        [Description("Create it beneath this page.")] Guid? parentPageId = null)
    {
        McpAccess.RequireWrite(current, accessor);
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Key == spaceKey.ToUpperInvariant(), ct);
        if (space is null || !await perms.CanViewSpaceAsync(space.Id)) throw McpAccess.NotFound("Space");

        var result = await writer.CreateAsync(space.Id, parentPageId, title, Body(content, contentJson), ct);
        return await ResultOf(result, db, settings, config, ct);
    }

    [McpServerTool(Name = "update_page"), Description(
        "Replace a page's body, and optionally its title. This creates a new version, as an edit in the browser " +
        "does: fetch the page first if you mean to change only part of it. Needs a token minted with write access.")]
    public static async Task<WriteResult> UpdatePage(
        [Description("The page id.")] Guid pageId,
        IPageWriter writer, AppDbContext db, CurrentUser current, IHttpContextAccessor accessor,
        ISiteSettingsService settings, IConfiguration config, CancellationToken ct,
        [Description("New body as Markdown.")] string? content = null,
        [Description("New body as a ProseMirror JSON document.")] string? contentJson = null,
        [Description("New title; omit to keep the current one.")] string? title = null,
        [Description("A short note describing the change, shown in the page's history.")] string? changeComment = null)
    {
        McpAccess.RequireWrite(current, accessor);
        var result = await writer.UpdateAsync(pageId, title, Body(content, contentJson), changeComment, ct);
        return await ResultOf(result, db, settings, config, ct);
    }

    [McpServerTool(Name = "add_page_label"), Description("Add a label to a page. Needs a token minted with write access.")]
    public static Task<LabelsResult> AddPageLabel(
        [Description("The page id.")] Guid pageId,
        [Description("The label, lower-case: letters, digits, dot, dash or underscore.")] string label,
        AppDbContext db, IPermissionService perms, CurrentUser current, IHttpContextAccessor accessor, CancellationToken ct) =>
        SetLabelAsync(pageId, label, add: true, db, perms, current, accessor, ct);

    [McpServerTool(Name = "remove_page_label"), Description("Remove a label from a page. Needs a token minted with write access.")]
    public static Task<LabelsResult> RemovePageLabel(
        [Description("The page id.")] Guid pageId,
        [Description("The label to remove.")] string label,
        AppDbContext db, IPermissionService perms, CurrentUser current, IHttpContextAccessor accessor, CancellationToken ct) =>
        SetLabelAsync(pageId, label, add: false, db, perms, current, accessor, ct);

    // -- shared ---------------------------------------------------------------

    /// <summary>Exactly one of the two content forms, converted to a stored document.</summary>
    private static string? Body(string? content, string? contentJson)
    {
        if (content is not null && contentJson is not null)
            throw new McpException("Give either `content` (Markdown) or `contentJson`, not both.");
        if (contentJson is not null) return contentJson;
        return content is null ? null : MarkdownToProseMirror.Convert(content);
    }

    private static async Task<WriteResult> ResultOf(
        PageWriteResult result, AppDbContext db, ISiteSettingsService settings, IConfiguration config, CancellationToken ct) =>
        result.Status switch
        {
            // The same masking REST uses: not viewable and not there are one answer.
            PageWriteStatus.NotFound => throw McpAccess.NotFound("Page"),
            PageWriteStatus.Forbidden => throw new McpException(result.Message ?? "You may not change that."),
            PageWriteStatus.Invalid => throw new McpException(result.Field + ": " + result.Message),
            _ => await Describe(result.Page!, result.Version!, db, settings, config, ct),
        };

    private static async Task<WriteResult> Describe(
        Page page, PageVersion version, AppDbContext db, ISiteSettingsService settings, IConfiguration config, CancellationToken ct)
    {
        var key = page.Space?.Key
            ?? await db.Spaces.AsNoTracking().Where(s => s.Id == page.SpaceId).Select(s => s.Key).FirstAsync(ct);
        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
        return new WriteResult(page.Id, page.Title, key, version.VersionNumber, baseUrl + "/spaces/" + key + "/pages/" + page.Id);
    }

    private static async Task<LabelsResult> SetLabelAsync(
        Guid pageId, string label, bool add, AppDbContext db, IPermissionService perms,
        CurrentUser current, IHttpContextAccessor accessor, CancellationToken ct)
    {
        McpAccess.RequireWrite(current, accessor);
        // Readable, not merely viewable: not a draft of someone else's, not in the trash (the 14.1 review).
        if (!await perms.CanReadPageAsync(pageId)) throw McpAccess.NotFound("Page");
        if (!await perms.CanEditPageAsync(pageId)) throw new McpException("You do not have edit rights on that page.");

        var name = (label ?? "").Trim().ToLowerInvariant();
        // The same rule the REST endpoint enforces, so a label added by an
        // assistant is one a person could have added.
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9][a-z0-9._-]{0,49}$"))
            throw new McpException("Labels are 1-50 characters: lower-case letters, digits, dot, dash or underscore, starting with a letter or digit.");

        if (add)
        {
            var entity = await db.Labels.FirstOrDefaultAsync(l => l.Name == name, ct);
            if (entity is null)
            {
                entity = new Label { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
                db.Labels.Add(entity);
            }
            if (!await db.PageLabels.AnyAsync(pl => pl.PageId == pageId && pl.LabelId == entity.Id, ct))
                db.PageLabels.Add(new PageLabel
                {
                    PageId = pageId,
                    LabelId = entity.Id,
                    AddedById = current.RequireId(),
                    AddedAt = DateTimeOffset.UtcNow,
                });
        }
        else
        {
            var link = await db.PageLabels.FirstOrDefaultAsync(pl => pl.PageId == pageId && pl.Label!.Name == name, ct);
            if (link is not null) db.PageLabels.Remove(link);
        }
        await db.SaveChangesAsync(ct);

        var labels = await db.PageLabels.AsNoTracking()
            .Where(pl => pl.PageId == pageId).Select(pl => pl.Label!.Name).OrderBy(n => n).ToListAsync(ct);
        return new LabelsResult(pageId, labels);
    }

}
