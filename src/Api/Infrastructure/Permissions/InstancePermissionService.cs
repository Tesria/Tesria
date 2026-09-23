using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

public interface IInstancePermissions
{
    /// <summary>Every right the caller holds, reserved ones included when they own the instance.</summary>
    Task<IReadOnlySet<string>> ForCurrentUserAsync(CancellationToken ct = default);

    Task<bool> HasAsync(string key, CancellationToken ct = default);

    /// <summary>The rights of one account, for the endpoints that report on somebody else.</summary>
    Task<IReadOnlySet<string>> ForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>What an anonymous reader may do: whatever the built-in User role holds, and no more.</summary>
    Task<bool> AnonymousHasAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Reads a person's rights from their role (dev-plan 11.1).
///
/// Cached per role for <see cref="PermissionCache.Ttl"/>, the same arrangement
/// and the same reasoning as <see cref="Settings.SiteSettingsCache"/>: rights
/// are read on nearly every request and change rarely, and every write
/// invalidates, so the TTL only bounds staleness across replicas.
///
/// The three reserved keys are added for an owner rather than stored, so no
/// configuration can take them away.
/// </summary>
public sealed class InstancePermissionService(AppDbContext db, CurrentUser current, PermissionCache cache)
    : IInstancePermissions
{
    private IReadOnlySet<string>? _mine;

    public async Task<IReadOnlySet<string>> ForCurrentUserAsync(CancellationToken ct = default)
    {
        if (_mine is { } cached) return cached;
        if (current.Id is not { } id) return _mine = new HashSet<string>();
        return _mine = await ForUserAsync(id, ct);
    }

    public async Task<bool> HasAsync(string key, CancellationToken ct = default) =>
        (await ForCurrentUserAsync(ct)).Contains(key);

    public async Task<IReadOnlySet<string>> ForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.RoleId, u.Status })
            .FirstOrDefaultAsync(ct);

        // A suspended account holds nothing: its sessions are already dead,
        // but an API token or a race should not outlive the suspension.
        if (account is null || account.Status != UserStatus.Active) return new HashSet<string>();

        var granted = await GrantsAsync(account.RoleId, account.Role, ct);
        if (account.Role != UserRole.Owner) return granted;

        var withReserved = new HashSet<string>(granted);
        foreach (var reserved in InstancePermissions.Reserved) withReserved.Add(reserved.Key);
        return withReserved;
    }

    public async Task<bool> AnonymousHasAsync(string key, CancellationToken ct = default) =>
        (await GrantsAsync(null, UserRole.Member, ct)).Contains(key);

    private async Task<IReadOnlySet<string>> GrantsAsync(Guid? roleId, UserRole tier, CancellationToken ct)
    {
        if (roleId is null)
        {
            // Before the seed has run, or for an account it has not reached:
            // the tier's built-in role, by key.
            roleId = await cache.BuiltInIdAsync(Role.Keys.For(tier), async () =>
                await db.Roles.AsNoTracking()
                    .Where(r => r.Key == Role.Keys.For(tier))
                    .Select(r => (Guid?)r.Id)
                    .FirstOrDefaultAsync(ct));
            // No roles at all yet (a fresh database mid-startup): fall back to
            // the catalog's defaults rather than refusing everything.
            if (roleId is null) return new HashSet<string>(InstancePermissions.DefaultsFor(tier));
        }

        return await cache.GrantsAsync(roleId.Value, async () =>
        {
            var keys = await db.RolePermissions.AsNoTracking()
                .Where(p => p.RoleId == roleId)
                .Select(p => p.Key)
                .ToListAsync(ct);
            // Keys the catalog has since dropped mean nothing.
            return new HashSet<string>(keys.Where(InstancePermissions.IsAssignable));
        });
    }
}

/// <summary>
/// Holds role grants across requests. A singleton, because the service that
/// reads it is scoped (it needs the request's DbContext), so the cache cannot
/// live on it. Every write to a role calls <see cref="Invalidate"/>.
/// </summary>
public sealed class PermissionCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, (IReadOnlySet<string> Keys, DateTimeOffset At)> _grants = [];
    private readonly Dictionary<string, Guid> _builtIns = [];

    public async Task<IReadOnlySet<string>> GrantsAsync(Guid roleId, Func<Task<HashSet<string>>> load)
    {
        lock (_gate)
        {
            if (_grants.TryGetValue(roleId, out var hit) && DateTimeOffset.UtcNow - hit.At <= Ttl)
                return hit.Keys;
        }

        var loaded = await load();
        lock (_gate) _grants[roleId] = (loaded, DateTimeOffset.UtcNow);
        return loaded;
    }

    public async Task<Guid?> BuiltInIdAsync(string key, Func<Task<Guid?>> load)
    {
        lock (_gate)
        {
            if (_builtIns.TryGetValue(key, out var hit)) return hit;
        }

        var loaded = await load();
        if (loaded is { } id) lock (_gate) _builtIns[key] = id;
        return loaded;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _grants.Clear();
            _builtIns.Clear();
        }
    }
}
