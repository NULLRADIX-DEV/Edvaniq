using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.UnitTests;

public sealed class CheckQuestionsTests
{
    internal static readonly Guid TopicId = Guid.NewGuid();

    [Fact]
    public void FromAMixedPool_ThreeComeEasiestFirst()
    {
        var pool = Pool(3, 2, 1, 3, 1, 2);

        var picked = CheckQuestions.Pick(pool, new Random(7))!;

        Assert.Equal([1, 1, 2], picked.Select(question => question.Difficulty));
    }

    [Fact]
    public void WithoutEasyQuestions_TheNextStepFillsUp()
    {
        var picked = CheckQuestions.Pick(Pool(3, 2, 3, 3), new Random(7))!;

        Assert.Equal([2, 3, 3], picked.Select(question => question.Difficulty));
    }

    [Fact]
    public void ExactlyTwo_BothCome()
    {
        var pool = Pool(3, 1);

        var picked = CheckQuestions.Pick(pool, new Random(7))!;

        Assert.Equal([pool[1], pool[0]], picked);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(20)]
    public void ThreeOrMore_ExactlyThreeCome(int count)
    {
        var picked = CheckQuestions.Pick(Pool([.. Enumerable.Repeat(1, count)]), new Random(7))!;

        Assert.Equal(3, picked.Count);
        Assert.Equal(3, picked.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwo_IsNotEnough(int count) =>
        Assert.Null(CheckQuestions.Pick(Pool([.. Enumerable.Repeat(1, count)]), new Random(7)));

    // Within a difficulty the pick is random, the same seed gives the same set.
    [Fact]
    public void WithinADifficulty_TheSeedDecides()
    {
        var pool = Pool([.. Enumerable.Repeat(1, 10)]);

        var sets = Enumerable.Range(0, 20).Select(seed => CheckQuestions.Pick(pool, new Random(seed))!).ToList();

        Assert.Equal(CheckQuestions.Pick(pool, new Random(3)), CheckQuestions.Pick(pool, new Random(3)));
        Assert.True(sets.Select(set => string.Join(",", set.Select(question => question.Id))).Distinct().Count() > 1);
    }

    internal static List<Question> Pool(params int[] difficulties) =>
        [.. difficulties.Select(difficulty => Question.Create(
            Guid.NewGuid(), "alice", TopicId, Guid.NewGuid(), 0, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1,
            "Zwei und zwei ist vier.", difficulty, "simulated", "questions-1", new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc)))];
}