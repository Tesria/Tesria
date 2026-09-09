using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Security;

/// <summary>
/// Windows and thresholds for the detectors (dev-plan 3.3). Constants on
/// purpose: tuning them is a decision for someone reading this file, not a
/// field to mis-set under pressure. The table in architecture.md mirrors
/// these; change both.
/// </summary>
public static class SecurityThresholds
{
    public static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(1);

    public const int FailedLoginsPerAddress = 20;
    public static readonly TimeSpan FailedLoginWindow = TimeSpan.FromMinutes(10);

    public const int DistinctAccountsPerAddress = 5;

    public const int LockoutsPerAccount = 3;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromHours(1);

    public const int DeniedResponsesPerAddress = 100;
    public static readonly TimeSpan DeniedWindow = TimeSpan.FromMinutes(5);

    public const int PagesRemovedPerActor = 10;
    public static readonly TimeSpan RemovalWindow = TimeSpan.FromMinutes(10);

    public const int TokensPerActor = 5;
    public static readonly TimeSpan TokenWindow = TimeSpan.FromMinutes(10);

    public const int Registrations = 10;
    public static readonly TimeSpan RegistrationWindow = TimeSpan.FromMinutes(10);

    /// <summary>How far back an admin's sign-in history is consulted for "new address".</summary>
    public const int AdminAddressHistory = 200;
}

/// <summary>
/// In-process sliding-window counters and alert cooldowns. A restart forgets
/// them, which is acceptable: written events are not lost, and an attack that
/// outlives a restart crosses its threshold again.
/// </summary>
public sealed class SecurityCounters
{
    private const int MaxKeys = 20_000;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _hits = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _distinct = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new();

    /// <summary>Records one hit and returns how many fall inside the window.</summary>
    public int Hit(string kind, string key, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var queue = _hits.GetOrAdd($"{kind}|{key}", _ => new ConcurrentQueue<DateTimeOffset>());
        queue.Enqueue(now);
        while (queue.TryPeek(out var oldest) && now - oldest > window) queue.TryDequeue(out _);
        if (_hits.Count > MaxKeys) Prune(now, window);
        return queue.Count;
    }

    /// <summary>Records a member of a set and returns how many distinct members the window holds.</summary>
    public int Distinct(string kind, string key, string member, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var set = _distinct.GetOrAdd($"{kind}|{key}", _ => new ConcurrentDictionary<string, DateTimeOffset>());
        set[member] = now;
        foreach (var (m, seen) in set)
            if (now - seen > window) set.TryRemove(m, out _);
        return set.Count;
    }

    /// <summary>True once per cooldown per (kind, key): the caller may alert.</summary>
    public bool TryStartCooldown(string kind, string key)
    {
        var now = DateTimeOffset.UtcNow;
        var id = $"{kind}|{key}";
        var allowed = false;
        _cooldowns.AddOrUpdate(id,
            _ => { allowed = true; return now; },
            (_, since) =>
            {
                if (now - since < SecurityThresholds.AlertCooldown) return since;
                allowed = true;
                return now;
            });
        return allowed;
    }

    private void Prune(DateTimeOffset now, TimeSpan window)
    {
        foreach (var (key, queue) in _hits)
            if (!queue.TryPeek(out var newest) || now - newest > window) _hits.TryRemove(key, out _);
    }
}

/// <summary>
/// Addresses nothing on the internet should be asked to fetch: loopback,
/// private, link-local (including the cloud metadata service), ULA,
/// multicast, unspecified. Shared by the detector (alerts) and dev-plan 3.4's
/// egress guard (blocks).
/// </summary>
public static class PrivateNetworks
{
    public static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254) // link-local incl. 169.254.169.254
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) // CGNAT
                || bytes[0] == 0
                || bytes[0] >= 224; // multicast and reserved
        }
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || address.IsIPv6UniqueLocal;
    }
}

public interface ISecurityDetector
{
    /// <summary>A sign-in failed. <paramref name="email"/> is hashed before storage.</summary>
    Task FailedLoginAsync(string? ip, string email);
    /// <summary>An account was just locked by 3.2.</summary>
    Task AccountLockedAsync(User user, string? ip);
    /// <summary>A sign-in succeeded; admins from an unseen address are flagged.</summary>
    Task SucceededLoginAsync(User user, string? ip);
    Task RegistrationAsync(string? ip);
    Task PagesRemovedAsync(Guid actorId, int count, string action, Guid pageId);
    Task TokenMintedAsync(Guid actorId);
    Task AdminPromotedAsync(Guid actorId, User promoted);
    Task PublicSpacesToggledAsync(Guid? actorId, bool enabled);
    /// <summary>A space was published to, or withdrawn from, the world (dev-plan 5.1). Always an alert.</summary>
    Task SpaceVisibilityChangedAsync(Guid actorId, Space space, bool isPublic);
    Task WebhookPrivateTargetAsync(Guid actorId, string url, Guid spaceId);
    Task AuditChainBrokenAsync(Audit.AuditChainReport report);
    /// <summary>Called by the denied-response middleware once its own counter crosses.</summary>
    Task DeniedSpikeAsync(string ip, int count);
}

