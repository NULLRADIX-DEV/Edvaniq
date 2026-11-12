using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #395: finishing a session ends it once and makes its block done, and the session names the topic its unit introduced.
public class SessionCompleteTests
{
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DayBlock Block = DayBlocks.WithIds("alice", Goal, Today, [new Block(Topic, 15)])[0];

    // AC3 in the domain: a second finish, or one after breaking off, changes nothing.
    [Fact]
    public void Complete_EndsTheSession_Once_AtItsLastStep()
    {
        var session = Start(topicIsNew: true);

        Assert.True(session.Complete(Now.AddMinutes(4)));
        Assert.False(session.Complete(Now.AddMinutes(9)));
        Assert.False(session.Abort(Now.AddMinutes(9)));

        Assert.False(session.IsActive);
        Assert.Null(session.ActiveOwnerId);
        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.Equal(Now.AddMinutes(4), session.EndedAt);
        Assert.Equal(session.Steps.Count - 1, session.CurrentStep);
        Assert.Equal(240, session.ActiveSeconds);
    }

    [Fact]
    public void Complete_AfterAbort_ChangesNothing()
    {
        var session = Start(topicIsNew: true);
        session.Abort(Now.AddMinutes(1));

        Assert.False(session.Complete(Now.AddMinutes(2)));
        Assert.Equal(SessionOutcome.Aborted, session.Outcome);
        Assert.Equal(0, session.CurrentStep);
    }

    // The block as it was planned, done at the end of the session, for the day it was planned for.
    [Fact]
    public void DoneBlock_IsTheBlockOfTheSession()
    {
        var session = Start(topicIsNew: false);
        session.Complete(Now.AddMinutes(3));

        var done = session.ToDoneBlock();

        Assert.Equal((Block.Id, "alice", Goal, Today), (done.Id, done.OwnerId, done.LearningGoalId, done.StudyDay));
        Assert.Equal(Block.Block, done.Block);
        Assert.Equal(Now.AddMinutes(3), done.DoneAt);
    }

    [Fact]
    public void DoneBlock_OfARunningSession_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Start(topicIsNew: false).ToDoneBlock());

    // Steps of a new topic: review, unit, quiz. The unit counts once the session is finished or broken off past it.
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, true)]
    [InlineData(false, null, false)]
    [InlineData(false, 1, false)]
    public void IntroducedTopic_IsTheNewTopic_OnceItsUnitIsDone(bool topicIsNew, int? abortedAtStep, bool introduced)
    {
        var session = Start(topicIsNew);
        if (abortedAtStep is { } step)
        {
            session.Advance(step, Now);
            session.Abort(Now.AddMinutes(1));
        }
        else
        {
            session.Complete(Now.AddMinutes(1));
        }

        Assert.Equal(introduced ? [Topic] : [], session.IntroducedTopicIds());
    }

    [Fact]
    public void RunningSession_IntroducesNothing() =>
        Assert.Empty(Start(topicIsNew: true).IntroducedTopicIds());

    private static Session Start(bool topicIsNew) => Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now, topicIsNew);
}