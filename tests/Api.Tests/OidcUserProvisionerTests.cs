using Tesria.Api.Domain;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Tesria.Api.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// Exercises the OIDC account-resolution rules directly against the DB
/// (bypassing the ASP.NET Core OIDC redirect/callback plumbing, which needs a
/// real identity provider): this is where an actual security bug would live.
/// </summary>
public class OidcUserProvisionerTests
{
    private static (TestAppFactory factory, AppDbContext db, OidcUserProvisioner provisioner) NewProvisioner()
    {
        var factory = new TestAppFactory();
        var services = factory.Services.CreateScope().ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        return (factory, db, new OidcUserProvisioner(db, services.GetRequiredService<ISiteSettingsService>()));
    }

    [Fact]
    public async Task First_login_provisions_a_new_passwordless_account()
    {
        var (factory, db, provisioner) = NewProvisioner();
        using var _ = factory;

        var user = await provisioner.ResolveOrProvisionAsync(
            "sub-1", "new.hire@example.com", emailVerified: true, "New Hire");

        Assert.Equal("new.hire@example.com", user.Email);
        Assert.Equal("New Hire", user.DisplayName);
        Assert.Equal("sub-1", user.OidcSubject);
        Assert.Null(user.PasswordHash);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Returning_user_with_the_same_subject_resolves_to_the_same_account()
    {
        var (factory, db, provisioner) = NewProvisioner();
        using var _ = factory;

        var first = await provisioner.ResolveOrProvisionAsync("sub-2", "returning@example.com", true, "First Name");
        var second = await provisioner.ResolveOrProvisionAsync("sub-2", "returning@example.com", true, "First Name");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Users.CountAsync(u => u.OidcSubject == "sub-2"));
    }

    [Fact]
    public async Task Verified_email_match_links_to_the_existing_local_account()
    {
        var (factory, db, provisioner) = NewProvisioner();
        using var _ = factory;

        var local = new User
        {
            Id = Guid.NewGuid(),
            Email = "alice@example.com",
            DisplayName = "Alice",
            PasswordHash = "argon2-hash-not-relevant-here",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(local);
        await db.SaveChangesAsync();

        var linked = await provisioner.ResolveOrProvisionAsync("sub-alice", "Alice@Example.com", true, "Alice Somebody");

        Assert.Equal(local.Id, linked.Id); // same account, not a new one
        Assert.Equal("sub-alice", linked.OidcSubject);
        Assert.NotNull(linked.PasswordHash); // the local password still works too
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "alice@example.com"));
    }

    [Fact]
    public async Task Unverified_email_match_is_refused_and_leaves_the_account_untouched()
    {
        var (factory, db, provisioner) = NewProvisioner();
        using var _ = factory;

        var local = new User
        {
            Id = Guid.NewGuid(),
            Email = "bob@example.com",
            DisplayName = "Bob",
            PasswordHash = "argon2-hash-not-relevant-here",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(local);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<OidcEmailNotVerifiedException>(() =>
            provisioner.ResolveOrProvisionAsync("attacker-sub", "bob@example.com", emailVerified: false, "Not Bob"));

        var reloaded = await db.Users.FirstAsync(u => u.Email == "bob@example.com");
        Assert.Null(reloaded.OidcSubject); // not linked
        Assert.Equal(local.Id, reloaded.Id); // still the original account, untouched
    }

    [Fact]
    public async Task Missing_email_is_rejected()
    {
        var (factory, _, provisioner) = NewProvisioner();
        using var _dispose = factory;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.ResolveOrProvisionAsync("sub-no-email", null, true, "No Email"));
    }

    [Fact]
    public async Task Missing_display_name_falls_back_to_the_email()
    {
        var (factory, _, provisioner) = NewProvisioner();
        using var _dispose = factory;

        var user = await provisioner.ResolveOrProvisionAsync("sub-no-name", "noname@example.com", true, null);
        Assert.Equal("noname@example.com", user.DisplayName);
    }
    [Fact]
    public async Task Invite_only_refuses_a_new_account_through_sso()
    {
        var (factory, db, provisioner) = NewProvisioner();
        using var _ = factory;
        var settings = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISiteSettingsService>();

        await provisioner.ResolveOrProvisionAsync("sub-owner", "owner@example.com", true, "Owner");
        await settings.UpdateAsync(s => s.AllowPublicRegistration = false, actorId: null);

        await Assert.ThrowsAsync<OidcRegistrationClosedException>(() =>
            provisioner.ResolveOrProvisionAsync("sub-stranger", "stranger@example.com", true, "Stranger"));
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Invite_only_still_signs_in_and_links_existing_accounts()
    {
        var (factory, db, provisioner) = NewProvisioner();
        using var _ = factory;
        var settings = factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISiteSettingsService>();

        var owner = await provisioner.ResolveOrProvisionAsync("sub-owner", "owner@example.com", true, "Owner");
        var local = await provisioner.ResolveOrProvisionAsync("sub-temp", "member@example.com", true, "Member");
        local.OidcSubject = null;
        await db.SaveChangesAsync();
        await settings.UpdateAsync(s => s.AllowPublicRegistration = false, actorId: null);

        Assert.Equal(owner.Id, (await provisioner.ResolveOrProvisionAsync("sub-owner", "owner@example.com", true, "Owner")).Id);
        var linked = await provisioner.ResolveOrProvisionAsync("sub-member", "member@example.com", true, "Member");
        Assert.Equal(local.Id, linked.Id);
        Assert.Equal("sub-member", linked.OidcSubject);
    }
    [Theory]
    [InlineData("/spaces", true)]
    [InlineData("/", true)]
    [InlineData("/spaces/DEMO/pages/1?x=1#top", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("spaces", false)]
    [InlineData("/a\nb", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Sso_return_address_is_a_path_on_this_site(string? url, bool allowed)
    {
        Assert.Equal(allowed, Tesria.Api.Features.Auth.AuthEndpoints.IsLocalPath(url));
    }
}
