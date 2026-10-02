using System.Net;
using System.Text;
using Tesria.Api.Features.Export;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The hourly pack-import allowance (t6-016): refused imports do not use it
/// up, and the refusal says the real wait rather than "a minute".
/// </summary>
public class ImportAllowanceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Refused_attempts_do_not_count_towards_the_ten_imports()
    {
        var clock = new Clock();
        var allowance = new ImportAllowance(clock);
        var me = Guid.NewGuid();

        // Nine refused attempts and one import, as in the report.
        for (var i = 0; i < 9; i++) Assert.Null(allowance.TryBegin(me));
        Assert.Null(allowance.TryBegin(me));
        allowance.Imported(me);

        Assert.Null(allowance.TryBegin(me));
    }

    [Fact]
    public void The_eleventh_import_says_how_long_to_wait()
    {
        var clock = new Clock();
        var allowance = new ImportAllowance(clock);
        var me = Guid.NewGuid();

        for (var i = 0; i < ImportAllowance.ImportsPerHour; i++)
        {
            Assert.Null(allowance.TryBegin(me));
            allowance.Imported(me);
            clock.Now += TimeSpan.FromMinutes(2);
        }

        // The first went in 20 minutes ago, so the next can go in 40 minutes from now.
        var refused = allowance.TryBegin(me);
        Assert.NotNull(refused);
        Assert.Equal(TimeSpan.FromMinutes(40), refused.Value.Wait);
        Assert.Contains("in 40 minutes", refused.Value.Message);

        clock.Now += TimeSpan.FromMinutes(40);
        Assert.Null(allowance.TryBegin(me));
    }

    [Fact]
    public void Attempts_are_still_bounded_more_loosely()
    {
        var clock = new Clock();
        var allowance = new ImportAllowance(clock);
        var me = Guid.NewGuid();

        for (var i = 0; i < ImportAllowance.AttemptsPerHour; i++) Assert.Null(allowance.TryBegin(me));

        var refused = allowance.TryBegin(me);
        Assert.NotNull(refused);
        Assert.Contains("in 60 minutes", refused.Value.Message);
        // Somebody else is not affected.
        Assert.Null(allowance.TryBegin(Guid.NewGuid()));
    }

    [Fact]
    public async Task Ten_refused_imports_leave_the_next_one_free()
    {
        await using var app = new TestAppFactory();
        var client = app.CreateClient();
        await client.RegisterAndSignInAsync();

        for (var i = 0; i < 10; i++)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not a pack")), "file", "pack.zip");
            form.Add(new StringContent($"BAD{i}"), "key");
            var res = await client.PostAsync("/api/spaces/import", form);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        using var last = new MultipartFormDataContent();
        last.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not a pack")), "file", "pack.zip");
        last.Add(new StringContent("BADX"), "key");
        // Still refused for what it is, not for the count.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/spaces/import", last)).StatusCode);
    }
}
