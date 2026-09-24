using Tesria.Api.Infrastructure.Export;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The credential the export sidecar's browser uses (dev-plan 12.1).
///
/// It is the one thing in the capture design that hands out access, so what
/// matters is the boundaries: signed, short-lived, scoped, and never more
/// than its user already had.
/// </summary>
public class RenderTokenTests
{
    private static RenderTokens With(string? secret = "a-shared-secret") =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pdf:SharedSecret"] = secret })
            .Build());

    [Fact]
    public void A_page_token_round_trips_its_claims()
    {
        var tokens = With();
        var page = Guid.NewGuid();
        var user = Guid.NewGuid();

        var claims = tokens.Verify(tokens.IssueForPage(page, user));

        Assert.NotNull(claims);
        Assert.Equal(user, claims!.UserId);
        Assert.Equal(RenderTokens.PageScope, claims.Scope);
        Assert.Equal(page, claims.ScopeId);
    }

    [Fact]
    public void An_anonymous_token_carries_no_user()
    {
        // The point of it: an anonymous export renders as nobody, so every
        // permission check downstream is the anonymous one.
        var tokens = With();

        var claims = tokens.Verify(tokens.IssueForPage(Guid.NewGuid(), null));

        Assert.NotNull(claims);
        Assert.Null(claims!.UserId);
    }

    [Fact]
    public void A_page_token_covers_only_that_page()
    {
        var tokens = With();
        var page = Guid.NewGuid();
        var space = Guid.NewGuid();
        var claims = tokens.Verify(tokens.IssueForPage(page, null))!;

        Assert.True(claims.CoversPage(page, space));
        Assert.False(claims.CoversPage(Guid.NewGuid(), space));
    }

    [Fact]
    public void A_space_token_covers_any_page_in_that_space()
    {
        var tokens = With();
        var space = Guid.NewGuid();
        var claims = tokens.Verify(tokens.IssueForSpace(space, null))!;

        Assert.True(claims.CoversPage(Guid.NewGuid(), space));
        Assert.False(claims.CoversPage(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void A_token_forged_with_the_pdf_services_secret_is_refused()
    {
        // What a compromised PDF sidecar could do if tokens were still signed
        // with the secret it holds (the 14.1 review): take a real payload,
        // make it the owner's, and sign it with Pdf:SharedSecret.
        var tokens = With("one-secret");
        var issued = tokens.IssueForPage(Guid.NewGuid(), null);
        var payload = issued[RenderTokens.Prefix.Length..].Split('.')[0];
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var forged = RenderTokens.Prefix + payload + "." + B64(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("one-secret"), System.Text.Encoding.UTF8.GetBytes(payload)));

        Assert.NotNull(tokens.Verify(issued));
        Assert.Null(tokens.Verify(forged));
    }

    [Fact]
    public void A_tampered_token_is_refused()
    {
        var tokens = With();
        var issued = tokens.IssueForPage(Guid.NewGuid(), Guid.NewGuid());

        // Flip a character of the payload; the signature no longer matches.
        var parts = issued[RenderTokens.Prefix.Length..].Split('.');
        var tampered = RenderTokens.Prefix + parts[0][..^1] + (parts[0][^1] == 'A' ? 'B' : 'A') + "." + parts[1];

        Assert.Null(tokens.Verify(tampered));
    }

    private sealed class PastClock(TimeSpan ago) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow - ago;
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        // Issued twenty minutes ago with the real key (the lifetime is
        // fifteen), then checked now.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pdf:SharedSecret"] = "a-shared-secret" })
            .Build();
        var old = new RenderTokens(config, new PastClock(TimeSpan.FromMinutes(20))).IssueForPage(Guid.NewGuid(), null);
        var fresh = new RenderTokens(config, new PastClock(TimeSpan.FromMinutes(10))).IssueForPage(Guid.NewGuid(), null);

        Assert.Null(With().Verify(old));
        Assert.NotNull(With().Verify(fresh));
    }

    [Fact]
    public void An_api_token_is_not_mistaken_for_a_render_token()
    {
        Assert.Null(With().Verify("tsk_something_else_entirely"));
        Assert.Null(With().Verify("not a token at all"));
    }

    [Fact]
    public void Without_a_secret_nothing_is_issued_or_accepted()
    {
        var tokens = With(secret: null);

        Assert.False(tokens.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => tokens.IssueForPage(Guid.NewGuid(), null));
        Assert.Null(tokens.Verify("trx_anything.at-all"));
    }
}
