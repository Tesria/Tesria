using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tesria.Api.Features.Export;

/// <summary>
/// A space as a portable file (dev-plan 8.5): the format, a canonical writer
/// and a validating reader.
///
/// <para><b>What a pack is.</b> Content and structure, with every identity
/// re-minted on import. It carries no permissions and no user accounts,
/// because those are the two things that would let a zip file grant access
/// on the instance that opens it. Authors survive as display names in
/// <c>authors.json</c>, for the record rather than for matching.</para>
///
/// <para><b>Why this file is pure.</b> Nothing here touches the database, the
/// filesystem or the clock beyond what it is handed. Reading a pack is
/// parsing something a stranger made, and that is much easier to be sure of
/// when it is a function from bytes to a model with no way to reach anything
/// else.</para>
/// </summary>
public static class WikiPack
{
    /// <summary>
    /// The format version. An integer, and the first thing a reader checks.
    ///
    /// Adding an optional field does not change it; changing what an existing
    /// field means does. A reader refuses anything higher than it knows
    /// rather than guessing, because guessing at a format from the future is
    /// how a pack silently loses half its content.
    /// </summary>
    public const int Format = 1;

    /// <summary>
    /// Caps on what a reader will accept. A pack is untrusted input: it
    /// arrives as an upload and decides how much memory and disk the import
    /// uses, so the ceilings are here rather than implied.
    /// </summary>
    public const long MaxTotalBytes = 500L * 1024 * 1024;
    public const int MaxEntries = 20_000;
    /// <summary>The same limit an ordinary upload has (AttachmentEndpoints).</summary>
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    /* ---- the model ------------------------------------------------------ */

    public sealed record Manifest(
        int Format,
        string Generator,
        DateTimeOffset ExportedAt,
        string Source,
        ManifestSpace Space,
        Counts Counts,
        Omitted Omitted,
        Restrictions Restrictions);

    public sealed record ManifestSpace(string Key, string Name);

    public sealed record Counts(int Pages, int Versions, int Attachments, int Comments, int Templates, int Labels);

    /// <summary>
    /// What the exporter could not see, as numbers and nothing else. A count
    /// tells the importer the pack is partial; a title would tell them what
    /// they were not allowed to read.
    /// </summary>
    public sealed record Omitted(int Pages);

    /// <summary>
    /// That the source space had access rules, never what they were. The
    /// importer needs to know to set them again; naming the principals would
    /// invite matching them by name on the target, which is how the wrong
    /// person ends up with access.
    /// </summary>
    public sealed record Restrictions(int Space, int Pages);

    public sealed record PackSpace(
        string Key,
        string Name,
        string? Description,
        Guid? Homepage,
        PackIcon Icon,
        IReadOnlyList<PackTemplate> Templates);

    /// <param name="File">Set only for an uploaded icon: the entry holding its bytes.</param>
    public sealed record PackIcon(int Kind, string? Value, int? Color, string? File);

    public sealed record PackTemplate(string Name, string? Description, string Content);

    public sealed record PackPage(
        Guid Id,
        Guid? Parent,
        int Position,
        string Title,
        bool FullWidth,
        DateTimeOffset CreatedAt,
        Guid? CreatedBy,
        int Current,
        IReadOnlyList<PackVersion> Versions,
        IReadOnlyList<string> Labels,
        IReadOnlyList<PackAttachment> Attachments,
        IReadOnlyList<PackComment> Comments);

    public sealed record PackVersion(
        int Number, DateTimeOffset CreatedAt, Guid? Author, string? Comment, JsonNode Content);

    public sealed record PackAttachment(
        Guid Id, string Filename, string ContentType, long Size, string File);

    public sealed record PackComment(
        Guid Id, Guid? Parent, Guid? Author, string Body, string? Anchor,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt);

    public sealed record Author(string DisplayName);

    /// <summary>A whole pack in memory, minus the bytes of its files.</summary>
    public sealed record Model(
        Manifest Manifest,
        PackSpace Space,
        IReadOnlyDictionary<Guid, Author> Authors,
        IReadOnlyList<PackPage> Pages);

    /* ---- entry names ---------------------------------------------------- */

    public const string ManifestEntry = "manifest.json";
    public const string SpaceEntry = "space.json";
    public const string AuthorsEntry = "authors.json";
    public const string PagePrefix = "pages/";
    public const string AttachmentPrefix = "attachments/";
    public const string IconEntry = "space-icon.webp";

    public static string PageEntry(Guid id) => $"{PagePrefix}{id:D}.json";
    public static string AttachmentEntry(Guid id) => $"{AttachmentPrefix}{id:D}";

    /* ---- writing -------------------------------------------------------- */

