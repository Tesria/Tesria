namespace Tesria.Api.Domain;

/// <summary>
/// One recorded read of a page, for the usage KPIs on the admin dashboard
/// (dev-plan 2.5). Raw rows rather than a rollup: the dashboard's queries are
/// bounded by a date range and indexed on <c>(PageId, ViewedAt)</c>, and a
/// daily rollup can be added later if volume demands it. Starting with a
/// rollup would throw away the detail before knowing which detail matters.
///
/// Recorded now, ahead of the dashboard that consumes it, so that when the
/// dashboard ships it has real history behind it rather than an empty chart.
/// </summary>
public class PageView
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }
    public Page? Page { get; set; }

    /// <summary>
    /// Null for an anonymous read. Nullable from the day the table is created
    /// even though nothing anonymous can reach a page yet — public read mode
    /// (dev-plan Phase 5) writes into this same table, and widening the column
    /// later would be a migration on a table that is large by then.
    /// </summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset ViewedAt { get; set; }
}
