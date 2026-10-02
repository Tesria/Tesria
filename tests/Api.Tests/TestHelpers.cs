using System.Net.Http.Json;

namespace Tesria.Api.Tests;

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

    /// <summary>
    /// Closes a space to everyone signed in (dev-plan 21.1), leaving only its
    /// groups and grants: its creator is in its Admins group. Before 21.1 the
    /// first grant did this, which is what most tests that lock a space down
    /// were relying on.
    /// </summary>
    public static async Task MakePrivateAsync(this HttpClient client, string key)
    {
        var res = await client.PutAsJsonAsync($"/api/spaces/{key}/permissions/everyone", new { Access = (int?)null });
        res.EnsureSuccessStatusCode();
    }

    /// <summary>The id of one of a space's four groups (21.1): 0 Viewers, 1 Editors, 2 Admins, 3 Reviewers.</summary>
    public static async Task<Guid> SpaceGroupAsync(this HttpClient client, string key, int role)
    {
        var access = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/spaces/{key}/permissions");
        return access.GetProperty("groups").EnumerateArray()
            .Single(g => g.GetProperty("role").GetInt32() == role).GetProperty("id").GetGuid();
    }
}
