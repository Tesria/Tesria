using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Blocks;

/// <summary>
/// The candidate sets the kinds share: this page's subtree, its space, or
/// everywhere. Candidates only — every kind still runs them through
/// <see cref="BlockContext.VisibleAsync"/>, which is where the permission
/// rule lives (architecture.md, "Dynamic blocks", decision 4).
/// </summary>
public static class PageScope
{
    public sealed record Candidate(Guid Id, Guid SpaceId, string SpaceKey, string Title, DateTimeOffset UpdatedAt, Guid? ParentPageId);

    /// <summary>Current (published, untrashed) pages in the scope, newest-updated first.</summary>
    public static async Task<List<Candidate>> CandidatesAsync(BlockContext ctx, string scope, CancellationToken ct)
    {
        var query = ctx.Db.Pages.AsNoTracking().Where(p => p.Status == PageStatus.Current);
        query = scope switch
        {
            "all" => query,
            _ => query.Where(p => p.SpaceId == ctx.Host.SpaceId),
        };

        // Ordered in memory, not in SQL: SQLite (the test provider) cannot
        // ORDER BY a DateTimeOffset, and doing it here means both providers
        // order identically rather than only Postgres being exercised.
        var rows = (await query
            .Select(p => new Candidate(p.Id, p.SpaceId, p.Space!.Key, p.Title, p.UpdatedAt, p.ParentPageId))
            .ToListAsync(ct))
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();

        // "tree" is the host's descendants, which needs the parent links —
        // cheaper to narrow in memory than to recurse in SQL at wiki scale.
        if (scope == "tree")
        {
            var descendants = DescendantIds(rows, ctx.Host.Id);
            rows = rows.Where(r => descendants.Contains(r.Id)).ToList();
        }
        return rows;
    }

    /// <summary>The page and everything beneath it, from an already-loaded flat set.</summary>
    public static HashSet<Guid> DescendantIds(IEnumerable<Candidate> all, Guid rootId)
    {
        var byParent = all.ToLookup(p => p.ParentPageId);
        var found = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>([rootId]);
        while (queue.Count > 0)
            foreach (var child in byParent[queue.Dequeue()])
                if (found.Add(child.Id)) queue.Enqueue(child.Id);
        return found;
    }
}
