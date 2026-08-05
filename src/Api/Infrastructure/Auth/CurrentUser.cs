using System.Security.Claims;

namespace Tesria.Api.Infrastructure.Auth;

/// <summary>
/// Reads the signed-in user's identity from the current request. Registered as
/// scoped and injected into endpoints that need to know who is acting.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

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
}
