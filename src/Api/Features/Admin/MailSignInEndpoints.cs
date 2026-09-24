using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Email;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// The mail provider presets (dev-plan 18.1) and signing in to the mail
/// server with Microsoft or Google (18.2, 18.3). Everything here needs the
/// right to change the email server; starting a sign-in also needs a recent
/// password, since what it stores can send mail as that mailbox.
/// </summary>
public static class MailSignInEndpoints
{
    public record StartResponse(string Url, string RedirectUri, bool PasteBack);
    public record CompleteRequest(string? Address);

    public static IEndpointRouteBuilder MapMailSignInEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/settings/email").WithTags("Admin").RequireAuthorization();
        group.MapGet("/providers", () => Results.Ok(MailProviders.All))
            .RequirePermission(InstancePermissions.SettingsEmail);
        group.MapPost("/oauth/{provider}/start", Start).RequirePermission(InstancePermissions.SettingsEmail);
        group.MapPost("/oauth/complete", CompletePasted).RequirePermission(InstancePermissions.SettingsEmail);
        group.MapPost("/oauth/disconnect", Disconnect).RequirePermission(InstancePermissions.SettingsEmail);

        // The provider sends the browser here, from its own site: the session
        // cookie may not come with it (SameSite), so this is anonymous, and
        // the state, unguessable, single-use and ten minutes long, is what
        // names the administrator who started it.
        routes.MapGet(MailOAuthService.CallbackPath["/api".Length..], Callback).AllowAnonymous().ExcludeFromDescription();
        return routes;
    }

    private static MailSignIn? Parse(string provider) => provider.ToLowerInvariant() switch
    {
        "microsoft" => MailSignIn.Microsoft,
        "google" => MailSignIn.Google,
        _ => null,
    };

    private static async Task<IResult> Start(
        string provider, MailOAuthService oauth, ISiteSettingsService settings, CurrentUser current,
        HttpContext http, IConfiguration config, CancellationToken ct)
    {
        if (Parse(provider) is not { } kind) return Results.NotFound();
        if (Auth.AuthEndpoints.RequireSudo(http, config) is { } denied) return denied;
        var s = await settings.GetAsync(ct);
        try
        {
            var (url, redirect, pasteBack) = oauth.Start(kind, s, current.RequireId());
            return Results.Ok(new StartResponse(url, redirect, pasteBack));
        }
        catch (MailOAuthException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["provider"] = [ex.Message] });
        }
    }

    /// <summary>The redirect from the provider, for an instance it can return to.</summary>
    private static async Task<IResult> Callback(
        string? code, string? state, string? error, string? error_description,
        MailOAuthService oauth, AppDbContext db, IInstancePermissions permissions, CancellationToken ct)
    {
        var pending = oauth.Take(state);
        if (pending is null)
            return Back("error", "That sign-in had expired or was already used. Start it again.");
        if (error is not null)
            return Back("error", Refusal(error, error_description));
        var (ok, why) = await StillAllowedAsync(pending, db, permissions, ct);
        if (!ok) return Back("error", why!);
        try
        {
            var account = await oauth.CompleteAsync(pending, code ?? "", ct);
            return Back("connected", account);
        }
        catch (MailOAuthException ex)
        {
            return Back("error", ex.Message);
        }
    }

    /// <summary>
    /// The paste-back: the address the provider sent the browser to, copied
    /// from a page that could not load, for an instance on a LAN name.
    /// </summary>
    private static async Task<IResult> CompletePasted(
        CompleteRequest req, MailOAuthService oauth, AppDbContext db, IInstancePermissions permissions,
        ISiteSettingsService settings, CurrentUser current, IConfiguration config, CancellationToken ct)
    {
        static IResult Problem(string message) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { ["address"] = [message] });

        if (!Uri.TryCreate(req.Address?.Trim(), UriKind.Absolute, out var pasted) || string.IsNullOrEmpty(pasted.Query))
            return Problem("Paste the whole address from the page that did not load. It starts with http://127.0.0.1.");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(pasted.Query);
        var pending = oauth.Take(query.TryGetValue("state", out var st) ? st.ToString() : null);
        if (pending is null)
            return Problem("That sign-in had expired or was already used. Start it again.");
        // Only the one who started it may finish it, and only from the
        // address it was sent to.
        if (pending.UserId != current.RequireId() || !pasted.GetLeftPart(UriPartial.Path).StartsWith(pending.RedirectUri, StringComparison.OrdinalIgnoreCase))
            return Problem("That address is not from the sign-in you started. Start it again.");
        if (query.TryGetValue("error", out var err))
            return Problem(Refusal(err.ToString(), query.TryGetValue("error_description", out var d) ? d.ToString() : null));
        var (ok, why) = await StillAllowedAsync(pending, db, permissions, ct);
        if (!ok) return Problem(why!);
        try
        {
            await oauth.CompleteAsync(pending, query.TryGetValue("code", out var c) ? c.ToString() : "", ct);
        }
        catch (MailOAuthException ex)
        {
            return Problem(ex.Message);
        }
        return Results.Ok(AdminEndpoints.MailInfo(await settings.GetAsync(ct), config));
    }

    private static async Task<IResult> Disconnect(
        MailOAuthService oauth, ISiteSettingsService settings, CurrentUser current, IConfiguration config, CancellationToken ct)
    {
        await oauth.DisconnectAsync(current.RequireId(), ct);
        return Results.Ok(AdminEndpoints.MailInfo(await settings.GetAsync(ct), config));
    }

    /// <summary>The account that started the sign-in may still change the email server.</summary>
    private static async Task<(bool Ok, string? Why)> StillAllowedAsync(
        MailOAuthPending pending, AppDbContext db, IInstancePermissions permissions, CancellationToken ct)
    {
        var active = await db.Users.AnyAsync(u => u.Id == pending.UserId && u.Status == UserStatus.Active, ct);
        if (!active || !(await permissions.ForUserAsync(pending.UserId, ct)).Contains(InstancePermissions.SettingsEmail))
            return (false, "The account that started this sign-in may no longer change the email server.");
        return (true, null);
    }

    private static string Refusal(string error, string? description) => error switch
    {
        "access_denied" => "The sign-in was canceled, or permission was not given.",
        _ => $"The provider refused: {description?.Split('\n')[0].Trim() ?? error}",
    };

    /// <summary>Back to the email settings, which read the outcome from the address and show it.</summary>
    private static IResult Back(string outcome, string detail) =>
        Results.Redirect($"/admin/settings?mail={outcome}&detail={Uri.EscapeDataString(detail)}#email");
}
