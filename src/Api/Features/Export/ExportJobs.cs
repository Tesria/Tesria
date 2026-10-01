using System.Collections.Concurrent;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Notifications;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Export;

/// <summary>
/// A reason an export cannot be made: answered as the request's own refusal,
/// or written into a job's error when the checks are run again as it starts
/// (dev-plan 20.2).
/// </summary>
public sealed record ExportRefusal(int Status, string Message, string? Field = null, string? Code = null)
{
    public static readonly ExportRefusal NoSpace =
        new(StatusCodes.Status404NotFound, "That space does not exist, or you can no longer see it.");

    public static ExportRefusal TurnedOff(Space space, ExportFormat format) =>
        new(StatusCodes.Status403Forbidden, $"Exporting {space.Name} as {SpaceExports.Label(format)} is turned off for this space.",
            Code: "export_disabled");

    public IResult ToResult() => Status switch
    {
        StatusCodes.Status404NotFound => Results.NotFound(),
        StatusCodes.Status400BadRequest when Field is not null =>
            Results.ValidationProblem(new Dictionary<string, string[]> { [Field] = [Message] }),
        StatusCodes.Status503ServiceUnavailable => Results.Problem(detail: Message, statusCode: Status),
        _ => Results.Json(new { code = Code, message = Message }, statusCode: Status),
    };
}

/// <summary>
/// What the runner and the endpoints share (dev-plan 20.2): a nudge when a
/// job is queued, and the way to stop a running one. The jobs themselves are
/// rows; this holds only what cannot be.
/// </summary>
public sealed class ExportJobQueue
{
    /// <summary>
    /// Where prepared files wait. The container's temporary folder, not the
    /// uploads volume: a prepared export is a copy, it must not end up in
    /// backups or offsite, and a restart losing it costs only preparing it
    /// again.
    /// </summary>
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "tesria-exports");

    /// <summary>How long a prepared file is kept.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromHours(24);

    /// <summary>How long a finished job stays in its owner's list.</summary>
    public static readonly TimeSpan Forget = TimeSpan.FromDays(7);

    /// <summary>Unfinished jobs one person may have at once.</summary>
    public const int PerPerson = 3;

    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public string PathOf(Guid jobId) => Path.Combine(Folder, $"{jobId:N}.zip");

    public void Signal()
    {
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    internal CancellationTokenSource Register(Guid jobId, CancellationToken stopping)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _running[jobId] = cts;
        return cts;
    }

    internal void Unregister(Guid jobId)
    {
        if (_running.TryRemove(jobId, out var cts)) cts.Dispose();
    }

    /// <summary>Stops a running job. False when it is not running here.</summary>
    public bool Cancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts)) return false;
        try { cts.Cancel(); } catch (ObjectDisposedException) { return false; }
        return true;
    }
}

public static class ExportJobEndpoints
{
    public record StartExportRequest(string SpaceKey, string Format, string? Audience = null, string? Style = null);

    public record ExportJobResponse(
        Guid Id, string Format, string SpaceKey, string SpaceName, string? Audience, string Status,
        string? Stage, int Done, int Total, string? Current, int? Position, string? Error,
        string? FileName, long? FileSize,
        DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, DateTimeOffset? ExpiresAt);

