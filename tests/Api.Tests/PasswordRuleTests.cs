using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Tesria.Api.Infrastructure;
using Tesria.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// One rule for every new password (T1-026): at least 8 characters, not only
/// spaces, not a common password, at most 1024. Sign-in never applies it.
/// </summary>
public class PasswordRuleTests
{
    private record RegisteredDto(Guid Id, List<string> RecoveryCodes);

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("12345678")]
    [InlineData("Qwertyuiop")]
    [InlineData("iloveyou")]
    public void Common_passwords_are_refused_whatever_their_case(string password)
    {
        Assert.True(PasswordRules.IsCommon(password));
        Assert.Contains("most common", PasswordRules.Problem(password));
    }

    [Fact]
    public void The_rule_in_full()
    {
        Assert.Equal("Password must be at least 8 characters.", PasswordRules.Problem("Abc12!x"));
        Assert.Equal("Password must be at least 8 characters.", PasswordRules.Problem(null));
        Assert.Equal("Password cannot be only spaces.", PasswordRules.Problem("        "));
        Assert.Equal("Password cannot be only spaces.", PasswordRules.Problem(" \t \n    "));
        Assert.Equal("Password must be at most 1024 characters.", PasswordRules.Problem(new string('x', 1025)));
        Assert.Null(PasswordRules.Problem(new string('x', 1024)));
        Assert.Null(PasswordRules.Problem("correct horse battery staple"));
        // The passwords the rest of the tests use are not on the list.
        Assert.Null(PasswordRules.Problem("supersecret"));
        Assert.Null(PasswordRules.Problem("a-brand-new-secret"));
    }

    [Fact]
    public async Task Registration_and_so_the_setup_wizards_owner_is_held_to_it()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        foreach (var weak in new[] { "password", "12345678", "        ", new string('p', 5000) })
        {
            var res = await client.PostAsJsonAsync("/api/auth/register",
                new { Email = "owner@example.com", DisplayName = "Owner", Password = weak });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("\"password\"", await res.Content.ReadAsStringAsync());
        }
        // Nobody was made the owner by any of those.
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync());

        (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "owner@example.com", DisplayName = "Owner", Password = "supersecret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_password_change_a_reset_link_and_a_recovery_code_are_held_to_it()
    {
        using var factory = new TestAppFactory();
        var owner = factory.CreateClient();
        await owner.PostAsJsonAsync("/api/auth/register",
            new { Email = "owner@example.com", DisplayName = "Owner", Password = "supersecret" });
        var client = factory.CreateClient();
        var member = (await (await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "member@example.com", DisplayName = "Member", Password = "supersecret" }))
            .Content.ReadFromJsonAsync<RegisteredDto>())!;

        var change = await client.PutAsJsonAsync("/api/auth/me/password",
            new { CurrentPassword = "supersecret", NewPassword = "Password1" });
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Contains("most common", await change.Content.ReadAsStringAsync());

        // A refused password does not spend the recovery code.
        var code = member.RecoveryCodes[0];
        var recover = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "member@example.com", Code = code, NewPassword = "        " });
        Assert.Equal(HttpStatusCode.BadRequest, recover.StatusCode);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/code",
            new { Email = "member@example.com", Code = code, NewPassword = "a-brand-new-secret" })).EnsureSuccessStatusCode();

        // Nor the reset link.
        var link = await (await owner.PostAsync($"/api/admin/users/{member.Id}/reset-password", null)).Content.ReadAsStringAsync();
        var token = Regex.Match(link, @"token=([0-9a-f]{64})").Groups[1].Value;
        Assert.NotEmpty(token);
        var reset = await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = token, NewPassword = "qwertyuiop" });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Contains("\"newPassword\"", await reset.Content.ReadAsStringAsync());
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/recover/token",
            new { Token = token, NewPassword = "yet-another-secret" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_password_set_before_the_rule_still_signs_in()
    {
        using var factory = new TestAppFactory();
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { Email = "old@example.com", DisplayName = "Old", Password = "supersecret" })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash("password");
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, hash));
        }

        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Email = "old@example.com", Password = "password" })).EnsureSuccessStatusCode();
    }
}
