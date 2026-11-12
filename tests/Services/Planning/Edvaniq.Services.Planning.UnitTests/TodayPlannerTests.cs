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

    // #380: of two topics with the same weight the one earlier in the material comes first, whatever its id.
    [Fact]
    public void SameWeight_GoesByTheOrderOfTheMaterial()
    {
        var mastery = Introduced(new() { [A] = 50, [B] = 50 });

        var bFirst = TodayPlanner.Plan([B, A], mastery, Budget, Now);
        var aFirst = TodayPlanner.Plan([A, B], mastery, Budget, Now);

        Assert.Equal(B, bFirst.Blocks.First(block => block.Kind == BlockKind.Learn).TopicId);
        Assert.Equal(A, aFirst.Blocks.First(block => block.Kind == BlockKind.Learn).TopicId);
    }

    // #380: weight is size × (100 - mastery), so a topic with three times the sections outweighs a weaker small one.
    [Fact]
    public void BiggerTopic_GetsMoreTime()
    {
        var plan = TodayPlanner.Plan(
            [A, B], Introduced(new() { [A] = 50, [B] = 30 }), DailyBudget.Uniform(60), Now, sizes: new Dictionary<Guid, int> { [A] = 3 });

        Assert.True(Minutes(plan, A) > Minutes(plan, B));
        Assert.True(Minutes(TodayPlanner.Plan([A, B], Introduced(new() { [A] = 50, [B] = 30 }), DailyBudget.Uniform(60), Now), A)
            < Minutes(TodayPlanner.Plan([A, B], Introduced(new() { [A] = 50, [B] = 30 }), DailyBudget.Uniform(60), Now), B));
    }

    private static int Minutes(TodayPlan plan, Guid topic) =>
        plan.Blocks.Where(block => block.TopicId == topic && block.Kind == BlockKind.Learn).Sum(block => block.Minutes);

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

    // Without mastery (LearningEngine down) every topic weighs the same and counts as new, so the order of the material
    // decides (#380, before it was the id) and there is no review.
    [Fact]
    public void WithoutMastery_TopicsGoByTheOrderOfTheMaterial()
    {
        var plan = TodayPlanner.Plan([C, A, B], NothingFromLearningEngine, Budget, Now);

        // Since #371 today is the first day of the week's plan: the 30 learn minutes go to C and A, B comes up tomorrow.
        Assert.Equal([new Block(C, 15), new Block(A, 15)], plan.Blocks);
        var week = StudyPlanner.Plan(
            [new(C, 1, 0, IsNew: true, Rank: 0), new(A, 1, 0, IsNew: true, Rank: 1), new(B, 1, 0, IsNew: true, Rank: 2)], Budget, Monday, examDay: null);
        Assert.Equal(B, week.Days[1].Blocks.First(block => block.Kind == BlockKind.Learn).TopicId);
    }

    // The order of the topics is part of the input (#380), the order LearningEngine names them in isn't.
    [Fact]
    public void SameInput_GivesTheSamePlan_WhateverOrderTheMasteryComesIn()
    {
        var masteries = Introduced(new() { [A] = 30, [B] = 30, [C] = 60 });
        var reversed = Introduced(new() { [C] = 60, [B] = 30, [A] = 30 });

        var first = TodayPlanner.Plan([A, B, C], masteries, Budget, Now);
        var second = TodayPlanner.Plan([A, B, C], reversed, Budget, Now);
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

    // #374: a topic without a ready question pool can't be practised, so it gets neither learn time nor a review, even
    // as the weakest, and the plan names it.
    [Fact]
    public void TopicWithoutReadyPool_GetsNoBlock_AndIsNamed()
    {
        var standings = new Dictionary<Guid, TopicStanding>
        {
            [A] = new(10, TopicState.Weakness),
            [B] = new(60, TopicState.Building),
            [C] = new(40, TopicState.Building),
        };

        var plan = TodayPlanner.Plan([A, B, C], standings, Budget, Now, readyTopics: new HashSet<Guid> { B, C });

        Assert.DoesNotContain(plan.Blocks, block => block.TopicId == A);
        Assert.Equal(30, plan.Blocks.Sum(block => block.Minutes));
        Assert.Equal([A], plan.NotReady);
        Assert.Null(plan.Empty);
    }

    [Fact]
    public void NoReadyPoolAtAll_GivesAnEmptyPlan_WithTheQuestionsPendingHint()
    {
        var plan = TodayPlanner.Plan([B, A], Introduced(new() { [A] = 20, [B] = 30 }), Budget, Now, readyTopics: new HashSet<Guid>());

        Assert.Empty(plan.Blocks);
        Assert.Equal(TodayPlanEmpty.QuestionsPending, plan.Empty);
        Assert.Equal([A, B], plan.NotReady);
    }

    // Unknown pools (Assessment down) plan every topic as before #374.
    [Fact]
    public void UnknownPools_PlanEveryTopic_AsBefore()
    {
        var standings = Introduced(new() { [A] = 80, [B] = 20, [C] = 0 });

        var unknown = TodayPlanner.Plan([A, B, C], standings, Budget, Now, readyTopics: null);
        var allReady = TodayPlanner.Plan([A, B, C], standings, Budget, Now, readyTopics: new HashSet<Guid> { A, B, C });

        Assert.Equal(TodayPlanner.Plan([A, B, C], standings, Budget, Now).Blocks, unknown.Blocks);
        Assert.Equal(unknown.Blocks, allReady.Blocks);
        Assert.Empty(unknown.NotReady);
        Assert.Empty(allReady.NotReady);
    }

    // A pool that isn't ready doesn't take part in shortening: with 4 of 6 topics waiting for questions, the week has
    // room for the other two, nothing is shortened.
    [Fact]
    public void TopicsWithoutReadyPool_DoNotCountWhenShortening()
    {
        var ids = Enumerable.Range(1, 6).Select(i => new Guid($"00000000-0000-0000-0000-00000000000{i}")).ToArray();
        var standings = ids.ToDictionary(id => id, _ => new TopicStanding(50, TopicState.Building));

        var plan = TodayPlanner.Plan(ids, standings, DailyBudget.Uniform(15), Now, readyTopics: new HashSet<Guid> { ids[4], ids[5] });

        Assert.Empty(plan.Shortened);
        Assert.Equal(ids[..4], plan.NotReady);
        Assert.All(plan.Blocks, block => Assert.Contains(block.TopicId, ids[4..]));
    }

    // #909: 10 minutes a day leave 35 learn minutes in the week, room for 2 blocks. The foundation C stays although it is
    // the lightest, and takes the place of B.
    [Fact]
    public void ShortOfTime_TheFoundationStays()
    {
        var standings = Introduced(new() { [A] = 20, [B] = 30, [C] = 90 });

        var plain = TodayPlanner.Plan([A, B, C], standings, DailyBudget.Uniform(10), Now);
        var withFoundation = TodayPlanner.Plan([A, B, C], standings, DailyBudget.Uniform(10), Now, foundations: new HashSet<Guid> { C });

        Assert.Equal([C], plain.Shortened);
        Assert.Equal([B], withFoundation.Shortened);
    }

    private static Dictionary<Guid, TopicStanding> Introduced(Dictionary<Guid, int> masteries) =>
        Standings(TopicState.Building, masteries);

    private static Dictionary<Guid, TopicStanding> Standings(TopicState state, Dictionary<Guid, int> masteries) =>
        masteries.ToDictionary(entry => entry.Key, entry => new TopicStanding(entry.Value, state));
}