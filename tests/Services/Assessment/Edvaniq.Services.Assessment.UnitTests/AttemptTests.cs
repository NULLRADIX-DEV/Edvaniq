using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.UnitTests;

public sealed class AttemptTests
{
    private static readonly DateTime AskedAt = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Answer_IsRightOnlyWithTheRightOption(int option, bool correct)
    {
        var question = Question();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);

        attempt.Answer(question, option, 1500, AskedAt.AddSeconds(5));

        Assert.Equal((true, option, correct, 1500), (attempt.Answered, attempt.Option, attempt.Correct, attempt.ResponseTimeMs));
        Assert.Equal(DateTimeKind.Utc, attempt.AnsweredAt!.Value.Kind);
    }

    [Fact]
    public void AnsweredAttempt_CannotBeAnsweredAgain()
    {
        var question = Question();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);
        attempt.Answer(question, 1, 1000, AskedAt.AddSeconds(2));

        Assert.Throws<InvalidOperationException>(() => attempt.Answer(question, 0, 1000, AskedAt.AddSeconds(3)));
        Assert.Equal(1, attempt.Option);
    }

    // The client can't have seen the question longer than the server had it out.
    [Theory]
    [InlineData(2_000, 5_000, 2_000)]
    [InlineData(60_000, 5_000, 5_000)]
    [InlineData(int.MaxValue, 5_000, 5_000)]
    [InlineData(0, 5_000, 0)]
    [InlineData(1_000, -5_000, 0)]
    public void ResponseTime_IsTheMeasuredTime_AtMostTheTimeSinceAsking(int measured, int elapsedMs, int stored) =>
        Assert.Equal(stored, Attempt.ResponseTime(measured, AskedAt, AskedAt.AddMilliseconds(elapsedMs)));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Option_MustBeOneOfTheQuestion(int option, bool valid) => Assert.Equal(valid, Attempt.IsOption(Question(), option));

    [Fact]
    public void Answer_WithAnOptionTheQuestionDoesNotHave_Throws()
    {
        var question = Question();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);

        Assert.Throws<ArgumentException>(() => attempt.Answer(question, 2, 1000, AskedAt.AddSeconds(2)));
        Assert.False(attempt.Answered);
    }

    // Right are A and C of four. Only exactly that set counts, in any order.
    [Theory]
    [InlineData(new[] { 0, 2 }, true)]
    [InlineData(new[] { 2, 0 }, true)]
    [InlineData(new[] { 2, 0, 2 }, true)]
    [InlineData(new[] { 0 }, false)]
    [InlineData(new[] { 0, 1, 2 }, false)]
    [InlineData(new[] { 1, 3 }, false)]
    [InlineData(new[] { 0, 1, 2, 3 }, false)]
    public void MultipleChoice_IsRightOnlyWhenComplete(int[] chosen, bool correct)
    {
        var question = MultipleChoice();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);

        attempt.Answer(question, chosen, 1500, AskedAt.AddSeconds(5));

        Assert.Equal(correct, attempt.Correct);
        Assert.Equal(chosen.Distinct().Order(), attempt.Options);
        Assert.Null(attempt.Option);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void TrueFalse_IsRightOnlyWithTheRightOption(int option, bool correct)
    {
        var question = TrueFalse();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);

        attempt.Answer(question, option, 1500, AskedAt.AddSeconds(5));

        Assert.Equal((correct, option), (attempt.Correct, attempt.Option));
    }

    [Theory]
    [InlineData(new int[0], false)]
    [InlineData(new[] { 1 }, true)]
    [InlineData(new[] { 0, 1 }, false)]
    [InlineData(new[] { 1, 1 }, true)]
    public void SingleChoice_TakesExactlyOneOption(int[] chosen, bool valid) => Assert.Equal(valid, Attempt.IsChoice(Question(), chosen));

    [Theory]
    [InlineData(new int[0], false)]
    [InlineData(new[] { 3 }, true)]
    [InlineData(new[] { 0, 1, 2, 3 }, true)]
    [InlineData(new[] { 0, 4 }, false)]
    public void MultipleChoice_TakesOneOrMoreOptionsOfTheQuestion(int[] chosen, bool valid) =>
        Assert.Equal(valid, Attempt.IsChoice(MultipleChoice(), chosen));

    // Showing the solution is a wrong answer with the measured time, and closes the attempt like one.
    [Fact]
    public void Reveal_IsWrong_AndClosesTheAttempt()
    {
        var question = Question();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);

        attempt.Reveal(question, 4000, AskedAt.AddSeconds(5));

        Assert.Equal((true, true, false, 4000, (int?)null), (attempt.Answered, attempt.Revealed, attempt.Correct, attempt.ResponseTimeMs, attempt.Option));
        Assert.Throws<InvalidOperationException>(() => attempt.Answer(question, 1, 1000, AskedAt.AddSeconds(6)));
        Assert.False(attempt.Correct);
    }

    [Fact]
    public void AnsweredAttempt_CannotBeRevealed()
    {
        var question = Question();
        var attempt = Attempt.Ask(question, Guid.NewGuid(), AskedAt);
        attempt.Answer(question, 1, 1000, AskedAt.AddSeconds(2));

        Assert.Throws<InvalidOperationException>(() => attempt.Reveal(question, 1000, AskedAt.AddSeconds(3)));
        Assert.Equal((true, false), (attempt.Correct, attempt.Revealed));
    }

    internal static Question MultipleChoice(string owner = "alice", Guid? topicId = null) =>
        Domain.Question.Create(
            Guid.NewGuid(), owner, topicId ?? Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), "Welche sind Primzahlen?", ["2", "4", "5", "9"], 2,
            "2 und 5 haben nur sich und 1 als Teiler.", 2, "simulated", "questions-1", AskedAt, QuestionType.MultipleChoice, [2, 0]);

    internal static Question TrueFalse(string owner = "alice", Guid? topicId = null) =>
        Domain.Question.Create(
            Guid.NewGuid(), owner, topicId ?? Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), "Stimmt das: 2 + 2 = 4?", ["Wahr", "Falsch"], 0,
            "Zwei und zwei ist vier.", 1, "simulated", "questions-1", AskedAt, QuestionType.TrueFalse);

    internal static Question Question(string owner = "alice", Guid? topicId = null) =>
        Domain.Question.Create(
            Guid.NewGuid(), owner, topicId ?? Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1,
            "Zwei und zwei ist vier.", 2, "simulated", "questions-1", AskedAt);
}