using Edvaniq.Services.Planning.Domain;
using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.UnitTests;

// #375: what was done today stays, the open blocks come from the fresh plan, and every block keeps its id.
public class DayBlocksTests
{
    private const string Alice = "alice";

    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutDoneBlocks_TheFreshPlanIsTheDay_AllOpen()
    {
        Block[] fresh = [new(A, 5, BlockKind.Review), new(B, 15), new(A, 10)];

        var day = DayBlocks.WithDone(Alice, Goal, Today, fresh, [], 30);

        Assert.Equal(fresh, day.Select(entry => entry.Block));
        Assert.All(day, entry => Assert.False(entry.Done));
        Assert.Equal(3, day.Select(entry => entry.Id).Distinct().Count());
    }

    // AC1: after the block the mastery went up, the fresh plan looks different, the done block is still there, done.
    [Fact]
    public void DoneBlock_Stays_WhenTheFreshPlanChanged()
    {
        var planned = DayBlocks.WithIds(Alice, Goal, Today, [new Block(A, 15), new Block(B, 15)]);
        var done = Done(planned[0]);

        var day = DayBlocks.WithDone(Alice, Goal, Today, [new Block(B, 15), new Block(C, 15)], [done], 30);

        Assert.Equal(new DayBlock(planned[0].Id, new Block(A, 15), Done: true), day[0]);
        Assert.Equal(new Block(B, 15), day[1].Block);
        Assert.False(day[1].Done);
        Assert.Equal(30, day.Sum(entry => entry.Block.Minutes));
    }

    // Two blocks of one topic, the second is done: exactly that one goes, the first keeps its id.
    [Fact]
    public void TwoBlocksOfOneTopic_TheDoneOneGoes_ByItsId()
    {
        Block[] fresh = [new(A, 15), new(B, 15), new(A, 15)];
        var planned = DayBlocks.WithIds(Alice, Goal, Today, fresh);

        var day = DayBlocks.WithDone(Alice, Goal, Today, fresh, [Done(planned[2])], 45);

        Assert.Equal([planned[2].Id, planned[0].Id, planned[1].Id], day.Select(entry => entry.Id));
        Assert.Equal([true, false, false], day.Select(entry => entry.Done));
    }

    // A done block whose id the fresh plan no longer has takes the first block of its topic and kind, so it isn't
    // planned twice.
    [Fact]
    public void DoneBlock_WithAnIdNoLongerPlanned_TakesTheSameTopicAndKind()
    {
        var done = DoneBlock.Create(Guid.NewGuid(), Alice, Goal, Today, new Block(A, 10), Now);

        var day = DayBlocks.WithDone(Alice, Goal, Today, [new Block(A, 5, BlockKind.Review), new Block(A, 15), new Block(B, 15)], [done], 60);

        Assert.Equal([new Block(A, 10), new Block(A, 5, BlockKind.Review), new Block(B, 15)], day.Select(entry => entry.Block));
    }

    // Done and open together never go over the day's budget.
    [Fact]
    public void OpenBlocks_FillOnlyWhatTheBudgetLeaves()
    {
        var done = DoneBlock.Create(Guid.NewGuid(), Alice, Goal, Today, new Block(C, 20), Now);

        var day = DayBlocks.WithDone(Alice, Goal, Today, [new Block(A, 15), new Block(B, 15), new Block(A, 5)], [done], 40);

        Assert.Equal([new Block(C, 20), new Block(A, 15), new Block(A, 5)], day.Select(entry => entry.Block));
        Assert.True(day.Sum(entry => entry.Block.Minutes) <= 40);
    }

    // Blocks of another day don't count today.
    [Fact]
    public void DoneBlocksOfAnotherDay_DoNotCount()
    {
        var yesterday = DoneBlock.Create(Guid.NewGuid(), Alice, Goal, Today.AddDays(-1), new Block(A, 15), Now);

        var day = DayBlocks.WithDone(Alice, Goal, Today, [new Block(A, 15)], [yesterday], 30);

        Assert.False(Assert.Single(day).Done);
    }

    // The same input gives the same ids, another day, user or goal other ones.
    [Fact]
    public void Ids_AreStable_AndDifferPerDayUserAndGoal()
    {
        Block[] fresh = [new(A, 15)];
        var id = DayBlocks.WithIds(Alice, Goal, Today, fresh)[0].Id;

        Assert.Equal(id, DayBlocks.WithIds(Alice, Goal, Today, fresh)[0].Id);
        Assert.NotEqual(id, DayBlocks.WithIds(Alice, Goal, Today.AddDays(1), fresh)[0].Id);
        Assert.NotEqual(id, DayBlocks.WithIds("bob", Goal, Today, fresh)[0].Id);
        Assert.NotEqual(id, DayBlocks.WithIds(Alice, Guid.NewGuid(), Today, fresh)[0].Id);
        Assert.NotEqual(id, DayBlocks.WithIds(Alice, Goal, Today, [new Block(A, 15, BlockKind.Review)])[0].Id);
    }

    // A session from 03:59 to 04:10 finishes the block planned for the study day before, so it counts there and takes
    // nothing from the new day's budget.
    [Fact]
    public void BlockFinishedAfterTheDayBoundary_BelongsToTheDayItWasPlannedFor()
    {
        var started = new DateTime(2026, 11, 12, 2, 59, 0, DateTimeKind.Utc);
        var finished = new DateTime(2026, 11, 12, 3, 10, 0, DateTimeKind.Utc);
        var plannedFor = StudyDay.Of(started);
        var newDay = StudyDay.Of(finished);
        Assert.Equal(plannedFor.AddDays(1), newDay);
        var planned = DayBlocks.WithIds(Alice, Goal, plannedFor, [new Block(A, 15)]);

        var done = DoneBlock.Create(planned[0].Id, Alice, Goal, plannedFor, planned[0].Block, finished);

        Assert.Equal(plannedFor, done.StudyDay);
        Assert.True(Assert.Single(DayBlocks.WithDone(Alice, Goal, plannedFor, [new Block(A, 15)], [done], 15)).Done);
        Assert.False(Assert.Single(DayBlocks.WithDone(Alice, Goal, newDay, [new Block(A, 15)], [done], 15)).Done);
    }

    private static DoneBlock Done(DayBlock planned) =>
        DoneBlock.Create(planned.Id, Alice, Goal, Today, planned.Block, Now);
}