namespace Tesria.Api.Domain;

/// <summary>
/// Top-level container for pages (e.g. per team or project), mirroring
/// Confluence spaces (PLAN §2). Identified by a short, human-friendly
/// <see cref="Key"/> used in URLs.
/// </summary>
public class Space
{
    public Guid Id { get; set; }

    /// <summary>Short uppercase key, unique across the instance (e.g. "ENG").</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Optional page shown as the space landing page.</summary>
    public Guid? HomepageId { get; set; }
    public Page? Homepage { get; set; }

    public bool Archived { get; set; }

    // --- Icon (dev-plan 6). Three columns rather than one: the kind decides
    // how IconValue is read, and the color applies to the tile behind a
    // letter or an emoji: an uploaded image covers the tile entirely.

    public SpaceIconKind IconKind { get; set; } = SpaceIconKind.None;

    /// <summary>
    /// The emoji for <see cref="SpaceIconKind.Emoji"/>; the stored image's
    /// content hash for <see cref="SpaceIconKind.Image"/> (the storage key is
    /// derived from the space id, so it is not duplicated here); null for
    /// <see cref="SpaceIconKind.None"/>.
    /// </summary>
    public string? IconValue { get; set; }

    /// <summary>
    /// Index into the client's tile palette, or null to derive one from the
    /// key. An index rather than a hex value so the palette can be restyled
    /// without rewriting every row: the same reasoning as
    /// <see cref="User.AvatarVariant"/>.
    /// </summary>
    public int? IconColor { get; set; }

    /// <summary>
    /// Readable by anyone, no account needed (dev-plan 5.1), but only while
    /// the instance-wide <c>AllowPublicSpaces</c> switch is also on. Kept
    /// when that switch is off, so re-enabling restores the previous state.
    /// </summary>
    public bool IsPublic { get; set; }
    public DateTimeOffset? PublicSince { get; set; }

    /// <summary>Whether anonymous readers may see (never write) comments. Off by default.</summary>
    public bool PublicComments { get; set; }

    /// <summary>
    /// How the page tree marks its pages (dev-plan 15.8): plain, numbered in
    /// outline (1, 1.1, 1.2, 2), or bulleted. The numbers are worked out from
    /// the tree's order when it is drawn, so they follow every move.
    /// </summary>
    public SpaceTreeStyle TreeStyle { get; set; } = SpaceTreeStyle.Plain;

    // --- Exports (dev-plan 12.3). Each format on by default; an
    // administrator turns them off for a space more sensitive than the rest.
    // They bind everyone, administrators and the owner included: the people
    // who can change them can turn them back on, and that is audited.

    /// <summary>A page as Markdown.</summary>
    public bool ExportMarkdown { get; set; } = true;
    /// <summary>A page as a single HTML file.</summary>
    public bool ExportHtml { get; set; } = true;
    /// <summary>A page as a PDF.</summary>
    public bool ExportPdf { get; set; } = true;
    /// <summary>The whole space as a static website.</summary>
    public bool ExportSite { get; set; } = true;
    /// <summary>The whole space as a wiki pack, with its history.</summary>
    public bool ExportPack { get; set; } = true;

    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Page> Pages { get; set; } = new List<Page>();
}

/// <summary>
/// How a space's icon is drawn. <see cref="None"/> is not "no icon": it is
/// the generated default (the key's first letter on a colored tile), so
/// every space has one from the moment it is created.
/// </summary>
/// <summary>How a space's page tree marks its pages (dev-plan 15.8).</summary>
public enum SpaceTreeStyle
{
    Plain = 0,
    Numbered = 1,
    Bulleted = 2,
}

public enum SpaceIconKind
{
    None = 0,
    Emoji = 1,
    Image = 2,
}
