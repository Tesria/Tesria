using Tesria.Api.Features.Trust;
using Tesria.Api.Infrastructure.Permissions;
using Tesria.Api.Infrastructure.Security;

namespace Tesria.Api.Features.Admin;

/// <summary>
/// The certificate card in Administration, Settings (the review's SEC-01): the
/// fingerprint of this server's own certificate authority, which people
/// compare before a device trusts it. Given only on a connection nobody could
/// have altered (<see cref="TrustedChannel"/>); anywhere else the card says
/// where to find it instead, since a page that could be anyone's must not be
/// where people learn to look.
/// </summary>
public static class CertificateEndpoints
{
    /// <param name="OwnCertificate">False when the server has a public certificate, and there is nothing to trust.</param>
    /// <param name="Known">Whether Caddy has answered: the fingerprint exists.</param>
    /// <param name="Channel"><c>server</c> or <c>tailnet</c> when this connection may be shown it, otherwise null.</param>
    public record CertificateStatus(bool OwnCertificate, bool Known, string? Channel, string? Sha256, string? Sha1, string? Subject);

    public static IEndpointRouteBuilder MapCertificateEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/admin/certificate", Get).WithTags("Admin").RequireAuthorization()
            .RequirePermission(InstancePermissions.SettingsInstance);
        return routes;
    }

    private static IResult Get(HttpContext context, IConfiguration config, LocalAuthority authority)
    {
        var own = TrustEndpoints.OwnCertificate(config);
        var current = own ? authority.Current : null;
        var channel = TrustedChannel.Of(context);
        var show = current is not null && channel is not null;
        return Results.Ok(new CertificateStatus(
            own, current is not null, channel,
            show ? current!.Sha256 : null, show ? current!.Sha1 : null, show ? current!.Subject : null));
    }
}
