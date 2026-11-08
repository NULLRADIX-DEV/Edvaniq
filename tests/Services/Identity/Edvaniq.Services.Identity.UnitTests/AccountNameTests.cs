using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.UnitTests;

public sealed class AccountNameTests
{
    [Theory]
    [InlineData("ben", "ben")]
    [InlineData("Ben", "ben")]
    [InlineData("  BEN\t", "ben")]
    [InlineData("anna.m-1_x", "anna.m-1_x")]
    public void ValidName_IsTrimmedAndLowerCase(string name, string expected) =>
        Assert.Equal(expected, AccountName.Normalize(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("ümlaut")]
    [InlineData("ben\nadmin")]
    [InlineData("a@b")]
    public void InvalidName_IsNull(string? name) => Assert.Null(AccountName.Normalize(name));

    [Fact]
    public void Name_HasAtMostTheColumnLength()
    {
        Assert.NotNull(AccountName.Normalize(new string('a', Account.NameMaxLength)));
        Assert.Null(AccountName.Normalize(new string('a', Account.NameMaxLength + 1)));
    }
}