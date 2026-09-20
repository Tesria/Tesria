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
    public void A_token_signed_with_another_secret_is_refused()
    {
        var issued = With("one-secret").IssueForPage(Guid.NewGuid(), null);

        Assert.Null(With("another-secret").Verify(issued));
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

    [Fact]
    public void An_expired_token_is_refused()
    {
        // Built by hand at a time in the past, because the lifetime is not
        // something a caller gets to choose.
        var tokens = With();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            sub = (string?)null,
            scope = "page",
            id = Guid.NewGuid().ToString(),
            exp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
        });
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("a-shared-secret"),
                System.Text.Encoding.UTF8.GetBytes(encoded)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Null(tokens.Verify($"{RenderTokens.Prefix}{encoded}.{signature}"));
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
