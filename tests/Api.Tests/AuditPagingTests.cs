using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The Audit tab's filters and Show Older (T7-019): <c>before</c>, <c>action</c>
/// (exact or a prefix), <c>actorId</c>, <c>from</c> and <c>to</c>, and the
/// header that says where the next page starts. What a caller may see is the
/// same as without them.
/// </summary>
public class AuditPagingTests
{
    private const string Doc = """{"type":"doc","content":[]}""";
    private const string NextBefore = "X-Audit-Next-Before";
    private const int UserPrincipal = 0, View = 0, Admin = 1;

    private record AuditEntry(
        Guid Id, string Action, string TargetType, Guid? TargetId,
        Guid? ActorId, string? ActorName, string? MetadataJson, DateTimeOffset CreatedAt, long Sequence);
    private record PageDto(Guid Id, Guid SpaceId, string Title);
    private record UserDto(Guid Id, string Email, string DisplayName);

    private static async Task<PageDto> NewPageAsync(HttpClient c, Guid spaceId, string title) =>
        (await (await c.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
            .Content.ReadFromJsonAsync<PageDto>())!;

    private static async Task<(List<AuditEntry> Entries, long? Next)> GetAsync(HttpClient c, string query)
    {
        var res = await c.GetAsync($"/api/audit?{query}");
        res.EnsureSuccessStatusCode();
        var entries = (await res.Content.ReadFromJsonAsync<List<AuditEntry>>())!;
        long? next = res.Headers.TryGetValues(NextBefore, out var v) ? long.Parse(v.Single()) : null;
        return (entries, next);
    }

    /// <summary>Follows the next-page header to the start of the log.</summary>
    private static async Task<List<AuditEntry>> AllPagesAsync(HttpClient c, string query)
    {
        var all = new List<AuditEntry>();
        long? before = null;
        for (var guard = 0; guard < 100; guard++)
        {
            var (entries, next) = await GetAsync(c, before is { } b ? $"{query}&before={b}" : query);
            all.AddRange(entries);
            if (next is null) return all;
            Assert.True(before is null || next < before, "the cursor always moves back");
            before = next;
        }
        throw new InvalidOperationException("Paging did not end.");
    }

    [Fact]
    public async Task Paging_with_before_has_no_gaps_or_repeats()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        for (var i = 0; i < 23; i++) await NewPageAsync(client, spaceId, $"Paged {i}");

        var (everything, rest) = await GetAsync(client, "take=200");
        Assert.Null(rest); // one page held it all
        var paged = await AllPagesAsync(client, "take=5");

        Assert.Equal(everything.Select(e => e.Sequence), paged.Select(e => e.Sequence));
        Assert.Equal(paged.Count, paged.Select(e => e.Id).Distinct().Count());
        Assert.True(paged.Zip(paged.Skip(1)).All(p => p.First.Sequence > p.Second.Sequence), "newest first");
        Assert.True(paged.Count(e => e.Action == "page.created") >= 23);

        // A page that is exactly full says there may be more; the one after it is empty and says no more.
        var (first, next) = await GetAsync(client, "take=5");
        Assert.Equal(5, first.Count);
        Assert.Equal(first[^1].Sequence, next);
        var (none, after) = await GetAsync(client, "before=1");
        Assert.Empty(none);
        Assert.Null(after);
    }

    [Fact]
    public async Task The_action_filter_takes_one_action_or_a_prefix()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        var page = await NewPageAsync(client, spaceId, "Filtered");
        await client.PutAsJsonAsync($"/api/pages/{page.Id}", new { Title = "Filtered v2", ContentJson = Doc, ChangeComment = (string?)null });
        await client.DeleteAsync($"/api/pages/{page.Id}");

        var created = await AllPagesAsync(client, "action=page.created&take=2");
        Assert.NotEmpty(created);
        Assert.All(created, e => Assert.Equal("page.created", e.Action));

