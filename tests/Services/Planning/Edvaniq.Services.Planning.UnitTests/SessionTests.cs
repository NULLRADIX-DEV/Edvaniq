using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #376: a session is bound to its block and the block's topic from the start, and runs until it ends.
public class SessionTests
{
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Start_TakesBlockAndTopic_AndIsActive()
    {
        var block = DayBlocks.WithIds("alice", Goal, Today, [new Block(Topic, 15, BlockKind.Review)])[0];

        var session = Session.Start(Guid.NewGuid(), "alice", Goal, Today, block, Now);

        Assert.Equal(block.Id, session.BlockId);
        Assert.Equal(Topic, session.TopicId);
        Assert.Equal(BlockKind.Review, session.Kind);
        Assert.Equal(15, session.Minutes);
        Assert.Equal(Today, session.StudyDay);
        Assert.Equal(Now, session.StartedAt);
        Assert.True(session.IsActive);
        Assert.Equal("alice", session.ActiveOwnerId);
    }

    [Fact]
    public void DoneBlock_CannotBeStarted()
    {
        var block = new DayBlock(Guid.NewGuid(), new Block(Topic, 15), Done: true);

        Assert.Throws<InvalidOperationException>(() => Session.Start(Guid.NewGuid(), "alice", Goal, Today, block, Now));
    }
}