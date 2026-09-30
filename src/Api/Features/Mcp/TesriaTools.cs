using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
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

    [McpServerTool(Name = "get_page", UseStructuredContent = true, OutputSchemaType = typeof(PageContent)), Description(
        "A page by id. Returns its content as Markdown: the same Markdown the export produces, with live " +
        "blocks (children lists, recently-updated tables, task reports…) resolved as this token's owner would see " +
        "them. The first text item is the content alone, exactly what update_page takes back as `content`; the " +
        "second is JSON with the page's id, title, space, labels, version, url and an `outline` of its headings. " +
        "Pass `section` with a heading id from that outline to get just " +
        "that heading and everything under it, which is usually what you want on a long page. A section comes " +
        "marked as one (a first-line comment in Markdown, a `section` property in JSON): to save a change to it, " +
        "send it back to update_page with the same `section`, which replaces that section and keeps the rest of " +
        "the page. Ask for format " +
        "'json' to get the editor's ProseMirror document instead (for `contentJson`), e.g. to copy a page exactly. " +
        "'Not found' can mean the page does not exist or that you may not see it; the two are deliberately indistinguishable.")]
    public static async Task<CallToolResult> GetPage(
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
            json = PageSections.Extract(json, wanted) ?? throw NoSuchSection(wanted, outline);
        }

        // Blocks are resolved against the whole page either way: a children
        // display inside a section is still that page's children.
        var content = wantJson
            ? json
            : ProseMirrorRenderer.ToMarkdown(json, await PageSnapshots.BlocksAsync(page.Id, json, blocks, ct), baseUrl);
        // One section is marked as one, so it cannot be sent back as the
        // whole page and replace everything else.
        if (!string.IsNullOrEmpty(wanted))
            content = wantJson ? SectionMark.Json(content, wanted) : SectionMark.Markdown(content, wanted);

        var result = new PageContent(
            page.Id, page.Title, page.Space.Key, page.ParentPageId, labels,
            page.CurrentVersion.VersionNumber, page.UpdatedAt,
            $"{baseUrl}/spaces/{page.Space.Key}/pages/{page.Id}",
            wantJson ? "json" : "markdown", content, outline,
            string.IsNullOrEmpty(wanted) ? null : wanted);

        // The content alone first, then what describes it (T5-005). Both used
        // to be one JSON text, and an assistant that sent that text back as
        // the page's content published the JSON as the page. The whole record
        // is still there, as structured content, for clients that read it.
        var structured = JsonSerializer.SerializeToNode(result, StructuredJson)!.AsObject();
        var about = (JsonObject)structured.DeepClone();
        about.Remove("content");
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = content },
                new TextContentBlock { Text = about.ToJsonString(StructuredJson) },
            ],
            StructuredContent = JsonSerializer.SerializeToElement(structured, StructuredJson),
        };
    }

    private static readonly JsonSerializerOptions StructuredJson = new(JsonSerializerDefaults.Web);

    private static McpException NoSuchSection(string wanted, IReadOnlyList<PageSections.Heading> outline) =>
        new($"This page has no top-level section '{wanted}'. Its sections are: "
            + (outline.Count == 0 ? "(none)" : string.Join(", ", outline.Select(h => h.Id))) + ".");

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
        "token with write access.")]
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
        "Replace a page's body, or one section of it, and optionally its title. This creates a new version, as an " +
        "edit in the browser does: fetch the page first if you mean to change only part of it. Without `section`, " +
        "`content` is the whole page. With `section` (a heading id from get_page's outline), `content` is just that " +
        "section, heading included: it replaces that heading and everything under it, and the rest of the page is " +
        "kept. A section read with get_page's `section` is refused without it, so it never replaces the whole " +
        "page. Needs a token with write access.")]
    public static async Task<WriteResult> UpdatePage(
        [Description("The page id.")] Guid pageId,
        IPageWriter writer, AppDbContext db, CurrentUser current, IHttpContextAccessor accessor,
        IPermissionService perms, IDynamicBlockService blocks,
        ISiteSettingsService settings, IConfiguration config, CancellationToken ct,
        [Description("New body as Markdown.")] string? content = null,
        [Description("New body as a ProseMirror JSON document.")] string? contentJson = null,
        [Description("New title; omit to keep the current one.")] string? title = null,
        [Description("A short note describing the change, shown in the page's history.")] string? changeComment = null,
        [Description("A heading id from the page's outline: the body replaces only that section.")] string? section = null)
    {
        McpAccess.RequireWrite(current, accessor);
        var wanted = section?.Trim();
        string? body;
        if (string.IsNullOrEmpty(wanted))
        {
            // One section sent as the whole page would replace everything
            // else on it with that section.
            if (SectionMark.Of(content ?? contentJson) is { } marked)
                throw new McpException(
                    $"That is only the section '{marked}' of the page, as get_page returned it with `section`, not the "
                    + $"whole page. Send it again with section: \"{marked}\" to replace just that section and keep the "
                    + "rest of the page. Sent as it is, it would have replaced the whole page.");
            body = Body(content, contentJson);
            if (content is not null)
                body = await MergeAsync(pageId, content, db, perms, blocks, settings, config, ct) ?? body;
        }
        else
        {
            body = await SectionBodyAsync(pageId, wanted, content, contentJson, db, perms, blocks, settings, config, ct);
        }
        var result = await writer.UpdateAsync(pageId, title, body, changeComment, ct);
        return await ResultOf(result, db, settings, config, ct);
    }

    /// <summary>
    /// The whole page with one section replaced by what the assistant sent,
    /// everything else as it is. Markdown is merged with that section as it
    /// is merged with a whole page (<see cref="MergeAsync"/>), so the blocks
    /// of the section it sent back unchanged keep what Markdown cannot say.
    /// </summary>
    private static async Task<string> SectionBodyAsync(
        Guid pageId, string wanted, string? content, string? contentJson, AppDbContext db, IPermissionService perms,
        IDynamicBlockService blocks, ISiteSettingsService settings, IConfiguration config, CancellationToken ct)
    {
        if (content is null && contentJson is null)
            throw new McpException(
                "`section` needs the section's new content, as `content` (Markdown) or `contentJson`. "
                + "To change only the title, leave `section` out.");
        // The same checks a whole body gets: one form, and not get_page's whole answer.
        _ = Body(content, contentJson);
        if (SectionMark.Of(content ?? contentJson) is { } marked && marked != wanted)
            throw new McpException(
                $"That content is the section '{marked}', but `section` names '{wanted}'. "
                + $"To replace '{marked}', pass section: \"{marked}\".");

        if (!await perms.CanViewPageAsync(pageId)) throw McpAccess.NotFound("Page");
        var current = await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId && p.CurrentVersion != null)
            .Select(p => p.CurrentVersion!.ContentJson)
            .FirstOrDefaultAsync(ct) ?? throw McpAccess.NotFound("Page");
        var sectionJson = PageSections.Extract(current, wanted)
            ?? throw NoSuchSection(wanted, PageSections.Outline(current));

        JsonArray replacement;
        if (content is not null)
        {
            var markdown = SectionMark.Strip(content);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                // Nothing at all: the section goes.
                replacement = [];
            }
            else
            {
                // Written exactly as get_page wrote the section, so an
                // unchanged block reads the same on both sides.
                var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
                var snapshots = await PageSnapshots.BlocksAsync(pageId, sectionJson, blocks, ct);
                var merged = MarkdownMerge.Merge(
                    sectionJson, ProseMirrorRenderer.ToMarkdownBlocks(sectionJson, snapshots, baseUrl), markdown);
                replacement = (JsonNode.Parse(merged)?["content"] as JsonArray) ?? [];
            }
        }
        else
        {
            JsonNode? doc;
            try { doc = JsonNode.Parse(SectionMark.Strip(contentJson!)); }
            catch (JsonException) { doc = null; }
            replacement = doc?["content"] as JsonArray
                ?? throw new McpException("`contentJson` must be a ProseMirror document: {\"type\":\"doc\",\"content\":[...]}.");
        }
        if (SectionShapeProblem(wanted, PageSections.Outline(current), replacement) is { } problem)
            throw new McpException(problem);
        return PageSections.Replace(current, wanted, replacement)!;
    }

    /// <summary>
    /// Why this cannot be the section <paramref name="wanted"/>'s new content,
    /// or null when it can (t5-R07, the 0.8.3 retest). A section is its
    /// heading and what follows it up to the next heading of the same or a
    /// higher level, so its content starts with a heading of its level and
    /// holds no other heading that high. The whole page sent with a section's
    /// name was accepted, and the page then held its other sections twice.
    /// Empty content still removes the section.
    /// </summary>
    internal static string? SectionShapeProblem(string wanted, IReadOnlyList<PageSections.Heading> outline, JsonArray replacement)
    {
        if (replacement.Count == 0) return null;
        var level = outline.FirstOrDefault(h => h.Id == wanted)?.Level ?? 1;
        static int? LevelOf(JsonNode? block) =>
            block?["type"]?.GetValue<string>() == "heading" ? (block["attrs"]?["level"]?.GetValue<int>() ?? 1) : null;
        if (LevelOf(replacement[0]) != level)
            return $"The content for section '{wanted}' must start with that section's heading, a level-{level} heading, "
                + "as get_page returned it with `section`. To remove the section, send empty content.";
        for (var i = 1; i < replacement.Count; i++)
            if (LevelOf(replacement[i]) is { } found && found <= level)
                return $"That content goes past the section '{wanted}': it also holds another heading of its level or higher "
                    + $"(\"{HeadingText(replacement[i])}\"). Send only that section, from its heading up to the next one of its "
                    + "level, or send the whole page without `section`.";
        return null;
    }

    private static string HeadingText(JsonNode? heading) =>
        string.Concat((heading?["content"] as JsonArray ?? []).Select(n => n?["text"]?.GetValue<string>() ?? ""));

    /// <summary>
    /// The Markdown written onto the page as it is, keeping every block the
    /// assistant sent back unchanged (T5-003; see <see cref="MarkdownMerge"/>).
    /// Null when there is nothing to merge with, or nothing the caller may
    /// see: the writer then answers as it would have.
    /// </summary>
    private static async Task<string?> MergeAsync(
        Guid pageId, string markdown, AppDbContext db, IPermissionService perms, IDynamicBlockService blocks,
        ISiteSettingsService settings, IConfiguration config, CancellationToken ct)
    {
        if (!await perms.CanViewPageAsync(pageId)) return null;
        var current = await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId && p.CurrentVersion != null)
            .Select(p => p.CurrentVersion!.ContentJson)
            .FirstOrDefaultAsync(ct);
        if (current is null) return null;

        // Written exactly as get_page wrote it, live blocks and links
        // included, so an unchanged block reads the same on both sides.
        var baseUrl = SiteUrl.Resolve(await settings.GetAsync(ct), config);
        var snapshots = await PageSnapshots.BlocksAsync(pageId, current, blocks, ct);
        return MarkdownMerge.Merge(current, ProseMirrorRenderer.ToMarkdownBlocks(current, snapshots, baseUrl), markdown);
    }

    [McpServerTool(Name = "add_page_label"), Description("Add a label to a page. Needs a token with write access.")]
    public static Task<LabelsResult> AddPageLabel(
        [Description("The page id.")] Guid pageId,
        [Description("The label, lower-case: letters, digits, dot, dash or underscore.")] string label,
        AppDbContext db, IPermissionService perms, CurrentUser current, IHttpContextAccessor accessor, CancellationToken ct) =>
        SetLabelAsync(pageId, label, add: true, db, perms, current, accessor, ct);

    [McpServerTool(Name = "remove_page_label"), Description("Remove a label from a page. Needs a token with write access.")]
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
        if (IsGetPageAnswer(content) || IsGetPageAnswer(contentJson))
            throw new McpException(
                "That is get_page's whole answer (id, title, content and so on), not a page body. "
                + "Send only its `content` value.");
        // A section read with get_page is a fine body for a new page; its
        // note is not part of it. (update_page deals with the note itself.)
        if (contentJson is not null) return SectionMark.Strip(contentJson);
        return content is null ? null : MarkdownToProseMirror.Convert(SectionMark.Strip(content));
    }

    /// <summary>
    /// Whether a body is get_page's description of a page rather than a page
    /// (T5-005): sent back whole, it used to be published as the page's text.
    /// </summary>
    private static bool IsGetPageAnswer(string? body)
    {
        if (body is null || !body.TrimStart().StartsWith('{')) return false;
        try
        {
            using var parsed = JsonDocument.Parse(body);
            var root = parsed.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && HasAny(root, "content", "Content")
                && HasAny(root, "format", "Format")
                && HasAny(root, "title", "Title");
        }
        catch (JsonException)
        {
            return false;
        }

        static bool HasAny(JsonElement root, params string[] names) =>
            names.Any(name => root.TryGetProperty(name, out _));
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
