using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.UnitTests;

// #393, #387: one way to pick questions, for one question, a block's quiz and the quiz of the weaknesses. Recently
// answered questions only when nothing else is left, the difficulty mixed, the weaknesses in turn, the severest first.
public sealed class QuestionSelectionTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<Guid, DateTime> NeverAnswered = [];

    [Fact]
    public void Picks_TheCount_NoneTwice()
    {
        var pool = CheckQuestionsTests.Pool(1, 1, 2, 2, 3, 3, 3);

        var picked = QuestionSelection.Pick(pool, NeverAnswered, 5, Now, new Random(7));

        Assert.Equal(5, picked.Questions.Count);
        Assert.Equal(5, picked.Questions.Distinct().Count());
        Assert.All(picked.Questions, question => Assert.Contains(question, pool));
        Assert.False(picked.PoolTooSmall);
    }

    [Fact]
    public void SmallerPool_GivesAllOfIt_AndSaysSo()
    {
        var pool = CheckQuestionsTests.Pool(1, 2);

        var picked = QuestionSelection.Pick(pool, NeverAnswered, 5, Now, new Random(7));

        Assert.Equal(pool.ToHashSet(), picked.Questions.ToHashSet());
        Assert.True(picked.PoolTooSmall);
    }

    [Fact]
    public void Pick_LeavesThePoolAsItWas()
    {
        var pool = CheckQuestionsTests.Pool(1, 2, 3);
        List<Question> before = [.. pool];

        QuestionSelection.Pick(pool, NeverAnswered, 2, Now, new Random(7));

        Assert.Equal(before, pool);
    }

    [Fact]
    public void RecentlyAnswered_StayOut_WhileThereAreOthers()
    {
        var pool = CheckQuestionsTests.Pool(1, 1, 2, 2, 3, 3);
        var recent = pool.Take(3).ToDictionary(question => question.Id, _ => Now.AddDays(-1));

        foreach (var seed in Enumerable.Range(0, 20))
        {
            var picked = QuestionSelection.Pick(pool, recent, 3, Now, new Random(seed));

            Assert.Equal(pool.Skip(3).ToHashSet(), picked.Questions.ToHashSet());
            Assert.False(picked.PoolTooSmall);
        }
    }

    [Fact]
    public void NeverAnswered_ComeBeforeThoseAnsweredLongAgo()
    {
        var pool = CheckQuestionsTests.Pool(1, 1, 1, 1);
        var longAgo = pool.Take(2).ToDictionary(question => question.Id, _ => Now.AddDays(-30));

        var picked = QuestionSelection.Pick(pool, longAgo, 2, Now, new Random(7));

        Assert.Equal(pool.Skip(2).ToHashSet(), picked.Questions.ToHashSet());
        Assert.False(picked.PoolTooSmall);
    }

    // The boundary: answered exactly 7 days ago is no longer recent, a second later it is.
    [Fact]
    public void Recent_MeansTheLastSevenDays()
    {
        var question = CheckQuestionsTests.Pool(1)[0];

        Assert.False(QuestionSelection.IsRecent(question, new Dictionary<Guid, DateTime> { [question.Id] = Now - QuestionSelection.Recent }, Now));
        Assert.True(QuestionSelection.IsRecent(question, new Dictionary<Guid, DateTime> { [question.Id] = Now - QuestionSelection.Recent + TimeSpan.FromSeconds(1) }, Now));
        Assert.False(QuestionSelection.IsRecent(question, NeverAnswered, Now));
    }

    [Fact]
    public void WithoutOthers_TheLongestAgoComesBack_AndThePoolIsTooSmall()
    {
        var pool = CheckQuestionsTests.Pool(1, 2, 3);
        var answered = new Dictionary<Guid, DateTime>
        {
            [pool[0].Id] = Now.AddHours(-1),
            [pool[1].Id] = Now.AddDays(-3),
            [pool[2].Id] = Now.AddDays(-30),
        };

        var picked = QuestionSelection.Pick(pool, answered, 2, Now, new Random(7));

        Assert.Equal([pool[2], pool[1]], picked.Questions);
        Assert.True(picked.PoolTooSmall);
    }

    [Fact]
    public void Difficulties_TakeTurns()
    {
        var pool = CheckQuestionsTests.Pool(3, 3, 3, 1, 1, 1, 2, 2, 2);

        var picked = QuestionSelection.Pick(pool, NeverAnswered, 6, Now, new Random(7));

        Assert.Equal([1, 2, 3, 1, 2, 3], picked.Questions.Select(question => question.Difficulty));
    }

    [Fact]
    public void Difficulties_TakeTurns_UntilOneRunsOut()
    {
        var pool = CheckQuestionsTests.Pool(1, 3, 3, 3);

        var picked = QuestionSelection.Pick(pool, NeverAnswered, 4, Now, new Random(7));

        Assert.Equal([1, 3, 3, 3], picked.Questions.Select(question => question.Difficulty));
    }

    [Fact]
    public void SameSeed_SamePick_OtherSeeds_OtherPicks()
    {
        var pool = CheckQuestionsTests.Pool([.. Enumerable.Repeat(2, 10)]);

        var sets = Enumerable.Range(0, 20)
            .Select(seed => string.Join(",", QuestionSelection.Pick(pool, NeverAnswered, 3, Now, new Random(seed)).Questions.Select(question => question.Id)))
            .ToList();

        Assert.Equal(
            QuestionSelection.Pick(pool, NeverAnswered, 3, Now, new Random(3)).Questions,
            QuestionSelection.Pick(pool, NeverAnswered, 3, Now, new Random(3)).Questions);
        Assert.True(sets.Distinct().Count() > 1);
    }

    [Fact]
    public void Weaknesses_TakeTurns_TheSeverestFirst()
    {
        var severest = CheckQuestionsTests.Pool(1, 1, 1);
        var lighter = CheckQuestionsTests.Pool(1, 1, 1);

        var picked = QuestionSelection.PickAcross([severest, lighter], NeverAnswered, 5, Now, new Random(7));

        Assert.Equal([true, false, true, false, true], picked.Questions.Select(severest.Contains));
        Assert.False(picked.PoolTooSmall);
    }

    [Fact]
    public void Weaknesses_FreshQuestionsOfAnyTopic_BeforeARecentOne()
    {
        var severest = CheckQuestionsTests.Pool(1, 1);
        var lighter = CheckQuestionsTests.Pool(1, 1, 1);
        var recent = severest.ToDictionary(question => question.Id, _ => Now.AddDays(-1));

        var picked = QuestionSelection.PickAcross([severest, lighter], recent, 4, Now, new Random(7));

        Assert.Equal(lighter.ToHashSet(), picked.Questions.Take(3).ToHashSet());
        Assert.Contains(picked.Questions[3], severest);
        Assert.True(picked.PoolTooSmall);
    }

    [Fact]
    public void Weaknesses_OnlyFromTheGivenTopics()
    {
        var weakness = CheckQuestionsTests.Pool(1, 2);

        var picked = QuestionSelection.PickAcross([weakness], NeverAnswered, 5, Now, new Random(7));

        Assert.Equal(weakness.ToHashSet(), picked.Questions.ToHashSet());
        Assert.True(picked.PoolTooSmall);
    }
}