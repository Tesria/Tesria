using Tesria.Api.Infrastructure.Settings;

namespace Tesria.Api.Features.Embeds;

/// <summary>
/// The two questions an embed or a smart link asks the server, never the
/// client, because both answers are trust decisions.
/// </summary>
public static class EmbedEndpoints
{
    public record EmbedResponse(bool Allowed, string? Url, string? Provider, string? AspectRatio, string? Reason);
    public record LinkPreviewResponse(string Url, string? Title, string? Description, string? SiteName, string? ImageUrl, string? Error);

    public static IEndpointRouteBuilder MapEmbedEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/embeds").WithTags("Embeds");
        // Readable by anonymous visitors for the same reason a page's other
        // content is: an embed on a public page is part of that page.
        group.MapGet("/resolve", Resolve).AllowAnonymous();
        // Unfurling makes an outbound request, so it needs an account: an
        // anonymous reader gets whatever is already cached, nothing more.
        group.MapGet("/unfurl", Unfurl).RequireAuthorization();
        return routes;
    }

    private static async Task<IResult> Resolve(string? url, ISiteSettingsService settings, CancellationToken ct)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return Results.Ok(new EmbedResponse(false, null, null, null, "Enter a full web address."));

        var entries = EmbedAllowlist.Parse((await settings.GetAsync(ct)).EmbedAllowlist);
        if (!EmbedAllowlist.IsAllowed(uri.Host, entries))
            return Results.Ok(new EmbedResponse(false, null, null, null,
                $"{uri.Host} is not on this site's embed allowlist. An administrator can add it in Admin → Settings."));

        // Allowed, but still narrowed where we know how: a YouTube watch page
        // becomes the no-cookie player rather than the whole site in a frame.
        var resolved = EmbedProviders.Resolve(uri);
        return Results.Ok(resolved is null
            ? new EmbedResponse(true, uri.ToString(), uri.Host, null, null)
            : new EmbedResponse(true, resolved.Url, resolved.Provider, resolved.AspectRatio, null));
    }

    private static async Task<IResult> Unfurl(string? url, ILinkPreviewService previews, CancellationToken ct)
    {
        var trimmed = (url ?? "").Trim();
        if (trimmed.Length == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["A web address is required."] });

        var preview = await previews.GetAsync(trimmed, ct);
        return Results.Ok(new LinkPreviewResponse(
            preview.Url, preview.Title, preview.Description, preview.SiteName, preview.ImageUrl, preview.Error));
    }
}
