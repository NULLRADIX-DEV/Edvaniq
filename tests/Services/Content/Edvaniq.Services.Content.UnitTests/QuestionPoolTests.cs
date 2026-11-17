using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// A pool counts what went out and wants a top-up below 15, up to three rounds with an answer. An outage waits the
// delays in turn, after the last one the pool is left as it is.
public class QuestionPoolTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    [Fact]
    public void NewPool_CountsItsQuestions_AndKnowsThem()
    {
        var pool = Pool(12);

        Assert.Equal((12, 8, true), (pool.Questions, pool.Missing, pool.WantsTopUp));
        Assert.True(pool.Knows(Key(0)));
        Assert.False(pool.Knows(Key(12)));
    }

    [Fact]
    public void FullPool_WantsNoTopUp() =>
        Assert.False(Pool(QuestionPool.MinQuestions).WantsTopUp);

    // A pool without a target of its own is meant to have MaxQuestions and is short just below MinQuestions.
    [Fact]
    public void PoolWithoutTarget_IsShortBelowMinQuestions()
    {
        var pool = Pool(QuestionPool.MinQuestions - 1);

        Assert.Equal((QuestionPool.MaxQuestions, true), (pool.Goal, pool.WantsTopUp));
    }

    [Fact]
    public void TopUp_AddsTheNewKeys_AndEndsTheWait()
    {
        var pool = Pool(12);
        pool.Postpone(Now, Delays, null);

        Assert.True(pool.ToppedUp(Keys(12, 5)));

        Assert.Equal((17, 3, false, 1, 0, (DateTime?)null), (pool.Questions, pool.Missing, pool.WantsTopUp, pool.TopUps, pool.TopUpOutages, pool.NextTopUpAt));
        Assert.True(pool.Knows(Key(16)));
    }

    [Fact]
    public void ThreeRoundsWithoutEnough_StopTheTopUps()
    {
        var pool = Pool(5);

        Assert.True(pool.ToppedUp(Keys(5, 2)));
        Assert.True(pool.ToppedUp([]));
        Assert.False(pool.ToppedUp(Keys(7, 1)));

        Assert.Equal((8, QuestionPool.MaxTopUps, true, false), (pool.Questions, pool.TopUps, pool.TopUpStopped, pool.WantsTopUp));
    }

    [Fact]
    public void Outages_WaitTheDelaysInTurn_ThenStop()
    {
        var pool = Pool(10);

        Assert.True(pool.Postpone(Now, Delays, null));
        Assert.Equal(Now.AddSeconds(30), pool.NextTopUpAt);
        Assert.True(pool.Postpone(Now, Delays, TimeSpan.FromHours(2)));
        Assert.Equal(Now + Document.MaxOutageDelay, pool.NextTopUpAt);

        Assert.False(pool.Postpone(Now, Delays, null));

        Assert.Equal((true, false, (DateTime?)null), (pool.TopUpStopped, pool.WantsTopUp, pool.NextTopUpAt));
    }

    [Fact]
    public void Wait_SetsTheNextTry_WithoutCountingARound()
    {
        var pool = Pool(10);

        pool.WaitUntil(Now.AddHours(3));

        Assert.Equal((Now.AddHours(3), 0, true), (pool.NextTopUpAt!.Value, pool.TopUps, pool.WantsTopUp));
    }

    [Fact]
    public void Pool_HoldsAtMostTwenty_AndOnlyRealKeys()
    {
        var pool = Pool(18);

        Assert.Throws<InvalidOperationException>(() => pool.ToppedUp(Keys(18, 3)));
        Assert.Throws<ArgumentException>(() => pool.ToppedUp(["kurz"]));
    }

    // #915: every job of the AI after the pool takes the next round, whatever kind it is.
    [Fact]
    public void Rounds_CountEveryJobAfterThePool()
    {
        var pool = Pool(10);

        Assert.Equal([1, 2, 3], [pool.NextRound(), pool.NextRound(), pool.NextRound()]);
    }

    // The material a pool was made from carries no keys, material added later the keys of its questions.
    [Fact]
    public void Material_KeepsTheKeysOfWhatItBrought()
    {
        var pool = Pool(10);
        var section = Guid.NewGuid();

        Assert.Empty(PoolMaterial.Of(pool, section).Keys);
        Assert.Empty(PoolMaterial.Added(pool, section, []).Keys);
        Assert.Equal([Key(30), Key(31)], PoolMaterial.Added(pool, section, [Key(30), Key(31)]).Keys);
        Assert.Equal((pool.TopicId, section, pool.OwnerId), (PoolMaterial.Of(pool, section).TopicId, section, PoolMaterial.Of(pool, section).OwnerId));
    }

    private static QuestionPool Pool(int questions) =>
        QuestionPool.For(Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", Now), Guid.NewGuid(), Now, Keys(0, questions));

    private static List<string> Keys(int from, int count) => [.. Enumerable.Range(from, count).Select(Key)];

    private static string Key(int number) => number.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}