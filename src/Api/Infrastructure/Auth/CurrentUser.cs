using System.Security.Claims;
using Tesria.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Reads the signed-in user's identity from the current request. Registered as
/// scoped and injected into endpoints that need to know who is acting.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, AppDbContext db)
{
    private UserRole? _role;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <summary>Whether this request came with an API token (or a render token) rather than a session.</summary>
    public bool ViaToken => Principal?.FindFirst(TokenScope.ClaimType) is not null;

    /// <summary>The signed-in user's id, or null if the request is anonymous.</summary>
    public Guid? Id
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <summary>The signed-in user's id, throwing if the request is anonymous.</summary>
    public Guid RequireId() =>
        Id ?? throw new InvalidOperationException("No authenticated user on the request.");

    /// <summary>
    /// Whether the caller administers the instance: Admin or Owner
    /// (dev-plan 10.1).
    ///
    /// Reads the row rather than trusting a claim: a role claim would be stale
    /// until the user's next sign-in, so a demotion would not take effect until
    /// then. Reading the row makes it effective on the very next request. One
    /// primary-key lookup, cached for the lifetime of this scoped instance, so
    /// repeated checks within a request cost nothing.
    /// </summary>
    public async Task<bool> IsAdminAsync() => await RoleAsync() >= UserRole.Admin;

    /// <summary>Whether the caller owns the instance (dev-plan 10.1).</summary>
    public async Task<bool> IsOwnerAsync() => await RoleAsync() == UserRole.Owner;

    /// <summary>
    /// The caller's role, or <see cref="UserRole.Member"/> when the request is
    /// anonymous or the account has gone.
    /// </summary>
    public async Task<UserRole> RoleAsync()
    {
        if (_role is { } cached) return cached;
        if (Id is not { } id) return (_role = UserRole.Member).Value;

        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync();
        return (_role = role ?? UserRole.Member).Value;
    }
}
