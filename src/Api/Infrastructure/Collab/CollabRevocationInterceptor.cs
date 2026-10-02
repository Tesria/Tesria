using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tesria.Api.Domain;

namespace Tesria.Api.Infrastructure.Collab;

/// <summary>
/// Ends live-editing connections when access changes (dev-plan 14.3), by
/// watching what is saved rather than each place that saves it, so a path
/// added later is covered too. After a save that changed any of these, the
/// sidecar is told to close the connections involved, and the editors
/// reconnect with a fresh token that the app only issues to people who may
/// still edit:
/// <list type="bullet">
/// <item>a user's status (suspended), security stamp (password changed,
/// signed out everywhere, two-factor turned off) or role: their connections;</item>
/// <item>a session revoked: its user's connections;</item>
/// <item>a group membership: that user's;</item>
/// <item>a space permission, or what everyone signed in may do in a space: that space's pages;</item>
/// <item>a page restriction: the pages of that page's space (restrictions
/// inherit, and closing a few extra connections costs a reconnect);</item>
/// <item>a group deleted, or a role's rights changed: everyone's.</item>
/// </list>
/// </summary>
public sealed class CollabRevocationInterceptor(IServiceScopeFactory scopes, ILogger<CollabRevocationInterceptor> log)
    : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<CollabRevocation>> _pending = new();

    private static readonly AsyncLocal<bool> Quiet = new();

    /// <summary>
    /// Saves inside the returned scope revoke nothing. Only for changes that
    /// keep everyone's access exactly as it was: the 21.1 seed moves grants
    /// into groups at start, before anyone is connected, and a call per space
    /// to a sidecar that may not be up yet would only slow the start down.
    /// </summary>
    public static IDisposable Suppressed()
    {
        Quiet.Value = true;
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => Quiet.Value = false;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        await SendAsync(eventData.Context);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SendAsync(eventData.Context).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context) _pending.Remove(context);
        base.SaveChangesFailed(eventData);
    }

    /// <summary>What the changes about to be saved would revoke. Public for tests.</summary>
    public static List<CollabRevocation> RevocationsIn(DbContext context)
    {
        var found = new List<CollabRevocation>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case User user when entry.State == EntityState.Modified
                    && (Changed(entry, nameof(User.Status)) || Changed(entry, nameof(User.SecurityStamp))
                        || Changed(entry, nameof(User.RoleId)) || Changed(entry, nameof(User.Role))):
                    found.Add(new(UserId: user.Id));
                    break;
                case UserSession session when entry.State == EntityState.Modified
                    && Changed(entry, nameof(UserSession.RevokedAt)) && session.RevokedAt is not null:
                    found.Add(new(UserId: session.UserId));
                    break;
                case UserGroup membership when entry.State is EntityState.Added or EntityState.Deleted:
                    found.Add(new(UserId: membership.UserId));
                    break;
                case SpacePermission permission when entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified:
                    found.Add(new(SpaceId: permission.SpaceId));
                    break;
                // What everyone signed in may do there (dev-plan 21.1).
                case Space space when entry.State == EntityState.Modified && Changed(entry, nameof(Space.EveryoneAccess)):
                    found.Add(new(SpaceId: space.Id));
                    break;
                case PageRestriction restriction when entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified:
                    found.Add(new(PageId: restriction.PageId));
                    break;
                case Group when entry.State == EntityState.Deleted:
                case RolePermission when entry.State is EntityState.Added or EntityState.Deleted:
                    found.Add(new(All: true));
                    break;
            }
        }
        return found.Distinct().ToList();
    }

    private static bool Changed(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string property) =>
        entry.Metadata.FindProperty(property) is not null && entry.Property(property).IsModified;

    private void Collect(DbContext? context)
    {
        if (context is null || Quiet.Value) return;
        var found = RevocationsIn(context);
        if (found.Count == 0) return;
        _pending.AddOrUpdate(context, found.Any(r => r.All) ? [new(All: true)] : found);
    }

    private async Task SendAsync(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var revocations)) return;
        _pending.Remove(context);
        try
        {
            using var scope = scopes.CreateScope();
            var notifier = scope.ServiceProvider.GetRequiredService<ICollabNotifier>();
            foreach (var revocation in revocations) await notifier.RevokeAsync(revocation);
        }
        catch (Exception ex)
        {
            // Never a reason for the save to look failed: it has succeeded.
            log.LogWarning(ex, "Could not tell the collaboration service about an access change");
        }
    }
}
