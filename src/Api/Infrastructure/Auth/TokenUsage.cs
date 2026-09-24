using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Counts what API tokens do, a row per token per day (the admin API tokens
/// tab). One statement that adds to today's row or starts it, so two
/// requests at once cannot both try to create it; Postgres and SQLite (the
/// tests) both read this form of upsert. Counting is never a reason for a
/// request to fail, so a failure is logged and forgotten.
/// </summary>
public static class TokenUsage
{
    public enum Kind { Read, Write, McpRead, McpWrite }

    /// <summary>The claim naming the token behind a principal, for MCP's tool log.</summary>
    public const string TokenIdClaim = "tesria:token_id";

    public static async Task RecordAsync(AppDbContext db, Guid tokenId, Kind kind, ILogger? log = null, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        int r = kind == Kind.Read ? 1 : 0, w = kind == Kind.Write ? 1 : 0;
        int mr = kind == Kind.McpRead ? 1 : 0, mw = kind == Kind.McpWrite ? 1 : 0;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ApiTokenDays" ("TokenId", "Day", "Reads", "Writes", "McpReads", "McpWrites")
                VALUES ({tokenId}, {day}, {r}, {w}, {mr}, {mw})
                ON CONFLICT ("TokenId", "Day") DO UPDATE SET
                    "Reads" = "ApiTokenDays"."Reads" + excluded."Reads",
                    "Writes" = "ApiTokenDays"."Writes" + excluded."Writes",
                    "McpReads" = "ApiTokenDays"."McpReads" + excluded."McpReads",
                    "McpWrites" = "ApiTokenDays"."McpWrites" + excluded."McpWrites"
                """, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.LogWarning(ex, "Could not count a use of API token {TokenId}", tokenId);
        }
    }

    /// <summary>How long the day rows and the MCP tool log are kept.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(90);

    /// <summary>Removes what is older than <see cref="Keep"/>.</summary>
    public static async Task PruneAsync(AppDbContext db, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - Keep;
        var cutoffDay = DateOnly.FromDateTime(cutoff.UtcDateTime);
        await db.ApiTokenDays.Where(d => d.Day < cutoffDay).ExecuteDeleteAsync(ct);
        // Loaded rather than filtered in SQL: SQLite, the test provider,
        // cannot compare a DateTimeOffset. The log is bounded by Keep anyway.
        var old = (await db.McpToolCalls.Select(c => new { c.Id, c.At }).ToListAsync(ct))
            .Where(c => c.At < cutoff).Select(c => c.Id).ToList();
        if (old.Count > 0) await db.McpToolCalls.Where(c => old.Contains(c.Id)).ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// Counts each REST request made with an API token, once it has been
/// answered: a change that was refused (read-only token, no permission) is a
/// request but not a change. MCP arrives as POST whatever the tool does, so
/// its calls are counted by the tool filter (<c>McpActivity</c>), which knows
/// the tool and the outcome.
/// </summary>
public sealed class TokenUsageMiddleware(RequestDelegate next, ILogger<TokenUsageMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        await next(context);
        if (context.Request.Path.StartsWithSegments("/mcp")) return;
        if (!Guid.TryParse(context.User.FindFirst(TokenUsage.TokenIdClaim)?.Value, out var tokenId)) return;
        var method = context.Request.Method;
        var change = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            && context.Response.StatusCode < 400;
        await TokenUsage.RecordAsync(db, tokenId, change ? TokenUsage.Kind.Write : TokenUsage.Kind.Read, log);
    }
}
