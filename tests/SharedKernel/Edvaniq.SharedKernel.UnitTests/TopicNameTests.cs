namespace Edvaniq.SharedKernel.UnitTests;

// The rule of tests/ReferenceSet/README.md, which Knowledge merges topics by.
public class TopicNameTests
{
    [Theory]
    [InlineData("pH-Wert", "ph wert")]
    [InlineData("Größe & Maße", "groesse masse")]
    [InlineData("  Zell–Kern (Nucleus) ", "zell kern nucleus")]
    [InlineData("ÄÖÜ", "aeoeue")]
    [InlineData("Kapitel 3.2", "kapitel 3 2")]
    [InlineData("---", "")]
    public void Normalize_SpellsOutUmlauts_LowersCase_AndKeepsOnlyLettersAndDigits(string name, string expected) =>
        Assert.Equal(expected, TopicName.Normalize(name));
}