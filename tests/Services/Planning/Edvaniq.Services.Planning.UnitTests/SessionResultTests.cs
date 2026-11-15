using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #403: what a session brought. Counts from the stored answers, the partial result of a block broken off and started
// again, and no mastery until LearningEngine has all answers and the introduction.
public class SessionResultTests
{
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DayBlock Block = DayBlocks.WithIds("alice", Goal, Today, [new Block(Topic, 15)])[0];
    private static readonly DayBlock OtherBlock = DayBlocks.WithIds("alice", Goal, Today, [new Block(Topic, 15), new Block(Guid.NewGuid(), 15)])[1];

    // AC1: right and wrong are the stored answers, a shown solution came as wrong.
    [Fact]
    public void Counts_AreTheAnswers()
    {
        var session = Completed(Now, Now.AddMinutes(10));
        var answers = new[] { Answer(true, 2), Answer(false, 3), Answer(true, 4), Answer(false, 5) };

        var result = SessionResult.Of([session], answers, Changes(Topic));

        Assert.Equal((4, 2, 2), (result.Answered, result.Correct, result.Wrong));
        Assert.True(result.Complete);
    }

    // AC4: the session broken off on the block counts with the one finished after it, a later one and another block
    // don't.
    [Fact]
    public void Chain_IsTheBlocksSessionsUpToThisOne()
    {
        var broken = Aborted(Now, Now.AddMinutes(3));
        var finished = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now.AddMinutes(5), previous: broken);
        finished.Complete(Now.AddMinutes(9));
        var elsewhere = Session.Start(Guid.NewGuid(), "alice", Goal, Today, OtherBlock, Now.AddMinutes(1));
        elsewhere.Abort(Now.AddMinutes(2));

        var chain = SessionResult.ChainOf(finished, [finished, elsewhere, broken]);

        Assert.Equal([broken.Id, finished.Id], chain.Select(session => session.Id));
        Assert.Equal([broken.Id], SessionResult.ChainOf(broken, [finished, broken]).Select(session => session.Id));
        Assert.Equal(new SessionWindow(Now, Now.AddMinutes(9)), SessionResult.WindowOf(chain, []));
    }

    // #1040: the unit on a block started with its first session, broken off ones included, whatever runs elsewhere.
    [Fact]
    public void UnitStartedAt_IsTheFirstSessionOnTheBlock()
    {
        var broken = Aborted(Now, Now.AddMinutes(3));
        var again = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now.AddMinutes(5), previous: broken);
        var elsewhere = Session.Start(Guid.NewGuid(), "alice", Goal, Today, OtherBlock, Now.AddMinutes(-30));
        elsewhere.Abort(Now.AddMinutes(-20));

        Assert.Equal(Now, again.UnitStartedAt([again, broken, elsewhere]));
        Assert.Equal(Now, broken.UnitStartedAt([broken]));
        Assert.Equal(Now.AddMinutes(5), Completed(Now.AddMinutes(5), Now.AddMinutes(9)).UnitStartedAt([]));
    }

    // An answer sent again after the end still lies inside, so the mastery after counts it.
    [Fact]
    public void Window_EndsAtTheLastAnswer_WhenThatCameLater()
    {
        var session = Completed(Now, Now.AddMinutes(10));

        Assert.Equal(new SessionWindow(Now, Now.AddMinutes(12)), SessionResult.WindowOf([session], [Answer(true, 4), Answer(true, 12)]));
    }

    [Fact]
    public void Window_OfARunningSession_Throws()
    {
        var running = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now);

        Assert.Throws<ArgumentException>(() => SessionResult.WindowOf([running], []));
    }

    // #406 in advance: an answer LearningEngine hasn't got yet, LearningEngine down, or the topic missing there gives the
    // counts but no mastery, never an old one.
    [Fact]
    public void NotAllThere_GivesCountsWithoutMastery()
    {
        var session = Completed(Now, Now.AddMinutes(10));
        var answers = new[] { Answer(true, 2) };

        foreach (var changes in new[] { Changes(Topic) with { MissingAnswers = 1 }, null, new MasteryChanges([], 0) })
        {
            var result = SessionResult.Of([session], answers, changes);

            Assert.Equal((1, 1, false), (result.Answered, result.Correct, result.Complete));
            Assert.Null(result.Topics);
        }
    }

    // A learn block of a new topic introduces it: until LearningEngine has the introduction, the mastery after would
    // still be the one of a new topic.
    [Fact]
    public void Introduction_MustHaveArrived()
    {
        var session = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now, topicIsNew: true);
        session.Complete(Now.AddMinutes(10));

        Assert.False(SessionResult.Of([session], [], Changes(Topic, introduced: false)).Complete);
        Assert.True(SessionResult.Of([session], [], Changes(Topic, introduced: true)).Complete);
        Assert.True(SessionResult.Of([Completed(Now, Now.AddMinutes(10))], [], Changes(Topic, introduced: false)).Complete);
    }

    [Fact]
    public void Topics_TheBlocksFirst_ThenThoseOfTheAnswers()
    {
        var other = Guid.NewGuid();
        var session = Completed(Now, Now.AddMinutes(10));
        var answers = new[] { Answer(true, 2, other), Answer(true, 3) };

        var result = SessionResult.Of([session], answers, Changes(other, Topic));

        Assert.Equal([Topic, other], SessionResult.TopicsOf([session], answers));
        Assert.Equal([Topic, other], result.Topics!.Select(topic => topic.TopicId));
    }

    private static Session Completed(DateTime start, DateTime end)
    {
        var session = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, start);
        session.Complete(end);
        return session;
    }

    private static Session Aborted(DateTime start, DateTime end)
    {
        var session = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, start);
        session.Abort(end);
        return session;
    }

    private static SessionAnswer Answer(bool correct, int minute, Guid? topic = null) =>
        new(Guid.NewGuid(), topic ?? Topic, correct, Now.AddMinutes(minute));

    private static MasteryChanges Changes(params Guid[] topics) => Changes(topics, introduced: false);

    private static MasteryChanges Changes(Guid topic, bool introduced) => Changes([topic], introduced);

    private static MasteryChanges Changes(Guid[] topics, bool introduced) =>
        new([.. topics.Select(id => new TopicChange(id, new TopicStanding(50, TopicState.Building), new TopicStanding(62, TopicState.Building), introduced))], 0);
}