    /// <summary>
    /// The one serializer, and the reason it is one: every string a pack
    /// writes goes through these options, so "canonical" is a property of the
    /// format rather than a habit each call site has to remember.
    ///
    /// Indented, because a committed pack is read and diffed by people
    /// (dev-plan 10.5). Unsafe-relaxed escaping so that an apostrophe in a
    /// page title stays an apostrophe instead of becoming <c>'</c>: the
    /// output is JSON in a zip, not HTML, and a diff full of escapes helps
    /// nobody.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes a pack. <paramref name="files"/> supplies the bytes for an
    /// entry name when asked, so a caller can stream them out of storage
    /// rather than holding a space's attachments in memory.
    ///
    /// <para><b>Deterministic on purpose.</b> Entries are written in a fixed
    /// order, every entry gets the same fixed timestamp, and the JSON is
    /// written by one serializer. A pack of an unchanged space is therefore
    /// byte-identical to the last one, which is what lets dev-plan 10.5 keep
    /// the manual's pack in git and see a one-word edit as one word.</para>
    /// </summary>
    public static async Task WriteAsync(
        Stream destination, Model model, Func<string, CancellationToken, Task<Stream?>> files,
        CancellationToken ct = default)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await WriteJsonAsync(zip, ManifestEntry, model.Manifest, ct);
        await WriteJsonAsync(zip, SpaceEntry, model.Space, ct);
        // Sorted, so the file does not reorder itself between exports.
        await WriteJsonAsync(
            zip, AuthorsEntry,
            model.Authors.OrderBy(a => a.Key).ToDictionary(a => a.Key.ToString("D"), a => a.Value), ct);

        // Pages in the order Place() walks them, which is the order a reader
        // of the tree would expect, and attachments beside them.
        foreach (var page in model.Pages)
        {
            await WriteJsonAsync(zip, PageEntry(page.Id), page, ct);
        }

