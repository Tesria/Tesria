using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;

namespace Tesria.Api.Features.Mcp;

/// <summary>
/// What assistants do through MCP, for administrators (the owner,
/// 2026-09-24: "keep an eye on what agents are doing"). A filter around
/// every tool call: it counts the call against its token for the day and
/// writes a line to the tool log, with the page or space it was about and
/// whether it worked. Recording never changes the tool's answer.
/// </summary>
public static class McpActivity
{
    /// <summary>The tools that change something; the rest only read.</summary>
    public static readonly IReadOnlySet<string> WriteTools =
        new HashSet<string>(StringComparer.Ordinal) { "create_page", "update_page", "add_page_label", "remove_page_label" };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (request, ct) =>
    {
        CallToolResult? result = null;
        string? error = null;
        try
        {
            result = await next(request, ct);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            await RecordAsync(request, result, error);
        }
    };

    private static async Task RecordAsync(RequestContext<CallToolRequestParams> request, CallToolResult? result, string? error)
    {
        var services = request.Services;
        var tokenClaim = request.User?.FindFirst(TokenUsage.TokenIdClaim)?.Value;
        if (services is null || !Guid.TryParse(tokenClaim, out var tokenId)) return;
        var db = services.GetRequiredService<AppDbContext>();
        var log = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(McpActivity));
        try
        {
            var tool = request.Params?.Name ?? "(unknown)";
            var write = WriteTools.Contains(tool);
            var ok = error is null && result?.IsError != true;
            // A change counts only if it happened; a refused one is still a call.
            await TokenUsage.RecordAsync(db, tokenId, write && ok ? TokenUsage.Kind.McpWrite : TokenUsage.Kind.McpRead, log);

            var token = await db.ApiTokens.AsNoTracking().Where(t => t.Id == tokenId)
                .Select(t => new { t.Name, t.Prefix, t.UserId }).FirstOrDefaultAsync();
            if (token is null) return;
            var args = request.Params?.Arguments;
            db.McpToolCalls.Add(new McpToolCall
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                TokenName = token.Name,
                TokenPrefix = token.Prefix,
                UserId = token.UserId,
                Tool = tool.Length > 64 ? tool[..64] : tool,
                Write = write,
                PageId = GuidArg(args, "pageId") ?? (ok && tool == "create_page" ? CreatedId(result) : null),
                SpaceKey = StringArg(args, "spaceKey")?.ToUpperInvariant() is { Length: <= 64 } key ? key : null,
                Ok = ok,
                Error = Trim(error ?? (result?.IsError == true ? FirstText(result) : null)),
                At = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Could not record an MCP tool call");
        }
    }

    private static Guid? GuidArg(IDictionary<string, JsonElement>? args, string name) =>
        args is not null && args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String
            && Guid.TryParse(v.GetString(), out var id) ? id : null;

    private static string? StringArg(IDictionary<string, JsonElement>? args, string name) =>
        args is not null && args.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? FirstText(CallToolResult? result) =>
        result?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    /// <summary>The new page's id, from create_page's answer ({"id": …}).</summary>
    private static Guid? CreatedId(CallToolResult? result)
    {
        if (FirstText(result) is not { } text) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var name in new[] { "id", "Id" })
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var v)
                    && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id))
                    return id;
        }
        catch (JsonException) { }
        return null;
    }

    private static string? Trim(string? s) => s is { Length: > 500 } ? s[..500] : s;
}
