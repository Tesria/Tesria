using System.Collections.Concurrent;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;

namespace Tesria.Api.Features.Export;

/// <summary>
/// How far along a site or pack export is (dev-plan 20.1).
///
/// <para>The export still runs inside its own request, as it always has, so
/// closing the tab or pressing Cancel aborts the request and with it the
/// work. What is new is a side channel: the page makes up an id, passes it
/// as <c>?progress=</c>, and reads <c>GET /api/export-progress/{id}</c> while
/// it waits. A job table was the alternative, and would let a closed tab come
/// back for its file; it would also mean running an export outside a
/// request, with its permissions and render token held somewhere else, which
/// is more than a progress bar is worth.</para>
///
/// <para>In memory, so a single app instance is assumed, as the settings
/// cache assumes it. An entry belongs to whoever started the export and is
/// forgotten ten minutes after its last change. It holds counts and the
/// title of the page being worked on, which the exporter can already read.</para>
/// </summary>
public sealed class ExportProgress(TimeProvider clock)
{
    public record Snapshot(string Stage, int Done, int Total, string? Current, DateTimeOffset StartedAt, bool Finished);

    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);
    /// <summary>A ceiling, because the ids come from callers.</summary>
    public const int MaxEntries = 1000;

    internal sealed class Entry
    {
        public required DateTimeOffset StartedAt { get; init; }
        public string Stage = "Starting";
        public int Done;
        public int Total;
        public string? Current;
        public bool Finished;
        public DateTimeOffset UpdatedAt;
    }

    private readonly TimeProvider _clock = clock;
    private readonly ConcurrentDictionary<(Guid? User, Guid Id), Entry> _entries = new();

    /// <summary>
    /// A reporter for one export. With no id, or an id already in use by
    /// this person, it reports to nobody: progress is a courtesy, never a
    /// reason for an export to fail.
    /// </summary>
    public Reporter Start(Guid? user, string? id)
    {
        if (!Guid.TryParse(id, out var guid)) return Reporter.None;
        Sweep();
        if (_entries.Count >= MaxEntries) return Reporter.None;
        var now = _clock.GetUtcNow();
        var entry = new Entry { StartedAt = now, UpdatedAt = now };
        return _entries.TryAdd((user, guid), entry) ? new Reporter(this, entry) : Reporter.None;
    }

    public Snapshot? Get(Guid? user, Guid id)
    {
        if (!_entries.TryGetValue((user, id), out var e)) return null;
        lock (e) return new Snapshot(e.Stage, e.Done, e.Total, e.Current, e.StartedAt, e.Finished);
    }

    private void Sweep()
    {
        var cutoff = _clock.GetUtcNow() - Keep;
        foreach (var (key, e) in _entries)
            if (e.UpdatedAt < cutoff) _entries.TryRemove(key, out _);
    }

    public sealed class Reporter
    {
        public static readonly Reporter None = new(null, null);
        private readonly ExportProgress? _owner;
        private readonly Entry? _entry;

        internal Reporter(ExportProgress? owner, Entry? entry) { _owner = owner; _entry = entry; }

        /// <summary>A new stage, counted from zero to <paramref name="total"/>.</summary>
        public void Stage(string stage, int total) => Update(e => { e.Stage = stage; e.Total = total; e.Done = 0; e.Current = null; });

        /// <summary>Starting on one item of the stage; <paramref name="current"/> names it.</summary>
        public void Working(string? current) => Update(e => e.Current = current);

        /// <summary>One item of the stage finished.</summary>
        public void Step() => Update(e => e.Done++);

        /// <summary>
        /// Built, and about to be sent. Also called when the export stops for
        /// any reason, so a page that is still asking learns to stop.
        /// </summary>
        public void Finish() => Update(e => { e.Finished = true; e.Current = null; });

        private void Update(Action<Entry> change)
        {
            if (_entry is null || _owner is null) return;
            lock (_entry)
            {
                change(_entry);
                _entry.UpdatedAt = _owner._clock.GetUtcNow();
            }
        }
    }
}

public static class ExportProgressEndpoints
{
    public static IEndpointRouteBuilder MapExportProgressEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/export-progress/{id:guid}", Get)
            .WithTags("Export")
            .RequirePermission(InstancePermissions.PagesExport);
        return routes;
    }

    private static IResult Get(Guid id, ExportProgress progress, CurrentUser current) =>
        progress.Get(current.Id, id) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound();
}
