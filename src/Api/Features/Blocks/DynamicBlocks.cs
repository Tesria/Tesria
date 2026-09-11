using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks;

/// <summary>A kind of dynamic block: the URL name and the query. Nothing else — see architecture.md.</summary>
public interface IDynamicBlockKind
{
    /// <summary>Kebab-case, as it appears in documents and URLs.</summary>
    string Kind { get; }
    Task<BlockResult> RenderAsync(BlockContext ctx, CancellationToken ct);
}

/// <summary>A parameter the kind could not accept. Becomes a 400 naming the field; an export renders a placeholder.</summary>
public sealed class BlockParamException(string param, string message) : Exception(message)
{
    public string Param { get; } = param;
}

/// <summary>
/// What a kind gets to work with: the host page, validated parameter access,
/// and the one permission helper that makes the filtering rule hard to
/// break — <see cref="VisibleAsync"/>.
/// </summary>
public sealed class BlockContext(
    Page host, IReadOnlyDictionary<string, string> parameters, AppDbContext db, IPermissionService perms)
{
    /// <summary>The page the block sits on; the caller has already been allowed to view it.</summary>
    public Page Host { get; } = host;
    public AppDbContext Db { get; } = db;
    public IPermissionService Perms { get; } = perms;

    /// <summary>Page hrefs are app-relative; the SPA routes them, the exporter makes them absolute.</summary>
    public string HrefFor(string spaceKey, Guid pageId) => $"/spaces/{spaceKey}/pages/{pageId}";

    public string Str(string name, string @default) =>
        parameters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : @default;

    public string Required(string name) =>
        parameters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.Trim()
            : throw new BlockParamException(name, $"'{name}' is required.");

    public int Int(string name, int @default, int min, int max)
    {
        if (!parameters.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw)) return @default;
        if (!int.TryParse(raw, out var value))
            throw new BlockParamException(name, $"'{name}' must be a whole number.");
        return Math.Clamp(value, min, max);
    }

    /// <summary>One of a fixed set of words; anything else is a 400, not a silent default.</summary>
    public string Enum(string name, string @default, params string[] allowed)
    {
        var value = Str(name, @default);
        return allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new BlockParamException(name, $"'{name}' must be one of: {string.Join(", ", allowed)}.");
    }

    /// <summary>
    /// The two-pass filter every page-listing kind must use: candidates
    /// already narrowed in SQL, then each checked with the caller's own
    /// permissions until <paramref name="limit"/> visible ones are in hand.
    /// Callers should over-fetch — the tenth visible page may be the
    /// fortieth candidate — and never count what was skipped.
    /// </summary>
    public async Task<List<T>> VisibleAsync<T>(IEnumerable<T> candidates, Func<T, Guid> pageId, int limit, CancellationToken ct)
    {
        var visible = new List<T>();
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (visible.Count >= limit) break;
            if (await Perms.CanViewPageAsync(pageId(candidate))) visible.Add(candidate);
        }
        return visible;
    }

    public static BlockUser UserOf(User user) => new(user.Id, user.DisplayName, user.AvatarHash, user.AvatarVariant);
}

public interface IDynamicBlockService
{
    IReadOnlyCollection<string> Kinds { get; }

    /// <summary>
    /// Null when the host page is not viewable by the caller or the kind is
    /// unknown — the endpoint turns those into 404 and 400 respectively; the
    /// exporter turns both into a placeholder.
    /// </summary>
    Task<BlockResult?> RenderAsync(Guid hostPageId, string kind, IReadOnlyDictionary<string, string> parameters, CancellationToken ct);
}

public sealed class DynamicBlockService(
    AppDbContext db, IPermissionService perms, IEnumerable<IDynamicBlockKind> kinds) : IDynamicBlockService
{
    private readonly Dictionary<string, IDynamicBlockKind> _kinds =
        kinds.ToDictionary(k => k.Kind, StringComparer.Ordinal);

    public IReadOnlyCollection<string> Kinds => _kinds.Keys;

    public async Task<BlockResult?> RenderAsync(
        Guid hostPageId, string kind, IReadOnlyDictionary<string, string> parameters, CancellationToken ct)
    {
        if (!_kinds.TryGetValue(kind, out var impl)) return null;
        // The host is the permission anchor: unviewable host, no block at all.
        if (!await perms.CanViewPageAsync(hostPageId)) return null;
        var host = await db.Pages.AsNoTracking().Include(p => p.Space)
            .FirstOrDefaultAsync(p => p.Id == hostPageId, ct);
        if (host is null) return null;
        return await impl.RenderAsync(new BlockContext(host, parameters, db, perms), ct);
    }
}

/// <summary>Finds the dynamic blocks in a stored document, in document order — the order the renderer will meet them.</summary>
public static class DynamicBlocks
{
    public sealed record Placement(string Kind, IReadOnlyDictionary<string, string> Params);

    public static List<Placement> Collect(string? contentJson)
    {
        var found = new List<Placement>();
        if (string.IsNullOrWhiteSpace(contentJson)) return found;
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            Walk(doc.RootElement);
        }
        catch (JsonException) { /* stored as sent; a bad document has no blocks */ }
        return found;

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array) { foreach (var c in node.EnumerateArray()) Walk(c); return; }
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "dynamicBlock")
            {
                var kind = "";
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
                if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                {
                    if (attrs.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String) kind = k.GetString() ?? "";
                    if (attrs.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                        foreach (var prop in ps.EnumerateObject())
                            if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                                parameters[prop.Name] = prop.Value.ToString();
                }
                found.Add(new Placement(kind, parameters));
            }
            if (node.TryGetProperty("content", out var content)) Walk(content);
        }
    }
}
