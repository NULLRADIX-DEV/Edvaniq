using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Content.UnitTests;

// #1046: single and multiple choice get their options in a random order with the right ones moved along, so the answer
// isn't always where the model put it. True/false keeps its order. Text, explanation and the right option texts stay.
public class QuestionShuffleTests
{
    private static readonly Guid Section = Guid.NewGuid();

    // 400 questions with the answer always first: afterwards each of the 4 places holds it about a quarter of the time.
    // The bounds are wide on purpose, so another permutation doesn't make the test wobble.
    [Fact]
    public void SingleChoice_SpreadsTheAnswerOverAllPlaces()
    {
        var random = new Random(1046);
        var question = Draft(QuestionType.SingleChoice, ["A", "B", "C", "D"], [0]);

        var places = Enumerable.Range(0, 400).Select(_ => question.Shuffled(random).Answer).ToList();

        Assert.All(Enumerable.Range(0, 4), place => Assert.InRange(places.Count(answer => answer == place), 60, 140));
    }

    [Fact]
    public void MultipleChoice_SpreadsTheRightOptionsToo()
    {
        var random = new Random(339);
        var question = Draft(QuestionType.MultipleChoice, ["A", "B", "C", "D"], [0, 1]);

        var firsts = Enumerable.Range(0, 400).Select(_ => question.Shuffled(random)).Count(shuffled => shuffled.Answers.SequenceEqual([0, 1]));

        Assert.InRange(firsts, 30, 110);
    }

    // Whatever the order, the right options are the same texts, the answer is the lowest of them, and nothing else changed.
    [Theory]
    [InlineData(QuestionType.SingleChoice, new[] { 2 })]
    [InlineData(QuestionType.MultipleChoice, new[] { 0, 3 })]
    [InlineData(QuestionType.MultipleChoice, new[] { 0, 1, 2, 3, 4 })]
    public void Shuffled_KeepsTheRightOptionsAndEverythingElse(QuestionType type, int[] answers)
    {
        var random = new Random(7);
        var question = Draft(type, ["Wasser", "Zucker", "Licht", "Luft", "Erde"], answers);

        for (var round = 0; round < 50; round++)
        {
            var shuffled = question.Shuffled(random);

            Assert.Equal(question.Options.Order(), shuffled.Options.Order());
            Assert.Equal(answers.Select(right => question.Options[right]).Order(), shuffled.Answers.Select(right => shuffled.Options[right]).Order());
            Assert.Equal(shuffled.Answers.Order(), shuffled.Answers);
            Assert.Equal(shuffled.Answers[0], shuffled.Answer);
            Assert.Equal(
                (question.SectionId, question.Type, question.Text, question.Explanation, question.Difficulty),
                (shuffled.SectionId, shuffled.Type, shuffled.Text, shuffled.Explanation, shuffled.Difficulty));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TrueFalse_KeepsItsOrder(int answer)
    {
        var question = Draft(QuestionType.TrueFalse, ["Wahr", "Falsch"], [answer]);

        var shuffled = question.Shuffled(new Random(1));

        Assert.Same(question, shuffled);
    }

    private static QuestionDraft Draft(QuestionType type, string[] options, int[] answers) =>
        new(Section, type, "Was spaltet Licht?", options, answers, "Licht spaltet Wasser.", 2);
}