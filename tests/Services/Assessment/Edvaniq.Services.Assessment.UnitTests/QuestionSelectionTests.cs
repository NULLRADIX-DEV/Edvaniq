using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.UnitTests;

// #393: one way to pick the questions of a topic, for one question and for a block's quiz.
public sealed class QuestionSelectionTests
{
    [Fact]
    public void Picks_TheCount_NoneTwice()
    {
        var pool = CheckQuestionsTests.Pool(1, 1, 2, 2, 3, 3, 3);

        var picked = QuestionSelection.Pick(pool, 5, new Random(7));

        Assert.Equal(5, picked.Count);
        Assert.Equal(5, picked.Distinct().Count());
        Assert.All(picked, question => Assert.Contains(question, pool));
    }

    [Fact]
    public void SmallerPool_GivesAllOfIt()
    {
        var pool = CheckQuestionsTests.Pool(1, 2);

        Assert.Equal(pool.ToHashSet(), QuestionSelection.Pick(pool, 5, new Random(7)).ToHashSet());
    }

    [Fact]
    public void Pick_LeavesThePoolAsItWas()
    {
        var pool = CheckQuestionsTests.Pool(1, 2, 3);
        List<Question> before = [.. pool];

        QuestionSelection.Pick(pool, 2, new Random(7));

        Assert.Equal(before, pool);
    }
}