        var pages = await AllPagesAsync(client, "action=page.&take=2");
        Assert.All(pages, e => Assert.StartsWith("page.", e.Action));
        Assert.Contains(pages, e => e.Action == "page.updated");
        Assert.Contains(pages, e => e.Action == "page.trashed");

        var users = await AllPagesAsync(client, "action=user.");
        Assert.Contains(users, e => e.Action == "user.registered");
        Assert.All(users, e => Assert.StartsWith("user.", e.Action));

        // "page" without the dot is a name no entry has, not a prefix.
        Assert.Empty((await GetAsync(client, "action=page")).Entries);
        // An underscore or percent sign is matched as itself.
        Assert.Empty((await GetAsync(client, "action=pa%25.")).Entries);
        Assert.Empty((await GetAsync(client, "action=p_ge.")).Entries);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/audit?action={new string('a', 101)}")).StatusCode);
    }

    [Fact]
    public async Task The_person_filter_keeps_only_that_persons_entries()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        var spaceId = await owner.CreateSpaceAsync();
        var member = factory.CreateClient();
        var memberId = await member.RegisterAndSignInAsync();
        await NewPageAsync(owner, spaceId, "Owner's");
        await NewPageAsync(member, spaceId, "Member's");
        await NewPageAsync(member, spaceId, "Member's too");

        var theirs = await AllPagesAsync(owner, $"actorId={memberId}&take=1");
        Assert.NotEmpty(theirs);
        Assert.All(theirs, e => Assert.Equal(memberId, e.ActorId));
        Assert.Equal(2, theirs.Count(e => e.Action == "page.created"));

        var mine = await AllPagesAsync(owner, $"actorId={ownerId}&action=page.created");
        Assert.Single(mine);
    }

    [Fact]
    public async Task From_and_to_bound_the_entries_by_time()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        for (var i = 0; i < 4; i++) await NewPageAsync(client, spaceId, $"Early {i}");
        await Task.Delay(50);
        var split = DateTimeOffset.UtcNow;
        await Task.Delay(50);
        for (var i = 0; i < 3; i++) await NewPageAsync(client, spaceId, $"Late {i}");

        static string At(DateTimeOffset t) => Uri.EscapeDataString(t.ToString("O"));
        var late = await AllPagesAsync(client, $"action=page.created&from={At(split)}&take=2");
        Assert.Equal(3, late.Count);
        Assert.All(late, e => Assert.True(e.CreatedAt >= split));

        var early = await AllPagesAsync(client, $"action=page.created&to={At(split)}&take=2");
        Assert.Equal(4, early.Count);
        Assert.All(early, e => Assert.True(e.CreatedAt < split));

        var all = await AllPagesAsync(client, $"action=page.created&from={At(split.AddHours(-1))}&to={At(split.AddHours(1))}");
        Assert.Equal(7, all.Count);
        Assert.Empty(await AllPagesAsync(client, $"from={At(split.AddDays(1))}"));
        // Local times with an offset mean the same instant.
        var offset = split.ToOffset(TimeSpan.FromHours(-7));
        Assert.Equal(3, (await AllPagesAsync(client, $"action=page.created&from={At(offset)}")).Count);
    }

    [Fact]
    public async Task Paging_and_filters_still_hide_what_the_caller_may_not_see()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        var ownerId = await owner.RegisterAndSignInAsync();
        var admin = f.CreateClient();
        var adminId = await admin.RegisterAndSignInAsync();
        (await owner.PutAsJsonAsync($"/api/admin/users/{adminId}/role", new { Role = Admin })).EnsureSuccessStatusCode();

        // Seen and unseen entries, interleaved, so every page mixes both.
        var seen = await admin.CreateSpaceAsync("SEEN");
        var hidden = await owner.CreateSpaceAsync("UNSEEN");
        await owner.MakePrivateAsync("UNSEEN");
        var seenPages = new List<Guid>();
        var hiddenPages = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            seenPages.Add((await NewPageAsync(admin, seen, $"Seen {i}")).Id);
            hiddenPages.Add((await NewPageAsync(owner, hidden, $"Unseen {i}")).Id);
            hiddenPages.Add((await NewPageAsync(owner, hidden, $"Unseen {i}b")).Id);
        }

        foreach (var query in new[] { "take=2", "action=page.&take=3", "action=page.created&take=1", $"actorId={ownerId}&take=2" })
        {
            var entries = await AllPagesAsync(admin, query);
            Assert.DoesNotContain(entries, e => e.TargetId is { } t && (hiddenPages.Contains(t) || t == hidden));
            Assert.DoesNotContain(entries, e => e.MetadataJson?.Contains("Unseen") == true);
            if (!query.StartsWith("actorId"))
                Assert.All(seenPages, p => Assert.Contains(entries, e => e.TargetId == p && e.Action == "page.created"));
        }

        // The owner, who may see both, gets every one of them through the same pages.
        var ownerView = await AllPagesAsync(owner, "action=page.created&take=4");
        Assert.All(seenPages.Concat(hiddenPages), p => Assert.Contains(ownerView, e => e.TargetId == p));
    }

    [Fact]
    public async Task A_member_is_still_refused()
    {
        using var f = new TestAppFactory();
        var owner = f.CreateClient();
        await owner.RegisterAndSignInAsync();
        var member = f.CreateClient();
        await member.RegisterAndSignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/audit?before=100&action=user.")).StatusCode);
    }
}

