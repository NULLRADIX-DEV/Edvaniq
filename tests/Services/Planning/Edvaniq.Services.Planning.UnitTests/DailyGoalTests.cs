using System.Globalization;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.UnitTests;

// #381: the daily goal is reached when every block planned for today is done (docs/spec/acceptance.md A49, A51).
public class DailyGoalTests
{
    private const string Alice = "alice";

    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    // AC1 and AC2 as in A49: two blocks, the first done leaves the goal open, the second reaches it.
    [Fact]
    public void Goal_IsReached_WithTheLastBlock_AndNotBefore()
    {
        Block[] fresh = [new(A, 15), new(B, 15)];
        var planned = DayBlocks.WithIds(Alice, Goal, Today, fresh);

        var none = DayBlocks.WithDone(Alice, Goal, Today, fresh, [], 30);
        var one = DayBlocks.WithDone(Alice, Goal, Today, fresh, [Done(planned[0], Today)], 30);
        var both = DayBlocks.WithDone(Alice, Goal, Today, fresh, [Done(planned[0], Today), Done(planned[1], Today)], 30);

        Assert.Equal(DailyGoalState.Open, DailyGoal.Of(none));
        Assert.Equal(DailyGoalState.Open, DailyGoal.Of(one));
        Assert.Equal(DailyGoalState.Reached, DailyGoal.Of(both));
    }

    // AC2: one open block among done ones is enough to keep it open.
    [Fact]
    public void OneOpenBlock_KeepsTheGoalOpen()
    {
        var id = Guid.NewGuid();
        DayBlock[] blocks = [new(id, new Block(A, 15), Done: true), new(Guid.NewGuid(), new Block(B, 15), Done: false)];

        Assert.Equal(DailyGoalState.Open, DailyGoal.Of(blocks));
    }

    // AC1: done blocks that filled the budget leave no open ones in today's plan, so the goal is reached.
    [Fact]
    public void DoneBlocksThatFilledTheBudget_ReachTheGoal()
    {
        var done = DoneBlock.Create(Guid.NewGuid(), Alice, Goal, Today, new Block(B, 30), Now);

        var day = DayBlocks.WithDone(Alice, Goal, Today, [new Block(A, 15)], [done], 30);

        Assert.Equal(DailyGoalState.Reached, DailyGoal.Of(day));
    }

    // AC3: a day without blocks has no open goal.
    [Fact]
    public void DayWithoutBlocks_HasNoGoal()
    {
        Assert.Equal(DailyGoalState.None, DailyGoal.Of([]));
    }

    // AC3: a plan that is empty because no question pool is ready or every topic is mastered never counts as open.
    [Fact]
    public void EmptyPlan_ForAnyReason_HasNoGoal()
    {
        var budget = DailyBudget.Uniform(30);
        var pending = TodayPlanner.Plan([A], new Dictionary<Guid, TopicStanding>(), budget, Now, readyTopics: new HashSet<Guid>());
        var mastered = TodayPlanner.Plan(
            [A, B],
            new Dictionary<Guid, TopicStanding> { [A] = new(100, TopicState.Secure), [B] = new(100, TopicState.Secure) },
            budget,
            Now);

        Assert.Equal(TodayPlanEmpty.QuestionsPending, pending.Empty);
        Assert.Equal(TodayPlanEmpty.AllMastered, mastered.Empty);
        foreach (var plan in new[] { pending, mastered })
        {
            var day = DayBlocks.WithDone(Alice, Goal, plan.StudyDay, plan.Blocks, [], 30);
            Assert.Equal(DailyGoalState.None, DailyGoal.Of(day));
        }
    }

    // AC4 as in A51: a block finished just before 04:00 Europe/Berlin counts for the study day that is ending, the goal
    // of the next one stays open. Winter (UTC+1) and summer (UTC+2).
    [Theory]
    [InlineData("2026-11-17T02:59:00Z", "2026-11-17T03:01:00Z")]
    [InlineData("2027-06-15T01:59:00Z", "2027-06-15T02:01:00Z")]
    public void BlockDoneBeforeFour_CountsForTheDayEnding_NotTheNext(string justBefore, string justAfter)
    {
        var before = DateTime.Parse(justBefore, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var after = DateTime.Parse(justAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var (yesterday, today) = (StudyDay.Of(before), StudyDay.Of(after));
        Block[] fresh = [new(A, 15)];
        var done = Done(DayBlocks.WithIds(Alice, Goal, yesterday, fresh)[0], yesterday, before);

        var ending = DayBlocks.WithDone(Alice, Goal, yesterday, fresh, [done], 30);
        var next = DayBlocks.WithDone(Alice, Goal, today, fresh, [done], 30);

        Assert.Equal(today, yesterday.AddDays(1));
        Assert.Equal(DailyGoalState.Reached, DailyGoal.Of(ending));
        Assert.Equal(DailyGoalState.Open, DailyGoal.Of(next));
    }

    private static DoneBlock Done(DayBlock block, DateOnly studyDay, DateTime? doneAt = null) =>
        DoneBlock.Create(block.Id, Alice, Goal, studyDay, block.Block, doneAt ?? Now);
}