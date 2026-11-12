using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #393: a session broken off ends as Aborted, moves only forward, and one started again on the block goes on from it.
public class SessionAbortTests
{
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DayBlock Block = DayBlocks.WithIds("alice", Goal, Today, [new Block(Topic, 15)])[0];

    [Fact]
    public void Abort_EndsTheSession_Once()
    {
        var session = Start(topicIsNew: true);

        Assert.True(session.Abort(Now.AddMinutes(5)));
        Assert.False(session.Abort(Now.AddMinutes(9)));

        Assert.False(session.IsActive);
        Assert.Null(session.ActiveOwnerId);
        Assert.Equal(SessionOutcome.Aborted, session.Outcome);
        Assert.Equal(Now.AddMinutes(5), session.EndedAt);
    }

    [Fact]
    public void Advance_OnlyMovesForward()
    {
        var session = Start(topicIsNew: true);

        Assert.Equal(SessionAdvance.Advanced, session.Advance(2, Now));
        Assert.Equal(SessionAdvance.Unchanged, session.Advance(1, Now));
        Assert.Equal(SessionAdvance.Unchanged, session.Advance(2, Now));
        Assert.Equal(2, session.CurrentStep);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Advance_PastTheSteps_IsNoSuchStep(int step)
    {
        // Review and quiz only.
        var session = Start(topicIsNew: false);

        Assert.Equal(SessionAdvance.NoSuchStep, session.Advance(step, Now));
        Assert.Equal(0, session.CurrentStep);
    }

    [Fact]
    public void Advance_AfterAbort_IsEnded()
    {
        var session = Start(topicIsNew: true);
        session.Abort(Now);

        Assert.Equal(SessionAdvance.Ended, session.Advance(1, Now));
        Assert.Equal(0, session.CurrentStep);
    }

    // The new session keeps the steps and the step of the one broken off, even if the topic counts as known by now.
    [Fact]
    public void StartAgain_GoesOnFromTheSessionBrokenOff()
    {
        var previous = Start(topicIsNew: true);
        previous.Advance(2, Now);
        previous.Abort(Now.AddMinutes(5));

        var again = Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now.AddMinutes(10), topicIsNew: false, previous);

        Assert.Equal(previous.Steps, again.Steps);
        Assert.Equal(2, again.CurrentStep);
        Assert.True(again.IsActive);
        Assert.Equal(SessionOutcome.Running, again.Outcome);
    }

    [Fact]
    public void StartAgain_OnlyFromASessionBrokenOffOnTheSameBlock()
    {
        var running = Start(topicIsNew: true);
        var other = DayBlocks.WithIds("alice", Goal, Today, [new Block(Guid.NewGuid(), 15)])[0];
        var otherAborted = Session.Start(Guid.NewGuid(), "alice", Goal, Today, other, Now);
        otherAborted.Abort(Now);

        Assert.Throws<ArgumentException>(() => Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now, previous: running));
        Assert.Throws<ArgumentException>(() => Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now, previous: otherAborted));
    }

    private static Session Start(bool topicIsNew) => Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now, topicIsNew);
}