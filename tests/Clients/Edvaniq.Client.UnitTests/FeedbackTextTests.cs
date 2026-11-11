using System.Reflection;
using System.Text.RegularExpressions;

using Edvaniq.Client.UI.Feedback;

namespace Edvaniq.Client.UnitTests;

// The rules from docs/ux-texts.md, run against every text in FeedbackTexts: no reproach, no pressure.
public sealed class FeedbackTextTests
{
    // Word starts only, so "falsch" also catches "falsche", but "Muster" is no "muss".
    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("Urteil „falsch“", Word("falsch")),
        ("Bedauern „leider“", Word("leider\\b")),
        ("Vorwurf der Wiederholung", Word("(schon wieder|wieder nicht|immer noch)\\b")),
        ("Druck durch Pflicht", Word("(muss|müss|sollst\\b|solltest\\b)")),
        ("Urteil über die Person", Word("(schlecht|schwach|versag|durchgefallen|enttäusch)")),
        ("Zeitdruck", Word("(endlich\\b|beeil|sofort\\b|schneller\\b)")),
        ("Kleinreden", Word("nur (noch )?(\\d|ein|eine|zwei|drei|wenige)\\b")),
        ("mehr als ein Ausrufezeichen", new Regex("!.*!", RegexOptions.Singleline)),
        ("Großbuchstaben", new Regex("\\b\\p{Lu}{3,}\\b")),
    ];

    public static TheoryData<SessionResult> Results => new(Enum.GetValues<SessionResult>());

    [Theory]
    [MemberData(nameof(Results))]
    public void EveryResult_HasATitleAndAText(SessionResult result)
    {
        var text = FeedbackTexts.For(result);

        Assert.False(string.IsNullOrWhiteSpace(text.Title));
        Assert.False(string.IsNullOrWhiteSpace(text.Body));
    }

    [Fact]
    public void ResultTexts_AreAllDifferent()
    {
        var titles = Enum.GetValues<SessionResult>().Select(result => FeedbackTexts.For(result).Title).ToList();

        Assert.Equal(titles.Count, titles.Distinct().Count());
    }

    [Fact]
    public void NoText_BreaksARule()
    {
        var texts = AllTexts();
        Assert.True(texts.Count >= 25, $"only {texts.Count} texts found, the reflection misses some");

        var broken = texts.SelectMany(text => Violations(text).Select(rule => $"{rule}: {text}")).ToList();

        Assert.Empty(broken);
    }

    // Each rule has to fire on a text that breaks it, or the check above would pass on anything.
    [Theory]
    [InlineData("Leider nicht richtig.", "Bedauern „leider“")]
    [InlineData("Das war falsch.", "Urteil „falsch“")]
    [InlineData("Schon wieder daneben.", "Vorwurf der Wiederholung")]
    [InlineData("Du kannst es immer noch nicht.", "Vorwurf der Wiederholung")]
    [InlineData("Du musst mehr üben.", "Druck durch Pflicht")]
    [InlineData("Das solltest du wissen.", "Druck durch Pflicht")]
    [InlineData("Das war schlecht.", "Urteil über die Person")]
    [InlineData("Du hast versagt.", "Urteil über die Person")]
    [InlineData("Beeil dich.", "Zeitdruck")]
    [InlineData("Nur 3 von 10 richtig.", "Kleinreden")]
    [InlineData("Super!!", "mehr als ein Ausrufezeichen")]
    [InlineData("Toll! Weiter so!", "mehr als ein Ausrufezeichen")]
    [InlineData("Das war NICHT richtig.", "Großbuchstaben")]
    public void EachRule_FindsWhatItForbids(string text, string rule) =>
        Assert.Contains(rule, Violations(text));

    [Theory]
    [InlineData("Diesmal nicht richtig.")]
    [InlineData("Richtig! Gut gemacht.")]
    [InlineData("Muster erkannt, das sitzt.")]
    [InlineData("Edvaniq ist vielleicht kurz nicht erreichbar.")]
    public void Rules_LetFriendlyTextsThrough(string text) =>
        Assert.Empty(Violations(text));

    [Fact]
    public void Doc_NamesEveryRule()
    {
        var doc = File.ReadAllText(Path.Combine(Repository.Root, "docs", "ux-texts.md"));

        Assert.All(Rules, rule => Assert.Contains(rule.Rule, doc, StringComparison.Ordinal));
    }

    private static Regex Word(string pattern) =>
        new("\\b" + pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> Violations(string text) =>
        Rules.Where(rule => rule.Pattern.IsMatch(text)).Select(rule => rule.Rule).ToList();

    // Every string constant in FeedbackTexts and its nested classes, plus the texts of every session result.
    private static List<string> AllTexts()
    {
        var constants = new[] { typeof(FeedbackTexts) }
            .Concat(typeof(FeedbackTexts).GetNestedTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);
        var results = Enum.GetValues<SessionResult>()
            .Select(FeedbackTexts.For)
            .SelectMany(text => new[] { text.Title, text.Body });
        return [.. constants, .. results];
    }
}