/// <summary>
/// The same filters on PostgreSQL, where the time range and the action prefix
/// are applied in SQL rather than to each row as it is read (SQLite cannot
/// compare a DateTimeOffset).
/// </summary>
[Collection("Postgres")]
public class AuditPagingPostgresTests
{
    private const string Doc = """{"type":"doc","content":[]}""";

    private record AuditEntry(Guid Id, string Action, Guid? ActorId, DateTimeOffset CreatedAt, long Sequence);

    [PostgresFact]
    public async Task Filters_and_paging_work_in_sql()
    {
        using var pg = new PostgresTestDatabase();
        using var factory = new TestAppFactory(pg);
        var client = factory.CreateClient();
        var me = await client.RegisterAndSignInAsync();
        var spaceId = await client.CreateSpaceAsync();
        async Task Page(string title) =>
            (await client.PostAsJsonAsync("/api/pages", new { SpaceId = spaceId, ParentPageId = (Guid?)null, Title = title, ContentJson = Doc }))
                .EnsureSuccessStatusCode();
        for (var i = 0; i < 4; i++) await Page($"Early {i}");
        await Task.Delay(50);
        var split = DateTimeOffset.UtcNow;
        await Task.Delay(50);
        for (var i = 0; i < 3; i++) await Page($"Late {i}");

        static string At(DateTimeOffset t) => Uri.EscapeDataString(t.ToString("O"));
        async Task<List<AuditEntry>> All(string query)
        {
            var all = new List<AuditEntry>();
            string? before = null;
            while (true)
            {
                var res = await client.GetAsync($"/api/audit?{query}{(before is null ? "" : $"&before={before}")}");
                res.EnsureSuccessStatusCode();
                all.AddRange((await res.Content.ReadFromJsonAsync<List<AuditEntry>>())!);
                if (!res.Headers.TryGetValues("X-Audit-Next-Before", out var v)) return all;
                before = v.Single();
            }
        }

        Assert.Equal(3, (await All($"action=page.created&from={At(split)}&take=2")).Count);
        Assert.Equal(4, (await All($"action=page.created&to={At(split)}&take=2")).Count);
        var pages = await All($"action=page.&actorId={me}&take=3");
        Assert.Equal(7, pages.Count);
        Assert.Equal(pages.Count, pages.Select(e => e.Sequence).Distinct().Count());
        Assert.Contains(await All("action=user."), e => e.Action == "user.registered");
        Assert.Empty(await All("action=p_ge."));
    }
}