/// <summary>
/// Writes security events and raises alerts. Queues on the caller's unit of
/// work — the caller's SaveChanges commits event, alert and notifications
/// together with whatever triggered them.
/// </summary>
public sealed class SecurityDetector(AppDbContext db, SecurityCounters counters, INotificationService notifications)
    : ISecurityDetector
{
    public async Task FailedLoginAsync(string? ip, string email)
    {
        if (ip is null) return;
        var normalized = (email ?? "").Trim().ToLowerInvariant();

        var perAddress = counters.Hit("login.failed_burst_ip", ip, SecurityThresholds.FailedLoginWindow);
        if (perAddress == SecurityThresholds.FailedLoginsPerAddress)
            await RaiseAsync("login.failed_burst_ip", SecuritySeverity.Warning, key: ip, ip: ip,
                alert: true, metadata: new { Failures = perAddress, WindowMinutes = SecurityThresholds.FailedLoginWindow.TotalMinutes });

        var accounts = counters.Distinct("login.credential_stuffing", ip, HashEmail(normalized), SecurityThresholds.FailedLoginWindow);
        if (accounts == SecurityThresholds.DistinctAccountsPerAddress)
            await RaiseAsync("login.credential_stuffing", SecuritySeverity.Critical, key: ip, ip: ip,
                alert: true, metadata: new { DistinctAccounts = accounts, WindowMinutes = SecurityThresholds.FailedLoginWindow.TotalMinutes });
    }

    public async Task AccountLockedAsync(User user, string? ip)
    {
        await RaiseAsync("account.locked", SecuritySeverity.Info, key: user.Id.ToString(), ip: ip,
            targetType: "user", targetId: user.Id, alert: false,
            metadata: new { user.FailedLoginCount, user.LockedUntil });

        var lockouts = counters.Hit("account.repeated_lockouts", user.Id.ToString(), SecurityThresholds.LockoutWindow);
        if (lockouts == SecurityThresholds.LockoutsPerAccount)
            await RaiseAsync("account.repeated_lockouts", SecuritySeverity.Warning, key: user.Id.ToString(), ip: ip,
                targetType: "user", targetId: user.Id, alert: true,
                metadata: new { Lockouts = lockouts, WindowMinutes = SecurityThresholds.LockoutWindow.TotalMinutes });
    }

    public async Task SucceededLoginAsync(User user, string? ip)
    {
        if (user.Role != UserRole.Admin || ip is null) return;

        // The account's sign-in history, from the audit log's Ip metadata.
        // No history at all means no baseline, and no alert: the first ever
        // sign-in of a brand-new admin is not suspicious.
        var history = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActorId == user.Id && a.Action == "user.login")
            .OrderByDescending(a => a.Sequence)
            .Take(SecurityThresholds.AdminAddressHistory)
            .Select(a => a.MetadataJson)
            .ToListAsync();
        if (history.Count == 0) return;

        var seen = history.Select(IpFromMetadata).Where(x => x is not null).ToHashSet();
        if (seen.Contains(ip)) return;

        await RaiseAsync("login.admin_new_address", SecuritySeverity.Warning, key: user.Id.ToString(), ip: ip,
            actorId: user.Id, targetType: "user", targetId: user.Id, alert: true,
            metadata: new { user.Email, KnownAddresses = seen.Count });
    }

    public async Task RegistrationAsync(string? ip)
    {
        var count = counters.Hit("registration.burst", "instance", SecurityThresholds.RegistrationWindow);
        if (count == SecurityThresholds.Registrations)
            await RaiseAsync("registration.burst", SecuritySeverity.Warning, key: "instance", ip: ip, alert: true,
                metadata: new { Registrations = count, WindowMinutes = SecurityThresholds.RegistrationWindow.TotalMinutes });
    }

    public async Task PagesRemovedAsync(Guid actorId, int count, string action, Guid pageId)
    {
        var total = 0;
        for (var i = 0; i < Math.Max(1, count); i++)
            total = counters.Hit("content.mass_removal", actorId.ToString(), SecurityThresholds.RemovalWindow);

        // ">= and cooldown" rather than "==": one request can jump the count
        // past the threshold by removing a whole subtree.
        if (total >= SecurityThresholds.PagesRemovedPerActor)
            await RaiseAsync("content.mass_removal", SecuritySeverity.Warning, key: actorId.ToString(),
                actorId: actorId, targetType: "page", targetId: pageId, alert: true,
                metadata: new { Pages = total, LastAction = action, WindowMinutes = SecurityThresholds.RemovalWindow.TotalMinutes });
    }

    public async Task TokenMintedAsync(Guid actorId)
    {
        var count = counters.Hit("token.minting_burst", actorId.ToString(), SecurityThresholds.TokenWindow);
        if (count == SecurityThresholds.TokensPerActor)
            await RaiseAsync("token.minting_burst", SecuritySeverity.Warning, key: actorId.ToString(),
                actorId: actorId, targetType: "user", targetId: actorId, alert: true,
                metadata: new { Tokens = count, WindowMinutes = SecurityThresholds.TokenWindow.TotalMinutes });
    }

    public Task AdminPromotedAsync(Guid actorId, User promoted) =>
        RaiseAsync("admin.promoted", SecuritySeverity.Warning, key: promoted.Id.ToString(),
            actorId: actorId, targetType: "user", targetId: promoted.Id, alert: true, cooldown: false,
            metadata: new { promoted.Email });

    public Task PublicSpacesToggledAsync(Guid? actorId, bool enabled) =>
        RaiseAsync("settings.public_spaces_toggled", SecuritySeverity.Critical, key: "instance",
            actorId: actorId, alert: true, cooldown: false, metadata: new { Enabled = enabled });

    public Task SpaceVisibilityChangedAsync(Guid actorId, Space space, bool isPublic) =>
        RaiseAsync(isPublic ? "space.published" : "space.unpublished", SecuritySeverity.Warning, key: space.Id.ToString(),
            actorId: actorId, targetType: "space", targetId: space.Id, alert: true, cooldown: false,
            metadata: new { space.Key, space.Name });

    public Task WebhookPrivateTargetAsync(Guid actorId, string url, Guid spaceId) =>
        RaiseAsync("webhook.private_target", SecuritySeverity.Warning, key: actorId.ToString(),
            actorId: actorId, targetType: "space", targetId: spaceId, alert: true, metadata: new { Url = url });

    public Task AuditChainBrokenAsync(Audit.AuditChainReport report) =>
        RaiseAsync("audit.chain_broken", SecuritySeverity.Critical, key: "instance", alert: true,
            metadata: new { report.BrokenAtSequence, report.Problem, report.Checked });

    public Task DeniedSpikeAsync(string ip, int count) =>
        RaiseAsync("http.denied_spike", SecuritySeverity.Warning, key: ip, ip: ip, alert: true,
            metadata: new { Denied = count, WindowMinutes = SecurityThresholds.DeniedWindow.TotalMinutes });

    /// <summary>
    /// Writes the event and, if it is alert-worthy and not in cooldown, the
    /// alert plus one notification per administrator.
    /// </summary>
    private async Task RaiseAsync(
        string kind, SecuritySeverity severity, string key, bool alert,
        string? ip = null, Guid? actorId = null, string? targetType = null, Guid? targetId = null,
        object? metadata = null, bool cooldown = true)
    {
        var now = DateTimeOffset.UtcNow;
        var evt = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Severity = severity,
            Key = key,
            Ip = ip,
            ActorId = actorId,
            TargetType = targetType,
            TargetId = targetId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAt = now,
        };
        db.SecurityEvents.Add(evt);

        if (!alert) return;
        if (cooldown && !counters.TryStartCooldown(kind, key)) return;

        var alertRow = new SecurityAlert
        {
            Id = Guid.NewGuid(),
            EventId = evt.Id,
            Kind = kind,
            Severity = severity,
            Key = key,
            Ip = ip,
            ActorId = actorId,
            Status = SecurityAlertStatus.Open,
            CreatedAt = now,
        };
        db.SecurityAlerts.Add(alertRow);

        await notifications.NotifyAdminsAsync("security.alert", alertRow.Id,
            new { Kind = kind, Severity = severity.ToString(), Key = key });
    }

    private static string HashEmail(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email)))[..16];

    private static string? IpFromMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Ip", out var ip) ? ip.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
