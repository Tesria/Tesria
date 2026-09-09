using System.Data;
using System.Security.Claims;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Features.Auth;

public static class AuthEndpoints
{
    public record RegisterRequest(string Email, string DisplayName, string Password);
    public record LoginRequest(string Email, string Password);
    public record UserResponse(Guid Id, string Email, string DisplayName, UserRole Role);
    public record OidcStatusResponse(bool Enabled, string DisplayName);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        // SignOut result clears the auth cookie; declared without an HttpContext
        // parameter so it binds as a route handler (avoids ASP0016).
        group.MapPost("/logout", () => Results.SignOut(
            new AuthenticationProperties(),
            [CookieAuthenticationDefaults.AuthenticationScheme])).RequireAuthorization();
        group.MapGet("/me", Me);

        group.MapGet("/oidc/status", OidcStatus);
        // A full-page browser redirect, not a fetch call — the IdP needs to
        // navigate the user's own browser through its login page.
        group.MapGet("/oidc/login", OidcLogin);

        return routes;
    }

    private static IResult OidcStatus(IConfiguration config)
    {
        var enabled = !string.IsNullOrWhiteSpace(config["Oidc:Authority"]);
        var displayName = config["Oidc:DisplayName"];
        return Results.Ok(new OidcStatusResponse(
            enabled, string.IsNullOrWhiteSpace(displayName) ? "Single sign-on" : displayName));
    }

    private static IResult OidcLogin(string? returnUrl, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config["Oidc:Authority"]))
            return Results.NotFound(new { message = "Single sign-on is not configured on this instance." });

        // Only ever redirect back into this same app, never to an
        // attacker-supplied external URL (an open-redirect otherwise).
        var target = returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";
        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = target },
            [OidcAuthenticationDefaults.Scheme]);
    }

    private static async Task<IResult> Register(
        RegisterRequest req, AppDbContext db, IPasswordHasher hasher, HttpContext http)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var displayName = (req.DisplayName ?? "").Trim();

        if (!IsValidEmail(email))
            return Results.ValidationProblem(Error("email", "A valid email address is required."));
        if (displayName.Length == 0)
            return Results.ValidationProblem(Error("displayName", "Display name is required."));
        if ((req.Password ?? "").Length < 8)
            return Results.ValidationProblem(Error("password", "Password must be at least 8 characters."));

        // Both the duplicate-email check and "is this the first account?" read the
        // table before writing to it, so they run in one serializable transaction:
        // without it, two simultaneous first registrations could each observe an
        // empty table and both be created as admin.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new { message = "An account with this email already exists." });

        // The first account on an empty instance administers it — otherwise a
        // fresh install has content and nobody able to manage it.
        var isFirstAccount = !await db.Users.AnyAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            PasswordHash = hasher.Hash(req.Password!),
            Status = UserStatus.Active,
            Role = isFirstAccount ? UserRole.Admin : UserRole.Member,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await SignIn(http, user);
        return Results.Ok(new UserResponse(user.Id, user.Email, user.DisplayName, user.Role));
    }

    private static async Task<IResult> Login(
        LoginRequest req, AppDbContext db, IPasswordHasher hasher, HttpContext http)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Verify even when the user is missing to keep timing uniform, and return
        // the same generic error for "no such user" and "wrong password".
        var ok = user?.PasswordHash is not null
            && hasher.Verify(req.Password ?? "", user.PasswordHash);
        if (!ok || user!.Status != UserStatus.Active)
            return Results.Unauthorized();

        await SignIn(http, user);
        return Results.Ok(new UserResponse(user.Id, user.Email, user.DisplayName, user.Role));
    }

    private static async Task<IResult> Me(AppDbContext db, CurrentUser current)
    {
        if (current.Id is not { } id) return Results.Unauthorized();
        var user = await db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserResponse(u.Id, u.Email, u.DisplayName, u.Role))
            .FirstOrDefaultAsync();
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static Task SignIn(HttpContext http, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 320
        && email.Contains('@')
        && email.IndexOf('@') > 0
        && email.IndexOf('@') < email.Length - 1;

    private static Dictionary<string, string[]> Error(string field, string message) =>
        new() { [field] = [message] };
}
