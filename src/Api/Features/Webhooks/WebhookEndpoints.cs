using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Webhooks;

public static class WebhookEndpoints
{
    public record CreateWebhookRequest(string Url, string Events);
    public record CreatedWebhookResponse(Guid Id, string Url, string Events, bool Enabled, string Secret);
    public record WebhookResponse(Guid Id, string Url, string Events, bool Enabled, DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/spaces/{key}/webhooks").WithTags("Webhooks").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapDelete("/{id:guid}", Delete);
        return routes;
    }

    private static async Task<IResult> List(string key, AppDbContext db, IPermissionService perms)
    {
        var space = await FindSpaceAsync(db, key);
        if (space is null) return Results.NotFound();
        // A webhook's URL and event filter are configuration, not content —
        // still, only space admins should see or manage this integration.
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var hooks = await db.Webhooks.AsNoTracking()
            .Where(w => w.SpaceId == space.Id)
            .Select(w => new WebhookResponse(w.Id, w.Url, w.Events, w.Enabled, w.CreatedAt))
            .ToListAsync();
        return Results.Ok(hooks);
    }

    private static async Task<IResult> Create(
        string key, CreateWebhookRequest req, AppDbContext db, IPermissionService perms, CurrentUser current)
    {
        var space = await FindSpaceAsync(db, key);
        if (space is null) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.ValidationProblem(Error("url", "A valid http:// or https:// URL is required."));
        }

        var events = (req.Events ?? "").Trim();
        if (events.Length == 0)
            return Results.ValidationProblem(Error("events",
                "Specify one or more comma-separated events (e.g. \"page.updated,comment.created\"), or \"*\" for all."));

        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            SpaceId = space.Id,
            Url = req.Url,
            // Random secret, shown once, used to HMAC-sign delivered payloads.
            Secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            Events = events,
            Enabled = true,
            CreatedById = current.RequireId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync();

        return Results.Created($"/api/spaces/{key}/webhooks/{webhook.Id}",
            new CreatedWebhookResponse(webhook.Id, webhook.Url, webhook.Events, webhook.Enabled, webhook.Secret));
    }

    private static async Task<IResult> Delete(
        string key, Guid id, AppDbContext db, IPermissionService perms)
    {
        var space = await FindSpaceAsync(db, key);
        if (space is null) return Results.NotFound();
        if (!await perms.CanAdminSpaceAsync(space.Id)) return Results.Forbid();

        var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.SpaceId == space.Id);
        if (webhook is null) return Results.NotFound();
        db.Webhooks.Remove(webhook);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static Task<Space?> FindSpaceAsync(AppDbContext db, string key)
    {
        var normalized = (key ?? "").ToUpperInvariant();
        return db.Spaces.FirstOrDefaultAsync(s => s.Key == normalized);
    }

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
