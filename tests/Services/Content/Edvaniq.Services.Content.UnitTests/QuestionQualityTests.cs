using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Content.UnitTests;

// #342: the numbers on a pool, each with a case that has it and one that doesn't.
public class QuestionQualityTests
{
    private const string Source =
        "Die Chloroplasten enthalten den grünen Farbstoff Chlorophyll. Er nimmt das Licht auf, das die Pflanze für die Fotosynthese braucht.";

    [Fact]
    public void Question_FromItsSource_IsSourced_AndAnotherNot()
    {
        var sourced = Choice("Welcher Farbstoff nimmt in den Chloroplasten das Licht auf?", ["Chlorophyll", "Hämoglobin", "Melanin"], 0);
        var unsourced = Choice("Welches Organ pumpt das Blut durch den Körper?", ["Herz", "Leber", "Niere"], 0);

        Assert.True(QuestionQuality.Of(sourced, Source).Sourced);
        Assert.False(QuestionQuality.Of(unsourced, Source).Sourced);
    }

    [Fact]
    public void Options_ThatReadAlmostTheSame_AreNotDistinct()
    {
        var distinct = Choice("Was nimmt das Licht auf?", ["Chlorophyll im Chloroplasten", "Stärke in der Wurzel"], 0);
        var close = Choice("Was nimmt das Licht auf?", ["Chlorophyll nimmt in den Chloroplasten der Blätter das Licht auf", "Chlorophyll nimmt in den Chloroplasten der Blätter das Licht ab"], 0);

        Assert.True(QuestionQuality.Of(distinct, Source).Distinct);
        Assert.False(QuestionQuality.Of(close, Source).Distinct);
    }

    [Fact]
    public void Explanation_NamingTheAnswer_Explains_AndOneWithoutDoesNot()
    {
        var explained = Choice("Welcher Farbstoff nimmt das Licht auf?", ["Chlorophyll", "Melanin"], 0, "Das Chlorophyll in den Chloroplasten nimmt das Licht auf.");
        var vague = Choice("Welcher Farbstoff nimmt das Licht auf?", ["Chlorophyll", "Melanin"], 0, "Das steht so im Abschnitt über die Pflanze.");

        Assert.True(QuestionQuality.Of(explained, Source).Explained);
        Assert.False(QuestionQuality.Of(vague, Source).Explained);
    }

    [Fact]
    public void LongestRightOption_IsFlagged_OnlyWhenClearlyLonger()
    {
        var giveaway = Choice("Was macht das Chlorophyll?", ["Es nimmt das Licht für die Fotosynthese auf", "Wasser", "Zucker"], 0);
        var even = Choice("Was macht das Chlorophyll?", ["Licht aufnehmen", "Wasser leiten", "Zucker lagern"], 0);

        Assert.True(QuestionQuality.Of(giveaway, Source).LongestRight);
        Assert.False(QuestionQuality.Of(even, Source).LongestRight);
    }

    [Fact]
    public void TrueFalse_HasNoOptionsToTellApartOrExplain()
    {
        var question = new QuestionDraft(
            Guid.NewGuid(), QuestionType.TrueFalse, "Chlorophyll nimmt das Licht für die Fotosynthese auf.", ["Wahr", "Falsch"], [0],
            "Der grüne Farbstoff Chlorophyll nimmt das Licht auf.", 1);

        var flags = QuestionQuality.Of(question, Source);

        Assert.True(flags.Sourced);
        Assert.True(flags.WellFormed);
        Assert.Null(flags.Distinct);
        Assert.Null(flags.Explained);
        Assert.Null(flags.LongestRight);
    }

    [Fact]
    public void ChoiceWithoutAQuestion_IsNotWellFormed()
    {
        Assert.True(QuestionQuality.Of(Choice("Welcher Farbstoff nimmt das Licht auf?", ["Chlorophyll", "Melanin"], 0), Source).WellFormed);
        Assert.False(QuestionQuality.Of(Choice("Chlorophyll und Licht", ["Chlorophyll", "Melanin"], 0), Source).WellFormed);
    }

    [Fact]
    public void Card_FromItsSource_IsSourced_AndAnotherNot()
    {
        Assert.True(QuestionQuality.Sourced(new CardDraft(Guid.NewGuid(), "Was nimmt das Licht auf?", "Das Chlorophyll in den Chloroplasten."), Source));
        Assert.False(QuestionQuality.Sourced(new CardDraft(Guid.NewGuid(), "Was pumpt das Blut?", "Das Herz mit seinen Kammern."), Source));
    }

    [Fact]
    public void Tally_CountsEachNumber_OnlyWhereItApplies()
    {
        var section = Guid.NewGuid();
        var tally = new QualityTally();
        var sources = new Dictionary<Guid, string> { [section] = Source };

        tally.Add(
            [
                Choice("Welcher Farbstoff nimmt in den Chloroplasten das Licht auf?", ["Chlorophyll", "Hämoglobin", "Melanin"], 0) with { SectionId = section },
                new QuestionDraft(section, QuestionType.TrueFalse, "Chlorophyll nimmt das Licht auf.", ["Wahr", "Falsch"], [0], "Der Farbstoff Chlorophyll nimmt es auf.", 3),
            ],
            [new CardDraft(section, "Was pumpt das Blut?", "Das Herz mit seinen Kammern.")],
            sources);
        tally.Add(null, [], sources);

        Assert.Equal(2, tally.Questions);
        Assert.Equal(1, tally.Cards);
        Assert.Equal(1, tally.Unusable);
        Assert.Equal((2, 2), (tally.Sourced.Yes, tally.Sourced.Of));
        Assert.Equal(1, tally.Distinct.Of);
        Assert.Equal((0, 1), (tally.CardsSourced.Yes, tally.CardsSourced.Of));
        Assert.Equal(new[] { 1, 0, 1 }, tally.Difficulties);
    }

    private static QuestionDraft Choice(string text, string[] options, int answer, string explanation = "Das Chlorophyll in den Chloroplasten nimmt das Licht auf.") =>
        new(Guid.NewGuid(), QuestionType.SingleChoice, text, options, [answer], explanation, 1);
}