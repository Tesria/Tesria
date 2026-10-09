using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Permissions;

/// <summary>
/// Gives every space its four groups (dev-plan 21.1), at start, in C# rather
/// than in the migration, because tests build the schema without migrations
/// and the seed has to be the same code in both. The owner decided
/// (2026-10-02): existing spaces get their groups, and grants to individual
/// people move into them (View into Viewers, Edit into Editors, Admin into
/// Admins). Group grants stay grants.
/// <para>
/// Per space and in a transaction, and only for a space that has no groups
/// yet: so it is idempotent, and never recomputes a space someone has since
/// changed. A space with no grants at all was open to everyone signed in, and
/// gets an EveryoneAccess of Admin, which keeps it exactly as open; every
/// other space gets none. A grant to the built-in Users group stays a grant:
/// it is an explicit admin (who administers it, and lifts restrictions) in a way EveryoneAccess
/// is not. Each moved grant is recorded in <see cref="SpaceGrantMove"/>, and
/// the audit log gets one entry per space.
/// </para>
/// </summary>
public static class SpaceGroupSeed
{
    public static async Task<int> EnsureAsync(AppDbContext db, Audit.IAuditLogger audit, ILogger log)
    {
        var pending = await db.Spaces.AsNoTracking()
            .Where(s => !db.Groups.Any(g => g.SpaceId == s.Id))
            .OrderBy(s => s.Key)
            .Select(s => s.Id)
            .ToListAsync();
        if (pending.Count == 0) return 0;

        // Nobody's access changes, so there is nothing for live editing to
        // revoke, and the sidecar may not be up yet.
        using var quiet = Collab.CollabRevocationInterceptor.Suppressed();
        var done = 0;
        foreach (var spaceId in pending)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
            // Again inside the transaction: a second process may have just done it.
            if (await db.Groups.AnyAsync(g => g.SpaceId == spaceId)) continue;

            // Deleted since the list was read: nothing to give groups to.
            var space = await db.Spaces.FirstOrDefaultAsync(s => s.Id == spaceId);
            if (space is null) continue;
            var rows = await db.SpacePermissions.Where(p => p.SpaceId == spaceId).ToListAsync();
            var now = DateTimeOffset.UtcNow;
            space.EveryoneAccess = rows.Count == 0 ? SpaceOperation.Admin : null;
            var groups = SpaceGroups.Add(db, spaceId, now);

            var moved = 0;
            var added = new HashSet<(Guid Group, Guid User)>();
            foreach (var row in rows.Where(r => r.PrincipalType == PrincipalType.User))
            {
                // A grant to an account that no longer exists grants nothing
                // and cannot be a membership; it is left as it is.
                if (!await db.Users.AnyAsync(u => u.Id == row.PrincipalId))
                {
                    log.LogWarning("Left the grant {Grant} on space {Key} in place: it names no account.", row.Id, space.Key);
                    continue;
                }
                var group = groups[SpaceGroups.RoleFor(row.Operation)];
                if (added.Add((group.Id, row.PrincipalId)))
                    db.UserGroups.Add(new UserGroup { GroupId = group.Id, UserId = row.PrincipalId, AddedAt = now });
                db.SpaceGrantMoves.Add(new SpaceGrantMove
                {
                    Id = Guid.NewGuid(),
                    SpaceId = spaceId,
                    GrantId = row.Id,
                    UserId = row.PrincipalId,
                    Operation = row.Operation,
                    GrantCreatedAt = row.CreatedAt,
                    GroupId = group.Id,
                    MovedAt = now,
                });
                db.SpacePermissions.Remove(row);
                moved++;
            }

            audit.Record("space.groups_created", "space", spaceId, new
            {
                space.Key,
                space.Name,
                EveryoneAccess = space.EveryoneAccess?.ToString(),
                MovedGrants = moved,
                KeptGrants = rows.Count - moved,
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            db.ChangeTracker.Clear();
            done++;
            }
            catch (DbUpdateException ex)
            {
                // Another instance starting at the same moment did this space
                // first (its groups' unique index, or the grants it already
                // moved). Its result stands; this start carries on rather than
                // failing (found by the 21.1 code review).
                await tx.RollbackAsync();
                db.ChangeTracker.Clear();
                log.LogInformation("Space {Space} was given its groups by another start ({Error}); skipped.", spaceId, ex.GetBaseException().Message);
            }
        }
        if (done > 0)
            log.LogInformation("Gave {Count} space(s) their Viewers, Editors, Admins and Reviewers groups (dev-plan 21.1).", done);
        return done;
    }
}
