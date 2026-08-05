using Tesria.Api.Infrastructure.Auth;
using Xunit;

namespace Tesria.Api.Tests;

public class PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_Verify_succeeds_for_correct_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");
        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");
        Assert.False(_hasher.Verify("Correct Horse Battery Staple", hash));
    }

    [Fact]
    public void Hash_is_salted_so_two_hashes_of_same_password_differ()
    {
        Assert.NotEqual(_hasher.Hash("same-password"), _hasher.Hash("same-password"));
    }
}
