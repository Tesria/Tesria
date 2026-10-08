using System.Net;
using System.Net.Http.Json;
using Tesria.Api.Infrastructure;
using Npgsql;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Registrations that arrive at the same moment (T1-023, t2-026). The
/// serializable transaction already made sure only one of them won; the
/// others answered a bare 500. Only real PostgreSQL raises the
/// serialization failure, so the races need it.
/// </summary>
public class RegistrationRaceTests
{
    private record Answer(string? Message, string? Detail, string? Code);

    private static async Task<HttpResponseMessage[]> AtOnceAsync(TestAppFactory factory, Func<int, object> body, int count = 4)
    {
        var clients = Enumerable.Range(0, count).Select(_ => factory.CreateClient()).ToArray();
        using var go = new ManualResetEventSlim();
        var sends = clients.Select((c, i) => Task.Run(async () =>
        {
            go.Wait();
            return await c.PostAsJsonAsync("/api/auth/register", body(i));
        })).ToArray();
        go.Set();
        return await Task.WhenAll(sends);
    }

    /// <summary>Every answer's status and body, for a failure message that says what happened.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage[] answers)
    {
        var lines = new List<string>();
        foreach (var a in answers)
        {
            var body = await a.Content.ReadAsStringAsync();
            lines.Add($"{(int)a.StatusCode} {(body.Length > 300 ? body[..300] : body)}");
        }
        return string.Join("\n", lines);
    }

    [PostgresFact]
    public async Task Two_owners_at_once_one_is_made_and_the_others_are_told_why()
    {
        using var pg = new PostgresTestDatabase();
        await using var factory = new TestAppFactory(pg) { OpenRegistration = false };

        var answers = await AtOnceAsync(factory, i => new
        {
            Email = $"owner{i}@example.com", DisplayName = $"Owner {i}", Password = "supersecret",
        });

        var seen = await DescribeAsync(answers);
        Assert.True(answers.Count(a => a.StatusCode == HttpStatusCode.OK) == 1, seen);
        foreach (var lost in answers.Where(a => a.StatusCode != HttpStatusCode.OK))
        {
            Assert.True(lost.StatusCode != HttpStatusCode.InternalServerError, seen);
            var body = await lost.Content.ReadFromJsonAsync<Answer>();
            Assert.False(string.IsNullOrWhiteSpace(body!.Message ?? body.Detail));
        }
    }

    [PostgresFact]
    public async Task The_same_address_twice_at_once_is_one_account_and_a_409()
    {
        using var pg = new PostgresTestDatabase();
        await using var factory = new TestAppFactory(pg);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "first@example.com", DisplayName = "First", Password = "supersecret" })).EnsureSuccessStatusCode();

        var answers = await AtOnceAsync(factory, _ => new
        {
            Email = "same@example.com", DisplayName = "Same", Password = "supersecret",
        });

        var seen = await DescribeAsync(answers);
        Assert.True(answers.Count(a => a.StatusCode == HttpStatusCode.OK) == 1, seen);
        foreach (var lost in answers.Where(a => a.StatusCode != HttpStatusCode.OK))
        {
            Assert.True(lost.StatusCode == HttpStatusCode.Conflict, seen);
            Assert.Contains("already exists", (await lost.Content.ReadFromJsonAsync<Answer>())!.Message);
        }
    }

    [Fact]
    public void A_collision_is_recognized_however_deeply_it_is_wrapped()
    {
        var lost = new PostgresException("could not serialize access", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        var unique = new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        var other = new PostgresException("value too long", "ERROR", "ERROR", PostgresErrorCodes.StringDataRightTruncation);

        Assert.True(DbConflicts.IsConflict(new InvalidOperationException("transient", lost)));
        Assert.True(DbConflicts.IsConflict(new Microsoft.EntityFrameworkCore.DbUpdateException("save", unique)));
        Assert.False(DbConflicts.IsConflict(new Microsoft.EntityFrameworkCore.DbUpdateException("save", other)));
        Assert.False(DbConflicts.IsConflict(new InvalidOperationException("nothing to do with it")));
    }
}
