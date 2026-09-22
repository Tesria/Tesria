using Tesria.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Settings;

public interface ISiteSettingsService
{
    /// <summary>
    /// The current settings, creating the single row on first use. Cached, so
    /// call it freely: request paths like registration read it every time.
    /// </summary>
    Task<SiteSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Applies <paramref name="mutate"/> to the row and invalidates the cache.</summary>
    Task<SiteSettings> UpdateAsync(Action<SiteSettings> mutate, Guid? actorId, CancellationToken ct = default);

    /// <summary>Encrypts a plaintext SMTP password for storage.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a stored SMTP password, or null if it cannot be read.</summary>
    string? Unprotect(string? protectedValue);
}

/// <summary>
/// Holds the cached settings across requests. A singleton because the service
/// itself is scoped (it needs the request's DbContext), so the cache cannot
/// live on it.
///
/// Single-instance assumption: invalidation is in-process, so if this app is
/// ever run as more than one replica, another replica keeps its copy until the
/// TTL expires. The TTL is the bound on that staleness, which is why it is
/// short rather than indefinite.
/// </summary>
public sealed class SiteSettingsCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private SiteSettings? _value;
    private DateTimeOffset _loadedAt;

    public SiteSettings? Get()
    {
        lock (_gate)
        {
            if (_value is null) return null;
            return DateTimeOffset.UtcNow - _loadedAt <= Ttl ? _value : null;
        }
    }

    public void Set(SiteSettings value)
    {
        lock (_gate)
        {
            _value = value;
            _loadedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Invalidate()
    {
        lock (_gate) { _value = null; }
    }

    /// <summary>
    /// The last value loaded, however old. For synchronous callers that cannot
    /// await a reload, the rate limiter's partitioner, where a slightly stale
    /// limit is better than none, and null only before the first load.
    /// </summary>
    public SiteSettings? Peek()
    {
        lock (_gate) { return _value; }
    }
}

public sealed class SiteSettingsService(
    AppDbContext db, SiteSettingsCache cache, IDataProtectionProvider dataProtection)
    : ISiteSettingsService
{
    // Purpose string: changing it invalidates every previously protected value,
    // so it is fixed and must not be edited casually.
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector("Tesria.SiteSettings.Smtp.v1");

    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.Get() is { } cached) return cached;

        var settings = await LoadOrCreateAsync(ct);
        cache.Set(settings);
        return settings;
    }

    public async Task<SiteSettings> UpdateAsync(
        Action<SiteSettings> mutate, Guid? actorId, CancellationToken ct = default)
    {
        // Tracked (not AsNoTracking): this instance is the one being saved.
        var settings = await db.SiteSettings.FirstOrDefaultAsync(s => s.Id == SiteSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new SiteSettings();
            db.SiteSettings.Add(settings);
        }

        mutate(settings);
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedById = actorId;
        await db.SaveChangesAsync(ct);

        // Set, not invalidated: the limiter reads through Peek() and should
        // see a change the moment it is saved rather than at the next load.
        cache.Set(settings);
        return settings;
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The Data Protection keyring was lost or rotated beyond recovery
            // (e.g. restoring the uploads volume without the database). Treat it
            // as "no password configured" rather than taking the request down:
            // the operator re-enters it, and "Send test email" reports the fault.
            return null;
        }
    }

    private async Task<SiteSettings> LoadOrCreateAsync(CancellationToken ct)
    {
        var existing = await db.SiteSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SiteSettings.SingletonId, ct);
        if (existing is not null) return existing;

        try
        {
            var created = new SiteSettings { UpdatedAt = DateTimeOffset.UtcNow };
            db.SiteSettings.Add(created);
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException)
        {
            // Two requests raced to create the row; the fixed primary key means
            // exactly one wins. Re-read to get the winner's copy.
            db.ChangeTracker.Clear();
            return await db.SiteSettings.AsNoTracking()
                .FirstAsync(s => s.Id == SiteSettings.SingletonId, ct);
        }
    }
}
