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

    private static readonly Dictionary<Guid, TopicStanding> NothingFromLearningEngine = [];

    [Fact]
    public void WeakerTopics_ComeFirst_AndGetMoreTime()
    {
        var plan = TodayPlanner.Plan([A, B, C], Introduced(new() { [A] = 80, [B] = 20, [C] = 0 }), Budget, Now);

        // Since #369 the day starts with a review block of the weakest topic, so 25 minutes are left to learn.
        Assert.Equal([new Block(C, 5, BlockKind.Review), new Block(C, 15), new Block(B, 10)], plan.Blocks);
        Assert.Equal(Monday, plan.StudyDay);
        Assert.Null(plan.Empty);
    }

    [Fact]
    public void Blocks_FillTodaysBudget()
    {
        var plan = TodayPlanner.Plan([A, B], Introduced(new() { [A] = 50, [B] = 50 }), DailyBudget.Uniform(45), Now);

        Assert.Equal(45, plan.Blocks.Sum(block => block.Minutes));
    }

    // LearningEngine starts a topic it knows at 50. No entry means it hasn't heard of the topic yet, which is weaker,
    // and new, so the review goes to the topic it knows.
    [Fact]
    public void TopicLearningEngineDoesNotKnowYet_CountsAsMasteryZero_BeforeAnUnpractisedOneAt50()
    {
        var plan = TodayPlanner.Plan([A, B], Introduced(new() { [A] = 50 }), Budget, Now);

        Assert.Equal(new Block(A, 5, BlockKind.Review), plan.Blocks[0]);
        Assert.Equal(B, plan.Blocks.First(block => block.Kind == BlockKind.Learn).TopicId);
        Assert.True(plan.Blocks.Where(block => block.TopicId == B).Sum(block => block.Minutes)
            > plan.Blocks.Where(block => block.TopicId == A && block.Kind == BlockKind.Learn).Sum(block => block.Minutes));
    }

    // AK1 and AK3 of #367: the state is LearningEngine's. With the same mastery, only another state from LearningEngine
    // changes the plan, nothing in Planning has to: new topics get no review, introduced ones do.
    [Fact]
    public void SameMastery_AnotherStateFromLearningEngine_GivesAnotherPlan()
    {
        var asNew = TodayPlanner.Plan([A, B], Standings(TopicState.New, new() { [A] = 50, [B] = 50 }), Budget, Now);
        var asBuilding = TodayPlanner.Plan([A, B], Standings(TopicState.Building, new() { [A] = 50, [B] = 50 }), Budget, Now);
        var asWeakness = TodayPlanner.Plan([A, B], Standings(TopicState.Weakness, new() { [A] = 50, [B] = 50 }), Budget, Now);

        Assert.All(asNew.Blocks, block => Assert.Equal(BlockKind.Learn, block.Kind));
        Assert.Equal(30, asNew.Blocks.Sum(block => block.Minutes));
        Assert.Equal(new Block(A, 5, BlockKind.Review), asBuilding.Blocks[0]);
        Assert.Equal(asBuilding.Blocks, asWeakness.Blocks);
    }

    // Only "new" makes a topic new. One new among introduced topics still gets its learn time, just no review.
    [Fact]
    public void NewTopic_AmongIntroducedOnes_GetsLearnTime_ButNoReview()
    {
        var standings = new Dictionary<Guid, TopicStanding>
        {
            [A] = new(0, TopicState.New),
            [B] = new(60, TopicState.Secure),
        };

        var plan = TodayPlanner.Plan([A, B], standings, Budget, Now);

        Assert.Equal(new Block(B, 5, BlockKind.Review), plan.Blocks[0]);
        Assert.Equal(A, plan.Blocks[1].TopicId);
        Assert.DoesNotContain(plan.Blocks, block => block.TopicId == A && block.Kind == BlockKind.Review);
    }

    // A state LearningEngine names that Planning doesn't know yet doesn't break the plan: the mastery counts, and the
    // topic counts as introduced, LearningEngine knows it after all.
    [Fact]
    public void UnknownState_CountsAsIntroduced_WithItsMastery()
    {
        var plan = TodayPlanner.Plan([A, B], new Dictionary<Guid, TopicStanding> { [A] = new(20, null), [B] = new(90, null) }, Budget, Now);

        Assert.Equal(new Block(A, 5, BlockKind.Review), plan.Blocks[0]);
        Assert.Equal(A, plan.Blocks[1].TopicId);
    }

    [Fact]
    public void NoTopics_GiveAnEmptyPlan_WithTheNoTopicsHint()
    {
        var plan = TodayPlanner.Plan([], NothingFromLearningEngine, Budget, Now);

        Assert.Empty(plan.Blocks);
        Assert.Equal(TodayPlanEmpty.NoTopics, plan.Empty);
        Assert.Equal(Monday, plan.StudyDay);
    }

    [Fact]
    public void EveryTopicAtHundred_GivesAnEmptyPlan_WithTheAllMasteredHint()
    {
        var plan = TodayPlanner.Plan([A, B], Introduced(new() { [A] = 100, [B] = 100 }), Budget, Now);

        Assert.Empty(plan.Blocks);
        Assert.Equal(TodayPlanEmpty.AllMastered, plan.Empty);
    }

    [Fact]
    public void MasteredTopic_GetsNoBlock_TheOthersDo()
    {
        var plan = TodayPlanner.Plan([A, B], Introduced(new() { [A] = 100, [B] = 30 }), Budget, Now);

        Assert.All(plan.Blocks, block => Assert.Equal(B, block.TopicId));
    }

    // Without mastery (LearningEngine down) every topic weighs the same and counts as new, so the id decides and there
    // is no review.
    [Fact]
    public void WithoutMastery_TopicsGoByIdAlone()
    {
        var plan = TodayPlanner.Plan([C, A, B], NothingFromLearningEngine, Budget, Now);

        // Since #371 today is the first day of the week's plan: the 30 learn minutes go to A and B, C comes up tomorrow.
        Assert.Equal([new Block(A, 15), new Block(B, 15)], plan.Blocks);
        var week = StudyPlanner.Plan([new(C, 1, 0, IsNew: true), new(A, 1, 0, IsNew: true), new(B, 1, 0, IsNew: true)], Budget, Monday, examDay: null);
        Assert.Equal(C, week.Days[1].Blocks.First(block => block.Kind == BlockKind.Learn).TopicId);
    }

    [Fact]
    public void SameInput_InAnyOrder_GivesTheSamePlan()
    {
        var masteries = Introduced(new() { [A] = 30, [B] = 30, [C] = 60 });
        var reversed = Introduced(new() { [C] = 60, [B] = 30, [A] = 30 });

        var first = TodayPlanner.Plan([A, B, C], masteries, Budget, Now);
        var second = TodayPlanner.Plan([C, B, A], reversed, Budget, Now);
        var third = TodayPlanner.Plan([A, B, C], masteries, Budget, Now);

        Assert.Equal(first.Blocks, second.Blocks);
        Assert.Equal(first.Blocks, third.Blocks);
    }

    [Fact]
    public void TopicTwice_CountsOnce()
    {
        var plan = TodayPlanner.Plan([A, A], Introduced(new() { [A] = 20 }), Budget, Now);

        Assert.Equal(30, plan.Blocks.Sum(block => block.Minutes));
        Assert.All(plan.Blocks, block => Assert.Equal(A, block.TopicId));
    }

    // Values from outside stay in 0 to 100 instead of breaking the plan.
    [Fact]
    public void MasteryOutOfRange_IsKeptInRange()
    {
        var plan = TodayPlanner.Plan([A, B], Introduced(new() { [A] = 150, [B] = -20 }), Budget, Now);

        Assert.All(plan.Blocks, block => Assert.Equal(B, block.TopicId));
    }

    // 03:30 on Tuesday in Berlin is still Monday's study day, with Monday's minutes.
    [Fact]
    public void BeforeFourInTheMorning_PlansTheDayBefore()
    {
        var tuesdayHalfPastThree = new DateTime(2026, 11, 17, 2, 30, 0, DateTimeKind.Utc);
        var budget = DailyBudget.Of(Enum.GetValues<DayOfWeek>().ToDictionary(day => day, day => day == DayOfWeek.Monday ? 20 : 60));

        var plan = TodayPlanner.Plan([A], NothingFromLearningEngine, budget, tuesdayHalfPastThree);

        Assert.Equal(Monday, plan.StudyDay);
        Assert.Equal(20, plan.Blocks.Sum(block => block.Minutes));
    }

    // #370: 15 minutes a day leave 70 minutes to learn in the week, room for 4 of 6 topics. The weakness from
    // LearningEngine stays although its mastery is the highest, the lightest topics in progress are shortened.
    [Fact]
    public void ShortWeek_KeepsTheWeaknessFromLearningEngine_AndNamesTheShortenedTopics()
    {
        var ids = Enumerable.Range(1, 6).Select(i => new Guid($"00000000-0000-0000-0000-00000000000{i}")).ToArray();
        var standings = ids.ToDictionary(id => id, _ => new TopicStanding(50, TopicState.Building));
        standings[ids[0]] = new TopicStanding(60, TopicState.Weakness);

        var plan = TodayPlanner.Plan(ids, standings, DailyBudget.Uniform(15), Now);
        var enoughTime = TodayPlanner.Plan(ids, standings, Budget, Now);

        Assert.Equal([ids[4], ids[5]], plan.Shortened);
        Assert.Empty(enoughTime.Shortened);
    }

    private static Dictionary<Guid, TopicStanding> Introduced(Dictionary<Guid, int> masteries) =>
        Standings(TopicState.Building, masteries);

    private static Dictionary<Guid, TopicStanding> Standings(TopicState state, Dictionary<Guid, int> masteries) =>
        masteries.ToDictionary(entry => entry.Key, entry => new TopicStanding(entry.Value, state));
}