using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// First-run setup (dev-plan 10.2). The wizard itself is SPA behavior; what
/// is worth pinning down here is that the server decides when an instance is
/// actually set up, and does not take the client's word for it.
/// </summary>
public class SetupTests
{
    private record UserDto(Guid Id, string Email, int Role, bool SetupRequired, int RecoveryCodesRemaining);
    private record StepDto(DateTimeOffset At, bool Skipped);
    private record StatusDto(bool Required, DateTimeOffset? CompletedAt, Dictionary<string, StepDto> Steps);
    private record InstanceDto(string InstanceName, bool NeedsOwner, bool PublicReading, bool AllowPublicRegistration);
    private const int Admin = 1, Owner = 2;

    private static async Task<UserDto> RegisterAsync(HttpClient c, string email) =>
        (await (await c.PostAsJsonAsync("/api/auth/register",
            new { Email = email, DisplayName = email, Password = "supersecret" }))
            .Content.ReadFromJsonAsync<UserDto>())!;

    private static async Task<T> InScopeAsync<T>(TestAppFactory f, Func<AppDbContext, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<HttpResponseMessage> RecordAsync(HttpClient c, string key, bool skipped = false) =>
        c.PostAsJsonAsync($"/api/setup/steps/{key}", new { Skipped = skipped });

    /// <summary>An owner on a fresh instance, mid-wizard.</summary>
    private static async Task<(TestAppFactory Factory, HttpClient Owner)> FreshAsync()
    {
        var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner@example.com");
        return (factory, owner);
    }

    /// <summary>Walks every gate except the one the caller wants to test.</summary>
    private static async Task SatisfyAllButAsync(TestAppFactory f, HttpClient owner, string? except)
    {
        if (except != "account")
            (await owner.PostAsJsonAsync("/api/auth/me/recovery-codes/acknowledge", new { }))
                .EnsureSuccessStatusCode();
        if (except != "instance")
        {
            (await owner.PutAsJsonAsync("/api/admin/settings", new { InstanceName = "Our wiki" }))
                .EnsureSuccessStatusCode();
            (await RecordAsync(owner, "instance")).EnsureSuccessStatusCode();
        }
        if (except != "registration")
            (await RecordAsync(owner, "registration")).EnsureSuccessStatusCode();
        if (except != "permissions")
            (await owner.PostAsJsonAsync("/api/admin/roles/review", new { })).EnsureSuccessStatusCode();
        if (except != "backups")
            (await owner.PutAsJsonAsync("/api/admin/backups/policy",
                new { RetentionEnabled = true, KeepCount = 3, KeepDays = 14 })).EnsureSuccessStatusCode();
    }

    // --- When the wizard runs.

    [Fact]
    public async Task Needs_owner_flips_on_the_first_account_and_that_account_owns_the_instance()
    {
        using var factory = new TestAppFactory();
        var anon = factory.CreateClient();
        Assert.True((await anon.GetFromJsonAsync<InstanceDto>("/api/instance"))!.NeedsOwner);

        var owner = factory.CreateClient();
        var registered = await RegisterAsync(owner, "first@example.com");

        Assert.Equal(Owner, registered.Role);
        Assert.False((await anon.GetFromJsonAsync<InstanceDto>("/api/instance"))!.NeedsOwner);
    }

    [Fact]
    public async Task The_owner_of_a_fresh_instance_is_told_setup_is_required()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;

        Assert.True((await owner.GetFromJsonAsync<UserDto>("/api/auth/me"))!.SetupRequired);
        Assert.True((await owner.GetFromJsonAsync<StatusDto>("/api/setup"))!.Required);
    }

    [Fact]
    public async Task An_instance_upgraded_from_before_the_wizard_is_already_finished()
    {
        // OwnerSeed stamps SetupCompletedAt when it promotes someone on an
        // instance that predates the wizard; such an owner must never be sent
        // through it.
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        // Through the settings service, as OwnerSeed does: writing the column
        // directly would leave the 30-second settings cache holding the old
        // answer, and /auth/me reads it.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<Infrastructure.Settings.ISiteSettingsService>()
                .UpdateAsync(s => s.SetupCompletedAt = DateTimeOffset.UtcNow.AddDays(-30), actorId: null);
        }