    public static IEndpointRouteBuilder MapExportJobEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/exports")
            .WithTags("Export")
            .RequireAuthorization()
            .RequirePermission(InstancePermissions.PagesExport);
        group.MapPost("/", Start).Produces<ExportJobResponse>(StatusCodes.Status202Accepted);
        group.MapGet("/", List).Produces<List<ExportJobResponse>>();
        group.MapGet("/{id:guid}", Get).Produces<ExportJobResponse>();
        group.MapPost("/{id:guid}/cancel", Cancel).Produces<ExportJobResponse>();
        group.MapDelete("/{id:guid}", Remove).Produces(StatusCodes.Status204NoContent);
        group.MapGet("/{id:guid}/file", Download).Produces(StatusCodes.Status200OK, contentType: "application/zip");
        return routes;
    }

    /// <summary>
    /// Queues a site or pack export of a space. The checks the export itself
    /// makes are made here first, so a refusal is said at once rather than
    /// arriving later as a failed job; they are made again when it runs.
    /// </summary>
    private static async Task<IResult> Start(
        StartExportRequest req, AppDbContext db, CurrentUser current, IPermissionService perms,
        IPdfRenderer renderer, Infrastructure.Export.IRenderTokens renderTokens, ExportJobQueue queue,
        ExportProgress tracker, CancellationToken ct)
    {
        var format = req.Format?.Trim().ToLowerInvariant();
        if (format is not ("site" or "pack"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["format"] = ["Choose site or pack."] });
        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == (req.SpaceKey ?? "").ToUpperInvariant(), ct);

        var anonymous = !string.Equals(req.Audience, "me", StringComparison.OrdinalIgnoreCase);
        if (format == "site")
        {
            if (await SiteExportEndpoints.CheckAsync(space, anonymous, perms, renderer, renderTokens, ct) is { } refused)
                return refused.ToResult();
            var pages = await SiteExportEndpoints.VisiblePagesAsync(db, anonymous ? perms.AsAnonymous() : perms, space!.Id, ct);
            if (SiteExportEndpoints.PageLimit(Count(pages)) is { } tooMany) return tooMany.ToResult();
        }
        else if (await PackExportEndpoints.CheckAsync(space, perms) is { } refused)
        {
            return refused.ToResult();
        }

        var userId = current.RequireId();
        var unfinished = await db.ExportJobs.CountAsync(j => j.UserId == userId
            && (j.Status == ExportJobStatus.Queued || j.Status == ExportJobStatus.Running), ct);
        if (unfinished >= ExportJobQueue.PerPerson)
            return Results.Json(new
            {
                code = "too_many_exports",
                message = $"You have {unfinished} exports being prepared already. Wait for one to finish, or cancel one.",
            }, statusCode: StatusCodes.Status429TooManyRequests);

        var job = new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SpaceId = space!.Id,
            Format = format,
            Audience = format == "site" ? (anonymous ? "anonymous" : "me") : null,
            Style = format == "site" && string.Equals(req.Style, "glass", StringComparison.OrdinalIgnoreCase) ? "glass" : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ExportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        queue.Signal();

        var response = await DescribeAsync(db, tracker, [job], ct);
        return Results.Json(response[0], statusCode: StatusCodes.Status202Accepted);
    }

    private static int Count(IReadOnlyList<SiteExport.PageNode> nodes) => nodes.Sum(n => 1 + Count(n.Children));

    /// <summary>The caller's own exports, newest first.</summary>
    private static async Task<IResult> List(AppDbContext db, CurrentUser current, ExportProgress tracker, CancellationToken ct)
    {
        var userId = current.RequireId();
        var jobs = (await db.ExportJobs.AsNoTracking().Where(j => j.UserId == userId).ToListAsync(ct))
            .OrderByDescending(j => j.CreatedAt).ToList();
        return Results.Ok(await DescribeAsync(db, tracker, jobs, ct));
    }

    private static async Task<IResult> Get(Guid id, AppDbContext db, CurrentUser current, ExportProgress tracker, CancellationToken ct)
    {
        var job = await MineAsync(db, current, id, ct);
        return job is null ? Results.NotFound() : Results.Ok((await DescribeAsync(db, tracker, [job], ct))[0]);
    }

    /// <summary>A queued job is canceled at once; a running one stops at its next step.</summary>
    private static async Task<IResult> Cancel(
        Guid id, AppDbContext db, CurrentUser current, ExportJobQueue queue, ExportProgress tracker, CancellationToken ct)
    {
        var job = await MineAsync(db, current, id, ct, tracked: true);
        if (job is null) return Results.NotFound();
        if (job.Status == ExportJobStatus.Queued)
        {
            job.Status = ExportJobStatus.Canceled;
            job.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        else if (job.Status == ExportJobStatus.Running)
        {
            queue.Cancel(job.Id);
        }
        return Results.Ok((await DescribeAsync(db, tracker, [job], ct))[0]);
    }

    /// <summary>Takes it out of the list, with its file; stops it first if it is still going.</summary>
    private static async Task<IResult> Remove(Guid id, AppDbContext db, CurrentUser current, ExportJobQueue queue, CancellationToken ct)
    {
        var job = await MineAsync(db, current, id, ct, tracked: true);
        if (job is null) return Results.NotFound();
        if (job.Status == ExportJobStatus.Running) queue.Cancel(job.Id);
        TryDelete(queue.PathOf(job.Id));
        db.ExportJobs.Remove(job);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>The prepared file, to the person who asked for it and nobody else, while it is kept.</summary>
    private static async Task<IResult> Download(Guid id, AppDbContext db, CurrentUser current, ExportJobQueue queue, CancellationToken ct)
    {
        var job = await MineAsync(db, current, id, ct);
        if (job is null) return Results.NotFound();
        var path = queue.PathOf(job.Id);
        if (job.Status != ExportJobStatus.Ready || !File.Exists(path))
            return Results.Json(new
            {
                code = "export_not_ready",
                message = job.Status == ExportJobStatus.Ready || job.Status == ExportJobStatus.Expired
                    ? "This file is no longer kept. Prepare the export again."
                    : "This export is not ready yet.",
            }, statusCode: StatusCodes.Status409Conflict);
        return Results.File(path, "application/zip", job.FileName, enableRangeProcessing: true);
    }

    private static async Task<ExportJob?> MineAsync(AppDbContext db, CurrentUser current, Guid id, CancellationToken ct, bool tracked = false)
    {
        var userId = current.RequireId();
        var jobs = tracked ? db.ExportJobs : db.ExportJobs.AsNoTracking();
        return await jobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static async Task<List<ExportJobResponse>> DescribeAsync(
        AppDbContext db, ExportProgress tracker, List<ExportJob> jobs, CancellationToken ct)
    {
        var spaceIds = jobs.Select(j => j.SpaceId).Distinct().ToList();
        var spaces = await db.Spaces.AsNoTracking().Where(s => spaceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Key, s.Name }).ToDictionaryAsync(s => s.Id, ct);
        // Where each waiting job is in the line, for "Waiting: 2 ahead".
        var queued = jobs.Any(j => j.Status == ExportJobStatus.Queued)
            ? (await db.ExportJobs.AsNoTracking().Where(j => j.Status == ExportJobStatus.Queued || j.Status == ExportJobStatus.Running)
                .Select(j => new { j.Id, j.CreatedAt, j.Status }).ToListAsync(ct))
                .OrderBy(j => j.Status == ExportJobStatus.Running ? 0 : 1).ThenBy(j => j.CreatedAt).Select(j => j.Id).ToList()
            : [];

        return [.. jobs.Select(j =>
        {
            var progress = j.Status == ExportJobStatus.Running ? tracker.Get(j.UserId, j.Id) : null;
            var space = spaces.GetValueOrDefault(j.SpaceId);
            var position = j.Status == ExportJobStatus.Queued ? queued.IndexOf(j.Id) : -1;
            return new ExportJobResponse(
                j.Id, j.Format, space?.Key ?? "", space?.Name ?? "", j.Audience, j.Status.ToString().ToLowerInvariant(),
                progress?.Stage, progress?.Done ?? 0, progress?.Total ?? 0, progress?.Current,
                position >= 0 ? position : null, j.Error, j.FileName, j.FileSize,
                j.CreatedAt, j.StartedAt, j.FinishedAt, j.ExpiresAt);
        })];
    }
}

/// <summary>
/// Prepares queued exports one at a time (dev-plan 20.2). A site export drives
/// the PDF sidecar's browser page by page, so two at once would only halve
/// each other on a small machine.
/// </summary>
public sealed class ExportJobRunner(
    IServiceScopeFactory scopes, ExportJobQueue queue, ExportProgress tracker, IConfiguration config,
    ILogger<ExportJobRunner> log) : BackgroundService
{
    /// <summary>How often the queue is looked at without a nudge.</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);

    /// <summary>How often kept files and old jobs are cleared.</summary>
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // The tests run jobs themselves (RunQueuedAsync): their database is
        // one SQLite connection, which a loop beside the requests would share.
        if (!config.GetValue("Exports:RunInBackground", true)) return;
        try { await RecoverAsync(stopping); }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            log.LogWarning("Export jobs: could not tidy up after the last start ({Error})", ex.Message);
        }

        var nextSweep = DateTimeOffset.MinValue;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextSweep)
                {
                    nextSweep = DateTimeOffset.UtcNow + SweepEvery;
                    await SweepAsync(stopping);
                }
                if (await NextAsync(stopping) is { } next)
                {
                    await RunAsync(next, stopping);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database restarting, most likely: try again shortly.
                log.LogWarning("Export jobs: {Error}", ex.Message);
            }
            try { await queue.WaitAsync(Idle, stopping); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// After a restart: a job that was running has lost its work, and says so
    /// rather than spinning for ever; a ready file the restart took is gone;
    /// and a file no ready job owns is removed.
    /// </summary>
    public async Task RecoverAsync(CancellationToken ct) // public for tests
    {
        Directory.CreateDirectory(queue.Folder);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var jobs = await db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Running || j.Status == ExportJobStatus.Ready).ToListAsync(ct);
        foreach (var job in jobs)
        {
            if (job.Status == ExportJobStatus.Running)
            {
                job.Status = ExportJobStatus.Failed;
                job.Error = "Tesria restarted while this was being prepared. Start it again.";
                job.FinishedAt = now;
            }
            else if (!File.Exists(queue.PathOf(job.Id)))
            {
                job.Status = ExportJobStatus.Expired;
            }
        }
        await db.SaveChangesAsync(ct);

        var ready = jobs.Where(j => j.Status == ExportJobStatus.Ready).Select(j => queue.PathOf(j.Id)).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(queue.Folder))
            if (!ready.Contains(file)) ExportJobEndpoints.TryDelete(file);
    }

    /// <summary>Runs every queued job, oldest first, then returns. For tests.</summary>
    public async Task RunQueuedAsync(CancellationToken ct)
    {
        while (await NextAsync(ct) is { } next) await RunAsync(next, ct);
    }

    /// <summary>Deletes files kept long enough, and forgets jobs finished long ago.</summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        // Filtered in memory: SQLite, which the tests run on, cannot compare
        // a DateTimeOffset, and these are few.
        var finished = await db.ExportJobs
            .Where(j => j.Status != ExportJobStatus.Queued && j.Status != ExportJobStatus.Running).ToListAsync(ct);
        var changed = false;
        foreach (var job in finished)
        {
            if (job.Status == ExportJobStatus.Ready && job.ExpiresAt <= now)
            {
                ExportJobEndpoints.TryDelete(queue.PathOf(job.Id));
                job.Status = ExportJobStatus.Expired;
                changed = true;
            }
            if ((job.FinishedAt ?? job.CreatedAt) + ExportJobQueue.Forget <= now)
            {
                ExportJobEndpoints.TryDelete(queue.PathOf(job.Id));
                db.ExportJobs.Remove(job);
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    private async Task<Guid?> NextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queued = await db.ExportJobs.AsNoTracking().Where(j => j.Status == ExportJobStatus.Queued)
            .Select(j => new { j.Id, j.CreatedAt }).ToListAsync(ct);
        return queued.OrderBy(j => j.CreatedAt).Select(j => (Guid?)j.Id).FirstOrDefault();
    }

    /// <summary>
    /// Runs one job as the person who asked: their account and their right
    /// to export are checked again, then the export's own checks, as them.
    /// </summary>
    internal async Task RunAsync(Guid jobId, CancellationToken stopping)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, stopping);
        if (job is null || job.Status != ExportJobStatus.Queued) return;

        job.Status = ExportJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stopping);

        var path = queue.PathOf(job.Id);
        var report = tracker.Start(job.UserId, job.Id.ToString());
        using var cts = queue.Register(job.Id, stopping);
        var space = await db.Spaces.AsNoTracking().FirstOrDefaultAsync(s => s.Id == job.SpaceId, stopping);
        try
        {
            if (await RefusedAsync(sp, db, job, space, stopping) is { } why)
            {
                await FinishAsync(sp, db, job, space, ExportJobStatus.Failed, why, stopping);
                return;
            }

            Directory.CreateDirectory(queue.Folder);
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.Asynchronous))
            {
                var perms = sp.GetRequiredService<IPermissionService>().AsUser(job.UserId);
                if (job.Format == "site")
                {
                    var anonymous = job.Audience != "me";
                    report.Stage("Checking which pages go in", 0);
                    var pages = await SiteExportEndpoints.VisiblePagesAsync(db, anonymous ? perms.AsAnonymous() : perms, space!.Id, cts.Token);
                    await SiteExportEndpoints.WriteAsync(file, space, anonymous, job.Style, job.UserId, pages,
                        SiteExportEndpoints.SiteServices.From(sp), report, cts.Token);
                    job.FileName = SiteExportEndpoints.FileName(space);
                }
                else
                {
                    await PackExportEndpoints.WriteAsync(file, space!, perms, job.UserId,
                        PackExportEndpoints.PackServices.From(sp), report, cts.Token);
                    job.FileName = PackExportEndpoints.FileName(space!);
                }
                await file.FlushAsync(cts.Token);
                job.FileSize = file.Length;
            }
            job.ExpiresAt = DateTimeOffset.UtcNow + ExportJobQueue.Keep;
            await FinishAsync(sp, db, job, space, ExportJobStatus.Ready, null, stopping);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            ExportJobEndpoints.TryDelete(path);
            await FinishAsync(sp, db, job, space, ExportJobStatus.Canceled, null, CancellationToken.None);
        }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            log.LogError(ex, "Export job {Job} ({Format} of space {Space}) failed", job.Id, job.Format, job.SpaceId);
            ExportJobEndpoints.TryDelete(path);
            await FinishAsync(sp, db, job, space, ExportJobStatus.Failed,
                "It could not be prepared. The server log has the details; try again, and tell an administrator if it keeps failing.",
                CancellationToken.None);
        }
        finally
        {
            report.Finish();
            queue.Unregister(job.Id);
        }
    }

    /// <summary>Why this job may not run now, as the person who asked, or null.</summary>
    private static async Task<string?> RefusedAsync(IServiceProvider sp, AppDbContext db, ExportJob job, Space? space, CancellationToken ct)
    {
        var status = await db.Users.AsNoTracking().Where(u => u.Id == job.UserId).Select(u => (UserStatus?)u.Status).FirstOrDefaultAsync(ct);
        if (status != UserStatus.Active) return "Your account is no longer active.";
        var rights = await sp.GetRequiredService<IInstancePermissions>().ForUserAsync(job.UserId, ct);
        if (!rights.Contains(InstancePermissions.PagesExport)) return "Your role no longer allows exporting.";

        var perms = sp.GetRequiredService<IPermissionService>().AsUser(job.UserId);
        var refusal = job.Format == "site"
            ? await SiteExportEndpoints.CheckAsync(space, job.Audience != "me", perms,
                sp.GetRequiredService<IPdfRenderer>(), sp.GetRequiredService<Infrastructure.Export.IRenderTokens>(), ct)
            : await PackExportEndpoints.CheckAsync(space, perms);
        if (refusal is not null) return refusal.Message;
        if (job.Format == "site")
        {
            var pages = await SiteExportEndpoints.VisiblePagesAsync(db, job.Audience != "me" ? perms.AsAnonymous() : perms, space!.Id, ct);
            if (SiteExportEndpoints.PageLimit(CountAll(pages)) is { } tooMany) return tooMany.Message;
        }
        return null;
    }

    private static int CountAll(IReadOnlyList<SiteExport.PageNode> nodes) => nodes.Sum(n => 1 + CountAll(n.Children));

    private static async Task FinishAsync(
        IServiceProvider sp, AppDbContext db, ExportJob job, Space? space, ExportJobStatus status, string? error, CancellationToken ct)
    {
        job.Status = status;
        job.Error = error;
        job.FinishedAt = DateTimeOffset.UtcNow;
        // The bell says so on any page and any device: ready, or why not. A
        // cancel was the person's own doing and needs no word.
        if (status is ExportJobStatus.Ready or ExportJobStatus.Failed)
            await sp.GetRequiredService<INotificationService>().NotifySystemAsync(
                job.UserId, status == ExportJobStatus.Ready ? "export.ready" : "export.failed", "export", job.Id,
                new { Title = space?.Name ?? "", job.Format });
        await db.SaveChangesAsync(ct);
    }
}
