using Tesria.Api.Domain;
using Tesria.Api.Features.Pages;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Search;

/// <summary>
/// Rebuilds every page's search text when <see cref="SearchableText"/> has
/// changed since it was built (<see cref="SiteSettings.SearchTextVersion"/>),
/// run by the migrate step after the migrations. Without it, only pages
/// saved after an upgrade would get the new text, and an old page would keep
/// its title in its text, its chips missing and its blocks run together
/// until someone edited it.
///
/// <para>Idempotent and resumable: the version is recorded only once every
/// page is done, so a pass cut short starts again on the next start. Drafts
/// and pages in the trash are included, so a restored page is current too.
/// Measured: the seeded instance's 261 pages in under a second.</para>
/// </summary>
public static class SearchTextBackfill
{
    private const int Batch = 200;

    /// <returns>How many pages' text changed.</returns>
    public static async Task<int> RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        var settings = await db.SiteSettings.AsNoTracking()
            .Where(s => s.Id == SiteSettings.SingletonId)
            .Select(s => new { s.SearchTextVersion })
            .FirstOrDefaultAsync(ct);
        // No settings row yet: a new instance, with no pages to rebuild.
        if (settings is null || settings.SearchTextVersion >= SearchableText.Version) return 0;

        var changed = 0;
        Guid? after = null;
        while (true)
        {
            var query = db.Pages.IgnoreQueryFilters().AsNoTracking();
            if (after is { } last) query = query.Where(p => p.Id.CompareTo(last) > 0);
            var pages = await query
                .OrderBy(p => p.Id)
                .Take(Batch)
                .Select(p => new { p.Id, p.SearchText, Content = p.CurrentVersion != null ? p.CurrentVersion.ContentJson : null })
                .ToListAsync(ct);
            if (pages.Count == 0) break;
            var updates = pages
                .Select(p => (p.Id, Text: PageContent.BuildSearchText(p.Content ?? ""), Old: p.SearchText))
                .Where(p => p.Text != p.Old)
                .ToList();
            if (updates.Count > 0)
            {
                // One statement per batch rather than per page: the round
                // trips, not the text, were most of the time.
                var ids = updates.Select(u => u.Id).ToArray();
                var texts = updates.Select(u => u.Text).ToArray();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    update "Pages" as p set "SearchText" = v.t
                    from unnest({ids}, {texts}) as v(id, t)
                    where p."Id" = v.id
                    """, ct);
                changed += updates.Count;
            }
            after = pages[^1].Id;
        }

        await db.SiteSettings.Where(s => s.Id == SiteSettings.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SearchTextVersion, SearchableText.Version), ct);
        return changed;
    }
}
