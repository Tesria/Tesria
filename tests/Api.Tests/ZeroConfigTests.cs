using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Tesria.Api.Infrastructure.Security;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Zero-config first start (dev-plan 25.1): the app's half. The init service's
/// half, generating and keeping the secrets, is checked by scripts/test-init.sh.
/// </summary>
public class ZeroConfigTests
{
    private record KeyDto(bool Generated, DateTimeOffset? SavedAt, string? SavedByName);
    private record OverviewDto(KeyDto Key);
    private record AlertDto(Guid Id, string Kind, int Severity, string Key, int Status);
    private record AuditEntry(Guid Id, string Action, string TargetType, Guid? TargetId, Guid? ActorId);

    private static string Secrets(params (string Name, string Value)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tesria-secrets-" + Guid.NewGuid().ToString("N"));
        foreach (var (name, value) in files)
        {
            Directory.CreateDirectory(Path.Combine(dir, name));
            File.WriteAllText(Path.Combine(dir, name, "value"), value);
        }
        return dir;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value)).Build();

    [Fact]
    public void Secret_files_fill_the_settings_the_app_already_reads()
    {
        var dir = Secrets(("app-db-password", "app-pw"), ("collab-secret", "c-secret\n"), ("pdf-secret", "p-secret"));
        var values = SecretFiles.Resolve(Config(("Database:Name", "wiki"), ("Database:AppUser", "wiki_app")), dir);

        Assert.Equal("c-secret", values["Collab:Secret"]);
        Assert.Equal("p-secret", values["Pdf:SharedSecret"]);
        var app = new NpgsqlConnectionStringBuilder(values["ConnectionStrings:App"]);
        Assert.Equal(("db", 5432, "wiki", "wiki_app", "app-pw"), (app.Host, app.Port, app.Database, app.Username, app.Password));
    }

    [Fact]
    public void A_configured_setting_wins_over_the_file()
    {
        var dir = Secrets(("app-db-password", "from-file"), ("collab-secret", "from-file"));
        var values = SecretFiles.Resolve(
            Config(("ConnectionStrings:App", "Host=elsewhere"), ("Collab:Secret", "from-env")), dir);

        Assert.False(values.ContainsKey("ConnectionStrings:App"));
        Assert.False(values.ContainsKey("Collab:Secret"));
    }

    [Fact]
    public void Only_a_service_told_its_owner_user_gets_the_owner_connection()
    {
        var dir = Secrets(("postgres-password", "owner-pw"), ("app-db-password", "app-pw"));

        Assert.False(SecretFiles.Resolve(Config(), dir).ContainsKey("ConnectionStrings:Default"));
        var owner = new NpgsqlConnectionStringBuilder(
            SecretFiles.Resolve(Config(("Database:OwnerUser", "tesria")), dir)["ConnectionStrings:Default"]);
        Assert.Equal(("tesria", "owner-pw"), (owner.Username, owner.Password));
    }

    [Fact]
    public void A_password_from_env_with_separators_survives_the_connection_string()
    {
        var dir = Secrets(("app-db-password", "semi;colon'quote=x"));
        var app = new NpgsqlConnectionStringBuilder(SecretFiles.Resolve(Config(), dir)["ConnectionStrings:App"]);
        Assert.Equal("semi;colon'quote=x", app.Password);
    }

    [Fact]
    public void No_secret_directory_changes_nothing()
    {
        Assert.Empty(SecretFiles.Resolve(Config(), Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid())));
    }

    private static string Status(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tesria-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files) File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }

    [Fact]
    public async Task A_key_set_in_env_is_not_asked_about()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?>
        {
            ["Install:StatusDirectory"] = Status(("backup-key-source", "provided\n")),
        });
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();

        var key = (await owner.GetFromJsonAsync<OverviewDto>("/api/admin/backups"))!.Key;
        Assert.False(key.Generated);
    }

    [Fact]
    public async Task A_generated_key_is_asked_about_until_someone_says_it_is_saved()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?>
        {
            ["Install:StatusDirectory"] = Status(("backup-key-source", "generated\n")),
        });
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();

        var before = (await owner.GetFromJsonAsync<OverviewDto>("/api/admin/backups"))!.Key;
        Assert.True(before.Generated);
        Assert.Null(before.SavedAt);

        var res = await owner.PostAsJsonAsync("/api/admin/backups/key-saved", new { });
        res.EnsureSuccessStatusCode();
        var saved = (await res.Content.ReadFromJsonAsync<KeyDto>())!;
        Assert.NotNull(saved.SavedAt);
        Assert.NotNull(saved.SavedByName);

        var after = (await owner.GetFromJsonAsync<OverviewDto>("/api/admin/backups"))!.Key;
        Assert.Equal(saved.SavedAt, after.SavedAt);

        // Saying it twice changes nothing, and the audit log has it once.
        (await owner.PostAsJsonAsync("/api/admin/backups/key-saved", new { })).EnsureSuccessStatusCode();
        var audit = await owner.GetFromJsonAsync<List<AuditEntry>>("/api/audit?targetType=settings");
        Assert.Single(audit!, a => a.Action == "backup.key_saved");
    }

    [Fact]
    public async Task Only_someone_who_manages_backups_may_say_the_key_is_saved()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?>
        {
            ["Install:StatusDirectory"] = Status(("backup-key-source", "generated\n")),
        });
        await factory.CreateClient().RegisterAndSignInAsync();
        var member = factory.CreateClient();
        await member.RegisterAndSignInAsync();

        var res = await member.PostAsJsonAsync("/api/admin/backups/key-saved", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_placeholder_secret_raises_one_critical_alert()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?>
        {
            ["Install:StatusDirectory"] = Status(("placeholders", "POSTGRES_PASSWORD\nBACKUP_ENCRYPTION_KEY\n")),
        });
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();

        var alerts = await owner.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        var alert = Assert.Single(alerts!, a => a.Kind == "config.placeholder_secrets");
        Assert.Equal(2, alert.Severity);
    }

    [Fact]
    public async Task No_placeholder_no_alert()
    {
        using var factory = new TestAppFactory(new Dictionary<string, string?>
        {
            ["Install:StatusDirectory"] = Status(),
        });
        var owner = factory.CreateClient();
        await owner.RegisterAndSignInAsync();

        var alerts = await owner.GetFromJsonAsync<List<AlertDto>>("/api/admin/security/alerts?status=all");
        Assert.DoesNotContain(alerts!, a => a.Kind == "config.placeholder_secrets");
    }
}
