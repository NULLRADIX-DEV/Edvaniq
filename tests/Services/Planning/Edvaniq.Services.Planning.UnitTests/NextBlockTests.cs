using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #407: the next open block of today's plan, a started one first (docs/spec/acceptance.md A61).
public class NextBlockTests
{
    private const string Alice = "alice";

    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Block[] Fresh = [new(A, 10), new(B, 10), new(C, 10)];
    private static readonly IReadOnlySet<Guid> NoneStarted = new HashSet<Guid>();

    // AC1: block 1 first, once it is done block 2.
    [Fact]
    public void FirstBlockFirst_ThenTheSecond_OnceTheFirstIsDone()
    {
        var planned = DayBlocks.WithIds(Alice, Goal, Today, Fresh);

        var first = NextBlock.Of(Day([]), NoneStarted);
        var second = NextBlock.Of(Day([planned[0]]), NoneStarted);

        Assert.Equal(planned[0].Id, first?.Id);
        Assert.Equal(planned[1].Id, second?.Id);
    }

    // AC2: everything done gives none, and the goal says it was reached; a day without blocks gives none as well.
    [Fact]
    public void AllDone_GivesNone_WithTheGoalReached()
    {
        var planned = DayBlocks.WithIds(Alice, Goal, Today, Fresh);
        var day = Day(planned);

        Assert.Null(NextBlock.Of(day, NoneStarted));
        Assert.Equal(DailyGoalState.Reached, DailyGoal.Of(day));
        Assert.Null(NextBlock.Of([], NoneStarted));
        Assert.Equal(DailyGoalState.None, DailyGoal.Of([]));
    }

    // AC3 as in A61: the first block done, the second untouched, the third started and left. The third goes on first,
    // once it is done the second.
    [Fact]
    public void StartedBlock_GoesOnFirst_ThenTheUntouchedOne()
    {
        var planned = DayBlocks.WithIds(Alice, Goal, Today, Fresh);
        var started = new HashSet<Guid> { planned[2].Id };

        var resumed = NextBlock.Of(Day([planned[0]]), started);
        var after = NextBlock.Of(Day([planned[0], planned[2]]), started);

        Assert.Equal(planned[2].Id, resumed?.Id);
        Assert.Equal(planned[1].Id, after?.Id);
    }

    // AC3 whatever the kind: a started review block goes before an open learn block that comes earlier in the plan.
    [Fact]
    public void StartedReviewBlock_GoesBeforeAnEarlierLearnBlock()
    {
        Block[] fresh = [new(A, 10), new(B, 5, BlockKind.Review)];
        var planned = DayBlocks.WithIds(Alice, Goal, Today, fresh);

        var next = NextBlock.Of(planned, new HashSet<Guid> { planned[1].Id });

        Assert.Equal(BlockKind.Review, next?.Block.Kind);
        Assert.Equal(planned[1].Id, next?.Id);
    }

    // A session on a block the plan no longer has changes nothing, and a started block that is done is not next.
    [Fact]
    public void StartedIds_NotOpenInThePlan_AreIgnored()
    {
        var planned = DayBlocks.WithIds(Alice, Goal, Today, Fresh);
        var started = new HashSet<Guid> { Guid.NewGuid(), planned[0].Id };

        var next = NextBlock.Of(Day([planned[0]]), started);

        Assert.Equal(planned[1].Id, next?.Id);
    }

    private static IReadOnlyList<DayBlock> Day(IReadOnlyList<DayBlock> done) =>
        DayBlocks.WithDone(
            Alice,
            Goal,
            Today,
            Fresh,
            [.. done.Select(block => DoneBlock.Create(block.Id, Alice, Goal, Today, block.Block, Now))],
            30);
}