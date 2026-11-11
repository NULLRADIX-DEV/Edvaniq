using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

public class TodayPlannerTests
{
    // Monday 16.11.2026, 11:00 in Berlin.
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DailyBudget Budget = DailyBudget.Uniform(30);

    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void WeakerTopics_ComeFirst_AndGetMoreTime()
    {
        var plan = TodayPlanner.Plan([A, B, C], new Dictionary<Guid, int> { [A] = 80, [B] = 20, [C] = 0 }, Budget, Now);

        // Since #369 the day starts with a review block of the weakest topic, so 25 minutes are left to learn.
        Assert.Equal([new Block(C, 5, BlockKind.Review), new Block(C, 15), new Block(B, 10)], plan.Blocks);
        Assert.Equal(Monday, plan.StudyDay);
        Assert.Null(plan.Empty);
    }

    [Fact]
    public void Blocks_FillTodaysBudget()
    {
        var plan = TodayPlanner.Plan([A, B], new Dictionary<Guid, int> { [A] = 50, [B] = 50 }, DailyBudget.Uniform(45), Now);

        Assert.Equal(45, plan.Blocks.Sum(block => block.Minutes));
    }

    // LearningEngine starts a topic it knows at 50. No entry means it hasn't heard of the topic yet, which is weaker.
    [Fact]
    public void TopicLearningEngineDoesNotKnowYet_CountsAsMasteryZero_BeforeAnUnpractisedOneAt50()
    {
        var plan = TodayPlanner.Plan([A, B], new Dictionary<Guid, int> { [A] = 50 }, Budget, Now);

        Assert.Equal(B, plan.Blocks[0].TopicId);
        Assert.True(plan.Blocks.Where(block => block.TopicId == B).Sum(block => block.Minutes)
            > plan.Blocks.Where(block => block.TopicId == A).Sum(block => block.Minutes));
    }

    [Fact]
    public void NoTopics_GiveAnEmptyPlan_WithTheNoTopicsHint()
    {
        var plan = TodayPlanner.Plan([], new Dictionary<Guid, int>(), Budget, Now);

        Assert.Empty(plan.Blocks);
        Assert.Equal(TodayPlanEmpty.NoTopics, plan.Empty);
        Assert.Equal(Monday, plan.StudyDay);
    }

    [Fact]
    public void EveryTopicAtHundred_GivesAnEmptyPlan_WithTheAllMasteredHint()
    {
        var plan = TodayPlanner.Plan([A, B], new Dictionary<Guid, int> { [A] = 100, [B] = 100 }, Budget, Now);

        Assert.Empty(plan.Blocks);
        Assert.Equal(TodayPlanEmpty.AllMastered, plan.Empty);
    }

    [Fact]
    public void MasteredTopic_GetsNoBlock_TheOthersDo()
    {
        var plan = TodayPlanner.Plan([A, B], new Dictionary<Guid, int> { [A] = 100, [B] = 30 }, Budget, Now);

        Assert.All(plan.Blocks, block => Assert.Equal(B, block.TopicId));
    }

    // Without mastery (LearningEngine down) every topic weighs the same, so the id decides.
    [Fact]
    public void WithoutMastery_TopicsGoByIdAlone()
    {
        var plan = TodayPlanner.Plan([C, A, B], new Dictionary<Guid, int>(), Budget, Now);

        Assert.Equal(A, plan.Blocks[0].TopicId);
        Assert.Equal([A, B, C], plan.Blocks.Where(block => block.Kind == BlockKind.Learn).Select(block => block.TopicId));
    }

    [Fact]
    public void SameInput_InAnyOrder_GivesTheSamePlan()
    {
        var masteries = new Dictionary<Guid, int> { [A] = 30, [B] = 30, [C] = 60 };
        var reversed = new Dictionary<Guid, int> { [C] = 60, [B] = 30, [A] = 30 };

        var first = TodayPlanner.Plan([A, B, C], masteries, Budget, Now);
        var second = TodayPlanner.Plan([C, B, A], reversed, Budget, Now);
        var third = TodayPlanner.Plan([A, B, C], masteries, Budget, Now);

        Assert.Equal(first.Blocks, second.Blocks);
        Assert.Equal(first.Blocks, third.Blocks);
    }

    [Fact]
    public void TopicTwice_CountsOnce()
    {
        var plan = TodayPlanner.Plan([A, A], new Dictionary<Guid, int> { [A] = 20 }, Budget, Now);

        Assert.Equal(30, plan.Blocks.Sum(block => block.Minutes));
        Assert.All(plan.Blocks, block => Assert.Equal(A, block.TopicId));
    }

    // Values from outside stay in 0 to 100 instead of breaking the plan.
    [Fact]
    public void MasteryOutOfRange_IsKeptInRange()
    {
        var plan = TodayPlanner.Plan([A, B], new Dictionary<Guid, int> { [A] = 150, [B] = -20 }, Budget, Now);

        Assert.All(plan.Blocks, block => Assert.Equal(B, block.TopicId));
    }

    // 03:30 on Tuesday in Berlin is still Monday's study day, with Monday's minutes.
    [Fact]
    public void BeforeFourInTheMorning_PlansTheDayBefore()
    {
        var tuesdayHalfPastThree = new DateTime(2026, 11, 17, 2, 30, 0, DateTimeKind.Utc);
        var budget = DailyBudget.Of(Enum.GetValues<DayOfWeek>().ToDictionary(day => day, day => day == DayOfWeek.Monday ? 20 : 60));

        var plan = TodayPlanner.Plan([A], new Dictionary<Guid, int>(), budget, tuesdayHalfPastThree);

        Assert.Equal(Monday, plan.StudyDay);
        Assert.Equal(20, plan.Blocks.Sum(block => block.Minutes));
    }
}