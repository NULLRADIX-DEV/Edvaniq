using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.UnitTests;

// #298: what the check of material and output takes from the classifier, and the local list in front of it.
public sealed class ModerationCheckTests
{
    [Theory]
    [InlineData("safe")]
    [InlineData(" Safe \n")]
    [InlineData("\nsafe\n\n")]
    public void Safe_StopsNothing(string answer) =>
        Assert.Equal([], ModerationCheck.Read(answer));

    // Only the chosen categories stop material: violent crimes, sex crimes, abuse of children, weapons, hate,
    // self-harm and sexual content. Crimes without violence, advice, privacy and the like are school material.
    [Theory]
    [InlineData("unsafe\nS12", new[] { "S12" })]
    [InlineData("unsafe\nS1,S4", new[] { "S1", "S4" })]
    [InlineData("UNSAFE\ns10, s11", new[] { "S10", "S11" })]
    [InlineData("unsafe\nS3,S9,S3", new[] { "S3", "S9" })]
    [InlineData("unsafe\nS2,S12", new[] { "S12" })]
    [InlineData("unsafe\nS2", new string[0])]
    [InlineData("unsafe\nS5,S6,S7,S8,S13,S14", new string[0])]
    public void Unsafe_StopsOnlyTheChosenCategories(string answer, string[] codes) =>
        Assert.Equal(codes, ModerationCheck.Read(answer));

    // Anything else is asked again and never taken as safe.
    [Theory]
    [InlineData("")]
    [InlineData("unsafe")]
    [InlineData("unsafe\n")]
    [InlineData("vielleicht")]
    [InlineData("unsafe\nGewalt")]
    [InlineData("unsafe\nS12; S1")]
    [InlineData("{\"safe\":true}")]
    public void AnythingElse_IsNoAnswer(string answer) =>
        Assert.Null(ModerationCheck.Read(answer));

    [Fact]
    public void Packages_KeepTheOrder_AndSectionsWhole()
    {
        var texts = new[] { new string('a', 6), new string('b', 6), new string('c', 20), "", new string('d', 3) };

        var packages = ModerationCheck.Packages(texts, maxCharacters: 15);

        Assert.Equal([$"{texts[0]}\n\n{texts[1]}", texts[2], texts[4]], packages);
    }

    [Fact]
    public void NothingToCheck_IsNoPackage() =>
        Assert.Empty(ModerationCheck.Packages(["", "  "], 100));

    [Theory]
    [InlineData("Du bist ein Hurensohn.")]
    [InlineData("so eine MISSGEBURT")]
    [InlineData("Wichser!")]
    public void LocalList_CatchesInsults(string text) =>
        Assert.True(ModerationCheck.HitsLocalList(text));

    // School material of every kind passes the list, whatever it is about. Only the classifier judges topics.
    [Theory]
    [InlineData("Bei der Befruchtung verschmelzen Eizelle und Spermium, beim Geschlechtsverkehr gelangen Spermien in die Scheide.")]
    [InlineData("Im Zweiten Weltkrieg wurden Millionen Menschen ermordet, der Holocaust war ein Völkermord.")]
    [InlineData("Suizidgedanken sind behandelbar. Hilfe gibt es rund um die Uhr bei der Telefonseelsorge.")]
    [InlineData("Die Hure Babylon ist ein Bild aus der Offenbarung des Johannes.")]
    [InlineData("Ein Spaß ist erlaubt, ein Spast nicht: Schimpfwörter verletzen.")]
    [InlineData("Die Fotosynthese wandelt Licht in chemische Energie um.")]
    public void LocalList_LetsSchoolMaterialPass(string text) =>
        Assert.False(ModerationCheck.HitsLocalList(text));
}