        Assert.False((await owner.GetFromJsonAsync<UserDto>("/api/auth/me"))!.SetupRequired);
    }

    // --- Who may.

    [Fact]
    public async Task Only_the_owner_may_record_steps_or_complete()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        var memberClient = factory.CreateClient();
        await RegisterAsync(memberClient, "member@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await RecordAsync(memberClient, "registration")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.PostAsJsonAsync("/api/setup/complete", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/setup")).StatusCode);

        // An administrator is not the owner either: this is the one account
        // that owns the instance (dev-plan 10.1).
        var adminClient = factory.CreateClient();
        var admin = await RegisterAsync(adminClient, "admin@example.com");
        (await owner.PutAsJsonAsync($"/api/admin/users/{admin.Id}/role", new { Role = Admin }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await RecordAsync(adminClient, "registration")).StatusCode);
    }

    // --- What the server will not accept.

    [Theory]
    [InlineData("account")]
    [InlineData("instance")]
    [InlineData("registration")]
    [InlineData("permissions")]
    [InlineData("backups")]
    public async Task A_required_step_cannot_be_recorded_as_skipped(string key)
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;

        var res = await RecordAsync(owner, key, skipped: true);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var status = await owner.GetFromJsonAsync<StatusDto>("/api/setup");
        Assert.DoesNotContain(key, status!.Steps.Keys);
    }

    [Fact]
    public async Task An_optional_step_may_be_skipped_and_is_remembered_as_such()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;

        (await RecordAsync(owner, "email", skipped: true)).EnsureSuccessStatusCode();

        var status = await owner.GetFromJsonAsync<StatusDto>("/api/setup");
        Assert.True(status!.Steps["email"].Skipped);
    }

    [Fact]
    public async Task An_unknown_step_is_refused()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        Assert.Equal(HttpStatusCode.BadRequest, (await RecordAsync(owner, "not-a-step")).StatusCode);
    }

    // --- Completion is checked against what happened, not what was clicked.

    [Theory]
    [InlineData("account")]
    [InlineData("registration")]
    [InlineData("permissions")]
    [InlineData("backups")]
    public async Task Complete_refuses_and_names_the_step_that_is_missing(string missing)
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        await SatisfyAllButAsync(factory, owner, missing);

        var res = await owner.PostAsJsonAsync("/api/setup/complete", new { });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(missing, body.GetProperty("step").GetString());
        Assert.Null(await InScopeAsync(factory,
            db => db.SiteSettings.Select(s => s.SetupCompletedAt).FirstAsync()));
    }

    [Fact]
    public async Task Recording_a_step_is_not_enough_on_its_own()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        // Every step clicked through, but nothing actually done behind them.
        foreach (var key in new[] { "account", "instance", "registration", "permissions", "backups" })
            (await RecordAsync(owner, key)).EnsureSuccessStatusCode();

        var res = await owner.PostAsJsonAsync("/api/setup/complete", new { });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Complete_succeeds_once_every_gate_is_satisfied_and_records_what_was_skipped()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        await SatisfyAllButAsync(factory, owner, except: null);
        (await RecordAsync(owner, "email", skipped: true)).EnsureSuccessStatusCode();
        (await RecordAsync(owner, "first-space", skipped: true)).EnsureSuccessStatusCode();

        var res = await owner.PostAsJsonAsync("/api/setup/complete", new { });
        res.EnsureSuccessStatusCode();

        Assert.NotNull(await InScopeAsync(factory,
            db => db.SiteSettings.Select(s => s.SetupCompletedAt).FirstAsync()));
        Assert.False((await owner.GetFromJsonAsync<UserDto>("/api/auth/me"))!.SetupRequired);

        var entry = await InScopeAsync(factory, db => db.AuditLogs
            .Where(a => a.Action == "setup.completed").OrderByDescending(a => a.Sequence).FirstAsync());
        using var metadata = JsonDocument.Parse(entry.MetadataJson!);
        var skipped = metadata.RootElement.GetProperty("Skipped").EnumerateArray()
            .Select(e => e.GetString() ?? "").ToArray();
        Assert.Equal(["email", "first-space"], skipped);
    }

    [Fact]
    public async Task Completing_twice_is_harmless()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        await SatisfyAllButAsync(factory, owner, except: null);
        (await owner.PostAsJsonAsync("/api/setup/complete", new { })).EnsureSuccessStatusCode();

        var again = await owner.PostAsJsonAsync("/api/setup/complete", new { });

        again.EnsureSuccessStatusCode();
        Assert.False((await again.Content.ReadFromJsonAsync<StatusDto>())!.Required);
    }

    // --- The account step's evidence.

    [Fact]
    public async Task Acknowledging_recovery_codes_is_recorded_once_and_is_idempotent()
    {
        var (factory, owner) = await FreshAsync();
        using var _ = factory;

        (await owner.PostAsJsonAsync("/api/auth/me/recovery-codes/acknowledge", new { }))
            .EnsureSuccessStatusCode();
        var first = await InScopeAsync(factory, db => db.Users
            .Where(u => u.Email == "owner@example.com").Select(u => u.RecoveryCodesAcknowledgedAt).FirstAsync());
        Assert.NotNull(first);

        (await owner.PostAsJsonAsync("/api/auth/me/recovery-codes/acknowledge", new { }))
            .EnsureSuccessStatusCode();
        var second = await InScopeAsync(factory, db => db.Users
            .Where(u => u.Email == "owner@example.com").Select(u => u.RecoveryCodesAcknowledgedAt).FirstAsync());
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task An_account_with_no_password_is_not_asked_to_save_codes_it_cannot_have()
    {
        // The OIDC shape (dev-plan 10.2): recovery codes reset a password
        // this account does not have, so the requirement is waived.
        var (factory, owner) = await FreshAsync();
        using var _ = factory;
        await InScopeAsync(factory, async db =>
        {
            var u = await db.Users.FirstAsync(x => x.Email == "owner@example.com");
            u.PasswordHash = null;
            return await db.SaveChangesAsync();
        });
        await SatisfyAllButAsync(factory, owner, except: "account");

        (await owner.PostAsJsonAsync("/api/setup/complete", new { })).EnsureSuccessStatusCode();

        Assert.NotNull(await InScopeAsync(factory,
            db => db.SiteSettings.Select(s => s.SetupCompletedAt).FirstAsync()));
    }
}
