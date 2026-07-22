using System.Net.Http.Json;

namespace ConfluenceClone.Api.Tests;

internal static class TestHelpers
{
    private static int _seq;

    /// <summary>
    /// Registers a fresh user on the client (which stores the auth cookie) so
    /// follow-up requests are authenticated. Returns the created user's id.
    /// </summary>
    public static async Task<Guid> RegisterAndSignInAsync(this HttpClient client)
    {
        var n = Interlocked.Increment(ref _seq);
        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = $"user{n}@example.com",
            DisplayName = $"User {n}",
            Password = "supersecret",
        });
        res.EnsureSuccessStatusCode();
        var user = await res.Content.ReadFromJsonAsync<UserDto>();
        return user!.Id;
    }

    public record UserDto(Guid Id, string Email, string DisplayName);

    /// <summary>Creates a space with a unique key and returns its id.</summary>
    public static async Task<Guid> CreateSpaceAsync(this HttpClient client, string? key = null)
    {
        var n = Interlocked.Increment(ref _seq);
        key ??= $"S{n:D4}";
        var res = await client.PostAsJsonAsync("/api/spaces",
            new { Key = key, Name = $"Space {n}", Description = (string?)null });
        res.EnsureSuccessStatusCode();
        var space = await res.Content.ReadFromJsonAsync<SpaceDto>();
        return space!.Id;
    }

    public record SpaceDto(Guid Id, string Key, string Name);
}
