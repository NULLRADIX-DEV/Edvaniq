using Edvaniq.Services.Identity.Application;

namespace Edvaniq.Services.Identity.UnitTests;

public sealed class PasswordHasherTests
{
    private const string Password = "correct horse battery";

    [Fact]
    public void Hash_NamesAlgorithmAndRounds_AndHoldsNoPassword()
    {
        var hash = PasswordHasher.Hash(Password);

        Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.Rounds}$", hash, StringComparison.Ordinal);
        Assert.Equal(4, hash.Split('$').Length);
        Assert.DoesNotContain(Password, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void SamePassword_GetsAnotherSalt_EveryTime() =>
        Assert.NotEqual(PasswordHasher.Hash(Password), PasswordHasher.Hash(Password));

    [Fact]
    public void Verify_TakesTheRightPassword_Only()
    {
        var hash = PasswordHasher.Hash(Password);

        Assert.True(PasswordHasher.Verify(Password, hash));
        Assert.False(PasswordHasher.Verify("correct horse battery ", hash));
        Assert.False(PasswordHasher.Verify("Correct horse battery", hash));
    }

    // No account: the dummy hash is checked, and nothing passes, not even its own password.
    [Fact]
    public void Verify_WithoutHash_IsFalse()
    {
        Assert.False(PasswordHasher.Verify(Password, null));
        Assert.False(PasswordHasher.Verify("no account has this password", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("md5$abc")]
    [InlineData("pbkdf2-sha512$1$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
    public void Verify_UnknownFormat_IsFalse(string stored) => Assert.False(PasswordHasher.Verify(Password, stored));

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void Check_AllowsTwelveTo256Characters(int length, bool allowed) =>
        Assert.Equal(allowed, PasswordHasher.Check(new string('x', length)) is null);
}