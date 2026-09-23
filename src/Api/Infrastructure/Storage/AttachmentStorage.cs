namespace Tesria.Api.Infrastructure.Storage;

/// <summary>
/// Stores and retrieves attachment file bytes. Local disk today (the uploads
/// volume, PLAN §3); an S3-compatible implementation can replace it later
/// without touching callers.
/// </summary>
public interface IAttachmentStorage
{
    Task SaveAsync(string storageKey, Stream content, CancellationToken ct = default);
    Stream? OpenRead(string storageKey);
    void Delete(string storageKey);
}

/// <summary>
/// Local-filesystem attachment storage. Files live under a configured root
/// (<c>Storage:UploadsPath</c>, mounted as the <c>uploads</c> volume in Docker).
/// Keys are opaque and sanitized so they cannot escape the root directory.
/// </summary>
public sealed class LocalAttachmentStorage : IAttachmentStorage
{
    private readonly string _root;

    public LocalAttachmentStorage(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:UploadsPath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "uploads")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken ct = default)
    {
        var path = ResolvePath(storageKey);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Stream? OpenRead(string storageKey)
    {
        var path = ResolvePath(storageKey);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(string storageKey)
    {
        var path = ResolvePath(storageKey);
        if (File.Exists(path)) File.Delete(path);
    }

    // Keys are generated as GUIDs, but resolve defensively so a crafted key can
    // never point outside the storage root.
    private string ResolvePath(string storageKey)
    {
        var safe = Path.GetFileName(storageKey);
        if (string.IsNullOrWhiteSpace(safe))
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        return Path.Combine(_root, safe);
    }
}
