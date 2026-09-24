using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Branding;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Public;

/// <summary>
/// What the SPA may know before anyone has signed in (dev-plan 5.5).
///
/// This is the only endpoint that answers an anonymous caller with anything
/// about the instance, so it says as little as it can get away with: the
/// name, whether the instance still needs its owner, whether there is any
/// public reading to offer, and whether the sign-up link should exist. None
/// of it is a secret from someone who can reach the address, and each field
/// exists because the SPA cannot render the first screen correctly without it.
/// </summary>
public static class InstanceEndpoints
{
    public record InstanceResponse(
        string InstanceName, bool NeedsOwner, bool PublicReading, bool AllowPublicRegistration,
        BrandingResponse Branding,
        /// <summary>
        /// The server makes its own certificate, so browsers warn until each
        /// device trusts it; the sign-in page then links to /trust (15.5).
        /// </summary>
        bool OwnCertificate = false,
        /// <summary>Which Tesria this is (dev-plan 16.1), as /api/health says too.</summary>
        string? Version = null);

    /// <summary>
    /// The branding the SPA draws (dev-plan 13.1). Anonymous because the
    /// sign-in page needs it, and harmless for the same reason: everything in
    /// it is on every page anyone can see.
    /// </summary>
    public record BrandingResponse(
        string Name, bool HasCustomName, bool HasIdentity, string Display, string SignInArrangement,
        BrandLogo? Logo, BrandLogo? LogoDark, bool HasFavicon,
        string ThemePolicy, string AccentPolicy, string? AccentName, string? AccentLight, string? AccentDark);

    public static BrandingResponse BrandingOf(BrandView b) => new(
        b.Name, b.HasCustomName, b.HasIdentity, b.Display, b.SignInArrangement,
        b.Logo, b.LogoDark, b.FaviconHash is not null,
        b.ThemePolicy, b.AccentPolicy, b.AccentName, b.AccentLight, b.AccentDark);

    public static IEndpointRouteBuilder MapInstanceEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/instance", Get).AllowAnonymous().WithTags("Instance");
        return routes;
    }

    private static async Task<IResult> Get(AppDbContext db, ISiteSettingsService settings, IConfiguration config, HttpContext http)
    {
        var s = await settings.GetAsync();

        // Anonymous reading is opt-in twice, and this is the whole rule: the
        // instance-wide switch, and a space that someone actually published.
        // An instance that publishes nothing should look like one, rather
        // than offering an empty public shell.
        var publicReading = s.AllowPublicSpaces
            && await db.Spaces.AsNoTracking().AnyAsync(x => x.IsPublic && !x.Archived);

        return Results.Ok(new InstanceResponse(
            InstanceName: s.InstanceName,
            // The first account becomes the owner (dev-plan 10.1), so "no
            // accounts" is what the setup wizard will key off.
            NeedsOwner: !await db.Users.AsNoTracking().AnyAsync(),
            PublicReading: publicReading,
            AllowPublicRegistration: s.AllowPublicRegistration,
            Branding: BrandingOf(BrandView.From(s)),
            OwnCertificate: Trust.TrustEndpoints.OwnCertificate(config),
            // Signed-in callers only (dev-plan 14.3), as /api/health.
            Version: http.User.Identity?.IsAuthenticated == true ? Infrastructure.Versioning.AppVersion.Current : null));
    }
}
