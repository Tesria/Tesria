namespace Tesria.Api.Features.Blocks;

/// <summary>
/// The one shape every dynamic-block kind answers with (architecture.md,
/// "Dynamic blocks", decision 2). Three shapes — <c>list</c>, <c>table</c>,
/// <c>document</c> — rendered by exactly one renderer in the SPA and one in
/// the exporter, so a kind is only ever a query.
/// </summary>
public sealed record BlockResult(
    string Kind,
    string Shape,
    IReadOnlyList<BlockItem> Items,
    string? Title = null,
    /// <summary>Shown when <see cref="Items"/> is empty — the kind knows why nothing is there.</summary>
    string? Empty = null,
    IReadOnlyList<BlockColumn>? Columns = null,
    /// <summary>ProseMirror JSON, for <c>document</c>-shaped kinds only.</summary>
    string? Document = null)
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BlockResult List(string kind, IReadOnlyList<BlockItem> items, string? title = null, string? empty = null) =>
        new(kind, "list", items, title, empty);

    public static BlockResult Table(string kind, IReadOnlyList<BlockColumn> columns, IReadOnlyList<BlockItem> items, string? title = null, string? empty = null) =>
        new(kind, "table", items, title, empty, columns);

    public static BlockResult DocumentOf(string kind, string? document, string? empty = null) =>
        new(kind, "document", [], null, empty, null, document);
}

public sealed record BlockColumn(string Key, string Label);

/// <summary>One row or entry. <see cref="Href"/> is app-relative (<c>/spaces/KEY/pages/ID</c>); the exporter makes it absolute.</summary>
public sealed record BlockItem(
    string Title,
    string? Href = null,
    string? Subtitle = null,
    IReadOnlyDictionary<string, BlockCell>? Cells = null,
    IReadOnlyList<BlockItem>? Children = null);

/// <summary>Exactly one of the value fields is set; the renderers decide how each is drawn, once.</summary>
public sealed record BlockCell(
    string? Text = null,
    string? Href = null,
    DateTimeOffset? Date = null,
    BlockUser? User = null,
    bool? Checked = null)
{
    public static BlockCell Of(string text, string? href = null) => new(Text: text, Href: href);
    public static BlockCell On(DateTimeOffset date) => new(Date: date);
    public static BlockCell By(BlockUser user) => new(User: user);
    public static BlockCell Done(bool done) => new(Checked: done);
}

public sealed record BlockUser(Guid Id, string DisplayName, string? AvatarHash, int? AvatarVariant);
