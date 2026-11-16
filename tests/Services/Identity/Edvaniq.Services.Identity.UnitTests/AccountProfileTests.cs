using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.UnitTests;

// #255: pseudonym and year of birth, the only things a user tells about themself.
public sealed class AccountProfileTests
{
    [Theory]
    [InlineData("Lernfuchs", "Lernfuchs")]
    [InlineData("  Max   der  Große ", "Max der Große")]
    [InlineData("ü_2-x.y", "ü_2-x.y")]
    [InlineData("abc", "abc")]
    [InlineData("abcdefghijklmnopqrstuvwx", "abcdefghijklmnopqrstuvwx")]
    public void ValidPseudonym_IsKept_WithSingleSpaces(string typed, string kept) =>
        Assert.Equal(kept, AccountProfile.NormalizePseudonym(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("123")]
    [InlineData("a@b.de")]
    [InlineData("<script>")]
    [InlineData("Max!")]
    public void InvalidPseudonym_IsRefused(string? typed) => Assert.Null(AccountProfile.NormalizePseudonym(typed));

    [Theory]
    [InlineData(2018, true)]
    [InlineData(1926, true)]
    [InlineData(2019, false)]
    [InlineData(1925, false)]
    public void BirthYear_IsPlausible_From8To100Years(int year, bool plausible) =>
        Assert.Equal(plausible, AccountProfile.IsPlausibleBirthYear(year, 2026));

    // Of age only when the year lies 19 years back: born 2008, someone might still be 17 in 2026.
    [Theory]
    [InlineData(2007, false)]
    [InlineData(2008, true)]
    [InlineData(2015, true)]
    public void Minor_UntilTheYearLies19YearsBack(int year, bool minor) =>
        Assert.Equal(minor, AccountProfile.IsMinor(year, 2026));
}