using System.Security.Claims;
using System.Text.Encodings.Web;
using Tesria.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Tesria.Api.Infrastructure.Auth;

public static class ApiTokenAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiToken";
}

/// <summary>
/// Authenticates requests bearing <c>Authorization: Bearer &lt;token&gt;</c>
/// against <see cref="IApiTokenService"/>. On success it populates the same
/// claim types cookie auth does (<see cref="CurrentUser"/> and the permission
/// service read those, not the auth scheme), so every existing endpoint works
/// unchanged for API-token callers.
/// </summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiTokenService tokens,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header)) return AuthenticateResult.NoResult();
        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var rawToken = value["Bearer ".Length..].Trim();
        var token = await tokens.ValidateAsync(rawToken);
        if (token is null) return AuthenticateResult.Fail("Invalid or expired API token.");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == token.UserId);
        if (user is null) return AuthenticateResult.Fail("Invalid or expired API token.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
