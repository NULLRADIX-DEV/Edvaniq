using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.UnitTests;

// The goal's level and language as Planning names them, and the default where it gives none or one Content doesn't know.
public class LearnerProfileTests
{
    [Fact]
    public void Default_IsGermanAtMiddleSecondaryLevel() =>
        Assert.Equal(new LearnerProfile(LearnerLevel.MiddleSecondary, "de"), LearnerProfile.Default);

    [Fact]
    public void Of_TakesPlanningsNames() =>
        Assert.Equal(new LearnerProfile(LearnerLevel.University, "en"), LearnerProfile.Of("University", "en"));

    [Theory]
    [InlineData(null, null)]
    [InlineData("Grundschule", "fr")]
    [InlineData("university", "EN")]
    [InlineData("1", "")]
    public void Of_UnknownOrMissing_GivesTheDefault(string? level, string? language) =>
        Assert.Equal(LearnerProfile.Default, LearnerProfile.Of(level, language));

    [Fact]
    public void Of_KeepsAKnownPart_WhenTheOtherIsMissing() =>
        Assert.Equal(new LearnerProfile(LearnerLevel.Apprenticeship, "de"), LearnerProfile.Of("Apprenticeship", null));

    [Fact]
    public void All_HasEveryLevelInEveryLanguage() =>
        Assert.Equal(Enum.GetValues<LearnerLevel>().Length * LearnerProfile.Languages.Count, LearnerProfile.All.Distinct().Count());
}