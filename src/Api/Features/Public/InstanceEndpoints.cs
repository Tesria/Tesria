using Tesria.Api.Infrastructure;
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
        string InstanceName, bool NeedsOwner, bool PublicReading, bool AllowPublicRegistration);

    public static IEndpointRouteBuilder MapInstanceEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/instance", Get).AllowAnonymous().WithTags("Instance");
        return routes;
    }

    private static async Task<IResult> Get(AppDbContext db, ISiteSettingsService settings)
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
            AllowPublicRegistration: s.AllowPublicRegistration));
    }
}
