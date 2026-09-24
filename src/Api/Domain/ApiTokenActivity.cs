namespace Tesria.Api.Domain;

/// <summary>
/// One token's use on one day (UTC), for the administrators' API tokens tab
/// (the owner, 2026-09-24: "I have no idea who has created tokens and how
/// often they are in use", and "keep an eye on what agents are doing").
/// REST requests are split into reads and changes by method; MCP tool calls
/// are counted apart, by whether the tool writes. Kept 90 days, including
/// after the token is revoked.
/// </summary>
public class ApiTokenDay
{
    public Guid TokenId { get; set; }
    public DateOnly Day { get; set; }
    public int Reads { get; set; }
    public int Writes { get; set; }
    public int McpReads { get; set; }
    public int McpWrites { get; set; }
}

/// <summary>
/// One tool an assistant called through MCP: which, as whom, on what, and
/// whether it worked. What it searched for is not kept (whether to log
/// search queries is an open privacy decision, dev-plan "Things this plan
/// deliberately does not decide"). Not tied to the token by a foreign key,
/// so revoking a token keeps the record of what it did; its name and prefix
/// are copied for the same reason. Kept 90 days.
/// </summary>
public class McpToolCall
{
    public Guid Id { get; set; }
    public Guid TokenId { get; set; }
    public required string TokenName { get; set; }
    public required string TokenPrefix { get; set; }
    public Guid UserId { get; set; }
    public required string Tool { get; set; }
    public bool Write { get; set; }
    public Guid? PageId { get; set; }
    public string? SpaceKey { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset At { get; set; }
}