        foreach (var file in model.Pages
                     .SelectMany(p => p.Attachments)
                     .Select(a => a.File)
                     .Concat(model.Space.Icon.File is { } icon ? [icon] : Array.Empty<string>())
                     .Distinct()
                     .Order(StringComparer.Ordinal))
        {
            await using var bytes = await files(file, ct);
            if (bytes is null) continue; // a file that has gone is skipped, not fatal
            var entry = Fixed(zip.CreateEntry(file, CompressionLevel.Optimal));
            await using var target = entry.Open();
            await bytes.CopyToAsync(target, ct);
        }
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, CancellationToken ct)
    {
        var entry = Fixed(zip.CreateEntry(name, CompressionLevel.Optimal));
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, Json, ct);
    }

    /// <summary>
    /// One timestamp for every entry, so the zip does not change when nothing
    /// in it did. The value is arbitrary and only has to be constant and
    /// representable in a zip header (which cannot hold a date before 1980).
    /// </summary>
    private static ZipArchiveEntry Fixed(ZipArchiveEntry entry)
    {
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return entry;
    }

    /* ---- reading -------------------------------------------------------- */

    public sealed class PackException(string message) : Exception(message);

    /// <summary>
    /// Reads and validates a pack, or throws <see cref="PackException"/> with
    /// a message fit to show the person who uploaded it.
    ///
    /// <para>Everything here treats the archive as hostile: the format is
    /// checked before anything else is parsed, entry names are matched
    /// against the shapes this format defines rather than sanitized, and the
    /// size and count ceilings are enforced while reading rather than after.
    /// The model it returns is still *untrusted content* (page documents
    /// still have to go through <c>PageContent.TryNormalize</c>); what it
    /// guarantees is that the archive itself is what it claims to be.</para>
    /// </summary>
    public static Model Read(Stream source)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        if (zip.Entries.Count > MaxEntries)
            throw new PackException($"This pack has more than {MaxEntries} files in it.");

        long total = 0;
        foreach (var entry in zip.Entries)
        {
            // A directory entry is a name ending in "/" with nothing in it.
            // This writer never makes one, but `zip -r` does, and zipping an
            // unpacked pack back up is exactly how 10.5 is meant to work, so
            // refusing them would break the workflow the format exists for.
            // Skipped rather than allowed through IsAllowedName, which is
            // about files and is also what the import's zip-slip defense is.
            if (IsDirectory(entry)) continue;

            // A zip declares each entry's uncompressed length, so the total is
            // known before a byte is decompressed. Checking it here is what
            // stops a small file that expands to fill the disk.
            total += entry.Length;
            if (total > MaxTotalBytes)
                throw new PackException("This pack is larger than 500 MB unpacked.");
            if (!IsAllowedName(entry.FullName))
                throw new PackException($"This pack contains an unexpected file: {Describe(entry.FullName)}");
        }

        var manifest = ReadJson<Manifest>(zip, ManifestEntry)
            ?? throw new PackException("This file is not a Tesria pack: it has no manifest.");

        // First, and before anything else is interpreted.
        if (manifest.Format > Format)
            throw new PackException(
                $"This pack was made by a newer version of Tesria (format {manifest.Format}; this one reads {Format}).");
        if (manifest.Format < 1)
            throw new PackException("This pack's format version is not valid.");

        var space = ReadJson<PackSpace>(zip, SpaceEntry)
            ?? throw new PackException("This pack has no space in it.");
        var authors = ReadJson<Dictionary<string, Author>>(zip, AuthorsEntry) ?? [];

        var pages = new List<PackPage>();
        foreach (var entry in zip.Entries
                     .Where(e => e.FullName.StartsWith(PagePrefix, StringComparison.Ordinal) && !IsDirectory(e))
                     .OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var page = ReadJson<PackPage>(zip, entry.FullName)
                ?? throw new PackException($"A page in this pack could not be read: {Describe(entry.FullName)}");
            pages.Add(page);
        }

        foreach (var attachment in pages.SelectMany(p => p.Attachments))
        {
            var entry = zip.GetEntry(attachment.File);
            if (entry is null)
                throw new PackException($"This pack refers to a file it does not contain: {Describe(attachment.File)}");
            if (entry.Length > MaxAttachmentBytes)
                throw new PackException(
                    $"'{attachment.Filename}' is larger than the {MaxAttachmentBytes / (1024 * 1024)} MB limit.");
        }

        return new Model(
            manifest,
            space,
            authors
                .Where(a => Guid.TryParse(a.Key, out _))
                .ToDictionary(a => Guid.Parse(a.Key), a => a.Value),
            Ordered(pages));
    }

    /// <summary>
    /// The pages, with the tree made safe: a parent that is not in the pack
    /// is dropped to the root (12.2 does the same for a page whose parent the
    /// reader cannot see), and a cycle is broken by dropping the page that
    /// closes it. A cycle cannot be imported at all, so refusing to is worse
    /// than flattening it.
    /// </summary>
    private static List<PackPage> Ordered(List<PackPage> pages)
    {
        var byId = pages.ToDictionary(p => p.Id);
        var fixedUp = new List<PackPage>(pages.Count);
        foreach (var page in pages)
        {
            var parent = page.Parent;
            if (parent is { } id && !byId.ContainsKey(id)) parent = null;
            if (parent is not null && ClimbsToSelf(page.Id, parent.Value, byId)) parent = null;
            fixedUp.Add(parent == page.Parent ? page : page with { Parent = parent });
        }
        return fixedUp;
    }

    private static bool ClimbsToSelf(Guid start, Guid from, Dictionary<Guid, PackPage> byId)
    {
        var seen = new HashSet<Guid>();
        var at = (Guid?)from;
        while (at is { } id && seen.Add(id))
        {
            if (id == start) return true;
            at = byId.TryGetValue(id, out var parent) ? parent.Parent : null;
        }
        return false;
    }

    private static T? ReadJson<T>(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return default;
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream, Json);
        }
        catch (JsonException ex)
        {
            throw new PackException($"{Describe(name)} in this pack is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether an entry name is one this format defines.
    ///
    /// An allow-list of shapes rather than a scrub of a name the archive
    /// supplied: <c>../</c>, an absolute path, a backslash and a stray
    /// directory all simply fail to match, without anyone having to think of
    /// them one at a time. The page and attachment names must parse as the
    /// ids they claim to be.
    /// </summary>
    public static bool IsAllowedName(string name)
    {
        if (name is ManifestEntry or SpaceEntry or AuthorsEntry or IconEntry) return true;
        if (name.StartsWith(PagePrefix, StringComparison.Ordinal))
        {
            var rest = name[PagePrefix.Length..];
            return rest.EndsWith(".json", StringComparison.Ordinal)
                   && Guid.TryParseExact(rest[..^".json".Length], "D", out _);
        }
        if (name.StartsWith(AttachmentPrefix, StringComparison.Ordinal))
            return Guid.TryParseExact(name[AttachmentPrefix.Length..], "D", out _);
        return false;
    }

    /// <summary>
    /// Whether this entry is a folder rather than a file: an empty entry whose
    /// name ends in a separator, which is how every zip tool records one.
    /// </summary>
    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.Length == 0 && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'));

    /// <summary>A name from an untrusted archive, safe to put in a message shown to a person.</summary>
    private static string Describe(string name)
    {
        var trimmed = name.Length > 80 ? name[..80] + "…" : name;
        var clean = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed) clean.Append(char.IsControl(c) ? '?' : c);
        return clean.ToString();
    }
}
