using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

public class StudyPlannerTests
{
    // 16.11.2026 is a Monday.
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateOnly ExamInTenDays = Today.AddDays(10);

    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void A22_LargerTopicsAndTopicsWithLowerMastery_GetMoreTime()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 20)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        var minutes = MinutesPerTopic(plan);
        Assert.True(minutes[A] > minutes[B]);
        Assert.True(minutes[C] > minutes[B]);
        Assert.Equal(300, minutes.Values.Sum());
    }

    [Fact]
    public void Time_FollowsSizeTimesMasteryGap()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 0)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        // Weights 100, 50 and 100 of 250. 5 of the 30 minutes each day are review, so 250 minutes are learned.
        var minutes = MinutesPerTopic(plan, BlockKind.Learn);
        Assert.Equal(100, minutes[A]);
        Assert.Equal(50, minutes[B]);
        Assert.Equal(100, minutes[C]);
    }

    [Fact]
    public void NoDay_ExceedsItsBudget()
    {
        var budget = DailyBudget.Of(new Dictionary<DayOfWeek, int>
        {
            [DayOfWeek.Monday] = 20,
            [DayOfWeek.Tuesday] = 45,
            [DayOfWeek.Wednesday] = 7,
            [DayOfWeek.Thursday] = 60,
            [DayOfWeek.Friday] = 1,
            [DayOfWeek.Saturday] = 90,
            [DayOfWeek.Sunday] = 33,
        });
        TopicToPlan[] topics = [new(A, 3, 10), new(B, 1, 90), new(C, 5, 60)];

        var plan = StudyPlanner.Plan(topics, budget, Today, ExamInTenDays);

        Assert.Equal(10, plan.Days.Count);
        Assert.All(plan.Days, day => Assert.Equal(budget.MinutesOn(day.StudyDay), day.Blocks.Sum(block => block.Minutes)));
    }

    [Fact]
    public void PlanEndsAtTheExam_TheLastBlockIsOnTheDayBefore()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.Equal(Today, plan.Days[0].StudyDay);
        Assert.Equal(ExamInTenDays.AddDays(-1), plan.Days[^1].StudyDay);
        Assert.DoesNotContain(plan.Days, day => day.StudyDay >= ExamInTenDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void ExamTodayOrEarlier_GivesAnEmptyPlan(int daysUntilExam)
    {
        var plan = StudyPlanner.Plan([new(A, 1, 0)], DailyBudget.Uniform(30), Today, Today.AddDays(daysUntilExam));

        Assert.Empty(plan.Days);
    }

    [Fact]
    public void ExamTomorrow_PlansToday()
    {
        var plan = StudyPlanner.Plan([new(A, 1, 0)], DailyBudget.Uniform(30), Today, Today.AddDays(1));

        var day = Assert.Single(plan.Days);
        Assert.Equal(Today, day.StudyDay);
        Assert.Equal(30, day.Blocks.Sum(block => block.Minutes));
    }

    [Fact]
    public void SameInput_GivesTheSameBlocks()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 20)];
        var budget = DailyBudget.Uniform(40);

        var first = StudyPlanner.Plan(topics, budget, Today, ExamInTenDays);
        var second = StudyPlanner.Plan(topics, budget, Today, ExamInTenDays);
        var reversed = StudyPlanner.Plan(topics.Reverse().ToArray(), budget, Today, ExamInTenDays);

        Assert.Equal(Flat(first), Flat(second));
        Assert.Equal(Flat(first), Flat(reversed));
    }

    [Fact]
    public void EqualTopics_GoByTopicId()
    {
        var plan = StudyPlanner.Plan([new(B, 1, 50), new(A, 1, 50)], DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.Equal(A, plan.Days[0].Blocks[0].TopicId);
    }

    [Fact]
    public void TopicsWithoutSizeOrWithFullMastery_GetNoTime()
    {
        TopicToPlan[] topics = [new(A, 0, 0), new(B, 3, 100), new(C, 1, 99)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.Equal([C], MinutesPerTopic(plan).Keys);
    }

    [Fact]
    public void NothingToLearn_GivesAnEmptyPlan()
    {
        Assert.Empty(StudyPlanner.Plan([], DailyBudget.Uniform(30), Today, ExamInTenDays).Days);
        Assert.Empty(StudyPlanner.Plan([new(A, 2, 100)], DailyBudget.Uniform(30), Today, ExamInTenDays).Days);
    }

    [Fact]
    public void BudgetsInStepsOfFive_GiveBlocksInStepsOfFive()
    {
        TopicToPlan[] topics = [new(A, 7, 13), new(B, 3, 71), new(C, 1, 42)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(35), Today, ExamInTenDays);

        Assert.All(Flat(plan), block => Assert.Equal(0, block.Minutes % StudyPlanner.Step));
        Assert.All(Flat(plan), block => Assert.InRange(block.Minutes, StudyPlanner.Step, StudyPlanner.BlockMinutes));
    }

    [Fact]
    public void Topics_TakeTurns()
    {
        var plan = StudyPlanner.Plan([new(A, 1, 0), new(B, 1, 0)], DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.All(plan.Days, day => Assert.Equal(
            [A, B], day.Blocks.Where(block => block.Kind == BlockKind.Learn).Select(block => block.TopicId).Order()));
        Assert.Equal(
            [A, B, A, B],
            plan.Days.Take(4).Select(day => day.Blocks.First(block => block.Kind == BlockKind.Learn).TopicId));
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    public void InvalidTopics_AreRejected(int size, int mastery)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StudyPlanner.Plan([new(A, size, mastery)], DailyBudget.Uniform(30), Today, ExamInTenDays));
    }

    [Fact]
    public void TheSameTopicTwice_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => StudyPlanner.Plan([new(A, 1, 50), new(A, 2, 50)], DailyBudget.Uniform(30), Today, ExamInTenDays));
    }

    [Fact]
    public void EveryStudyDay_StartsWithOneReviewBlock()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 20)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.Equal(10, plan.Days.Count);
        Assert.All(plan.Days, day =>
        {
            Assert.Equal(BlockKind.Review, day.Blocks[0].Kind);
            Assert.Single(day.Blocks, block => block.Kind == BlockKind.Review);
        });
    }

    // Fewest reviews first, then the heaviest: weights A 100, C 80, B 50.
    [Fact]
    public void ReviewBlocks_TakeTurns_HeaviestFirst()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 20)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.Equal([A, C, B, A, C, B], plan.Days.Take(6).Select(day => day.Blocks[0].TopicId));
    }

    [Theory]
    [InlineData(30, 5)]
    [InlineData(45, 5)]
    [InlineData(50, 10)]
    [InlineData(720, 10)]
    [InlineData(32, 7)]
    [InlineData(12, 7)]
    [InlineData(10, 5)]
    [InlineData(7, 7)]
    [InlineData(5, 5)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    public void ReviewBlock_IsAFifthOfTheDay_FiveToTenMinutes_PlusWhatDoesNotFillAStep(int budget, int review)
    {
        Assert.Equal(review, StudyPlanner.ReviewMinutes(budget));

        var plan = StudyPlanner.Plan([new(A, 1, 0), new(B, 1, 50)], DailyBudget.Uniform(budget), Today, ExamInTenDays);

        Assert.All(plan.Days, day =>
        {
            Assert.Equal((BlockKind.Review, review), (day.Blocks[0].Kind, day.Blocks[0].Minutes));
            Assert.Equal(budget, day.Blocks.Sum(block => block.Minutes));
            Assert.All(day.Blocks.Skip(1), block => Assert.Equal(0, block.Minutes % StudyPlanner.Step));
        });
    }

    // A budget below a block: the whole day is the review.
    [Fact]
    public void BudgetBelowOneStep_IsAllReview()
    {
        var plan = StudyPlanner.Plan([new(A, 1, 0)], DailyBudget.Uniform(3), Today, ExamInTenDays);

        Assert.All(plan.Days, day => Assert.Equal([new Block(A, 3, BlockKind.Review)], day.Blocks));
    }

    // Nothing introduced yet means nothing to review on the first day. A topic learned that day is reviewed from day 2.
    [Fact]
    public void OnlyNewTopics_FirstDayHasNoReview_TheNextDaysDo()
    {
        TopicToPlan[] topics = [new(A, 1, 0, IsNew: true), new(B, 1, 0, IsNew: true)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        Assert.DoesNotContain(plan.Days[0].Blocks, block => block.Kind == BlockKind.Review);
        Assert.Equal(30, plan.Days[0].Blocks.Sum(block => block.Minutes));
        var learnedOnDayOne = plan.Days[0].Blocks.Select(block => block.TopicId).ToHashSet();
        Assert.All(plan.Days.Skip(1), day => Assert.Equal(BlockKind.Review, day.Blocks[0].Kind));
        Assert.Contains(plan.Days[1].Blocks[0].TopicId, learnedOnDayOne);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(10, 2)]
    [InlineData(12, 2)]
    [InlineData(13, 3)]
    [InlineData(30, 6)]
    public void FinalPhase_IsTheLastFifthOfTheStudyDays_AtLeastTwo(int studyDays, int finalPhase)
    {
        Assert.Equal(finalPhase, StudyPlanner.FinalPhaseDays(studyDays));
    }

    // 10 days: the last 2 are the final phase. D is new and learned before it, so it may come back. C is new and too light
    // to get time before it, so it gets none in the final phase either, although minutes would be left for it.
    [Fact]
    public void FinalPhase_HasNoNewTopic()
    {
        var d = new Guid("00000000-0000-0000-0000-00000000000d");
        TopicToPlan[] topics = [new(A, 1, 0), new(B, 1, 50), new(C, 1, 99, IsNew: true), new(d, 10, 0, IsNew: true)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, ExamInTenDays);

        var finalPhase = plan.Days.TakeLast(2).SelectMany(day => day.Blocks).Select(block => block.TopicId).ToHashSet();
        var before = plan.Days.SkipLast(2).SelectMany(day => day.Blocks).Select(block => block.TopicId).ToHashSet();
        Assert.Contains(d, before);
        Assert.DoesNotContain(C, before);
        Assert.DoesNotContain(C, finalPhase);
        Assert.Contains(d, finalPhase);
        Assert.All(plan.Days.TakeLast(2), day => Assert.Equal(30, day.Blocks.Sum(block => block.Minutes)));
    }

    // 3 study days with a new topic: it is learned on day 1, the final phase of 2 days only repeats and deepens.
    [Fact]
    public void ThreeDays_OnlyTheFirstHasANewTopic()
    {
        TopicToPlan[] topics = [new(A, 1, 50), new(B, 1, 0, IsNew: true)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, Today.AddDays(3));

        Assert.Equal(3, plan.Days.Count);
        Assert.Contains(plan.Days[0].Blocks, block => block.TopicId == B && block.Kind == BlockKind.Learn);
        Assert.All(plan.Days, day => Assert.Equal(30, day.Blocks.Sum(block => block.Minutes)));
    }

    // 1 or 2 study days are all final phase: new topics stay out, introduced ones fill the days.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OneOrTwoDays_HaveNoNewTopic(int studyDays)
    {
        TopicToPlan[] topics = [new(A, 1, 50), new(B, 1, 0, IsNew: true)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, Today.AddDays(studyDays));

        Assert.Equal(studyDays, plan.Days.Count);
        Assert.All(plan.Days.SelectMany(day => day.Blocks), block => Assert.Equal(A, block.TopicId));
        Assert.All(plan.Days, day => Assert.Equal(30, day.Blocks.Sum(block => block.Minutes)));
    }

    [Fact]
    public void OneOrTwoDays_WithOnlyNewTopics_GiveAnEmptyPlan()
    {
        Assert.Empty(StudyPlanner.Plan([new(A, 1, 0, IsNew: true)], DailyBudget.Uniform(30), Today, Today.AddDays(2)).Days);
    }

    // A23: topics for more than a week and no exam give seven full study days from today.
    [Fact]
    public void NoExam_PlansSevenStudyDaysFromToday()
    {
        TopicToPlan[] topics = [new(A, 20, 0), new(B, 20, 50), new(C, 20, 20)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, examDay: null);

        Assert.Equal(Enumerable.Range(0, StudyPlanner.RollingDays).Select(Today.AddDays), plan.Days.Select(day => day.StudyDay));
        Assert.All(plan.Days, day => Assert.Equal(30, day.Blocks.Sum(block => block.Minutes)));
        Assert.Equal([A, B, C], MinutesPerTopic(plan, BlockKind.Learn).Keys.Order());
    }

    // A23: when the next study day begins, the old first day is gone and a new seventh one is at the end.
    [Fact]
    public void NoExam_NextDay_DropsTheFirstDayAndAddsASeventh()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 20)];
        var budget = DailyBudget.Uniform(30);

        var today = StudyPlanner.Plan(topics, budget, Today, examDay: null);
        var tomorrow = StudyPlanner.Plan(topics, budget, Today.AddDays(1), examDay: null);

        Assert.Equal(StudyPlanner.RollingDays, tomorrow.Days.Count);
        Assert.Equal(today.Days[1].StudyDay, tomorrow.Days[0].StudyDay);
        Assert.DoesNotContain(tomorrow.Days, day => day.StudyDay == Today);
        Assert.Equal(today.Days[^1].StudyDay.AddDays(1), tomorrow.Days[^1].StudyDay);
    }

    // Nothing to finish towards, so a new topic may come on the last day too, and every day after the first has review.
    [Fact]
    public void NoExam_HasNoFinalPhase()
    {
        TopicToPlan[] topics = [new(A, 1, 50), new(B, 1, 0, IsNew: true), new(C, 1, 0, IsNew: true)];

        var plan = StudyPlanner.Plan(topics, DailyBudget.Uniform(30), Today, examDay: null);

        Assert.Equal(StudyPlanner.RollingDays, plan.Days.Count);
        Assert.All(plan.Days, day => Assert.Equal(BlockKind.Review, day.Blocks[0].Kind));
        Assert.Contains(plan.Days[^1].Blocks, block => block.TopicId != A && block.Kind == BlockKind.Learn);
    }

    // Only new topics still fill the week, with review from the second day on. With an exam in 2 days they would give nothing.
    [Fact]
    public void NoExam_WithOnlyNewTopics_StillPlansTheWeek()
    {
        var plan = StudyPlanner.Plan([new(A, 1, 0, IsNew: true)], DailyBudget.Uniform(30), Today, examDay: null);

        Assert.Equal(StudyPlanner.RollingDays, plan.Days.Count);
        Assert.DoesNotContain(plan.Days[0].Blocks, block => block.Kind == BlockKind.Review);
        Assert.All(plan.Days.Skip(1), day => Assert.Equal(BlockKind.Review, day.Blocks[0].Kind));
    }

    [Fact]
    public void NoExam_SameInput_GivesTheSamePlan()
    {
        TopicToPlan[] topics = [new(A, 2, 50), new(B, 1, 50), new(C, 1, 20, IsNew: true)];
        var budget = DailyBudget.Uniform(40);

        var first = StudyPlanner.Plan(topics, budget, Today, examDay: null);
        var reversed = StudyPlanner.Plan(topics.Reverse().ToArray(), budget, Today, examDay: null);

        Assert.Equal(Flat(first), Flat(reversed));
    }

    // Many random inputs with a fixed seed. A failure names seed and run, so Run(seed + run) reproduces it.
    [Fact]
    public void RandomInputs_KeepAllRules()
    {
        const int Seed = 368;
        for (var run = 0; run < 500; run++)
        {
            Run(Seed + run, $"seed {Seed}, run {run}");
        }
    }

    private static void Run(int seed, string input)
    {
        var random = new Random(seed);
        var steps = random.Next(2) == 0;
        var budget = DailyBudget.Of(Enum.GetValues<DayOfWeek>().ToDictionary(
            day => day,
            _ => steps ? random.Next(1, 145) * 5 : random.Next(DailyBudget.MinMinutes, DailyBudget.MaxMinutes + 1)));
        var today = new DateOnly(2026, 1, 1).AddDays(random.Next(730));
        DateOnly? exam = random.Next(5) == 0 ? null : today.AddDays(random.Next(-3, 121));
        var topics = Enumerable.Range(0, random.Next(13))
            .Select(_ => new TopicToPlan(RandomId(random), random.Next(21), random.Next(101), random.Next(3) == 0))
            .ToArray();
        var weights = topics.ToDictionary(topic => topic.TopicId, topic => topic.Size * (100 - topic.Mastery));
        var isNew = topics.ToDictionary(topic => topic.TopicId, topic => topic.IsNew);

        var plan = StudyPlanner.Plan(topics, budget, today, exam);

        var studyDays = exam is { } examDay ? Math.Max(0, examDay.DayNumber - today.DayNumber) : StudyPlanner.RollingDays;
        var finalPhase = exam is null ? 0 : StudyPlanner.FinalPhaseDays(studyDays);
        var anyIntroduced = topics.Any(topic => weights[topic.TopicId] > 0 && !topic.IsNew);
        if (studyDays == 0 || weights.Values.All(weight => weight == 0) || (!anyIntroduced && finalPhase == studyDays))
        {
            Assert.True(plan.Days.Count == 0, $"{input}: expected an empty plan");
            return;
        }

        Assert.True(plan.Days.Count == studyDays, $"{input}: {plan.Days.Count} days instead of {studyDays}");
        var learned = new HashSet<Guid>();
        for (var i = 0; i < plan.Days.Count; i++)
        {
            var day = plan.Days[i];
            Assert.True(day.StudyDay == today.AddDays(i), $"{input}: day {i} is {day.StudyDay}");
            var minutes = day.Blocks.Sum(block => block.Minutes);
            Assert.True(minutes == budget.MinutesOn(day.StudyDay), $"{input}: {day.StudyDay} has {minutes} minutes");
            Assert.True(
                day.Blocks.All(block => block.Minutes is >= 1 and <= StudyPlanner.BlockMinutes),
                $"{input}: block size on {day.StudyDay}");
            Assert.True(
                !steps || day.Blocks.All(block => block.Minutes % StudyPlanner.Step == 0),
                $"{input}: block not in steps of five on {day.StudyDay}");

            // #369: one review block first on every day with something introduced, for a topic introduced by then.
            var reviews = day.Blocks.Where(block => block.Kind == BlockKind.Review).ToList();
            Assert.True(reviews.Count == (anyIntroduced || i > 0 ? 1 : 0), $"{input}: {reviews.Count} review blocks on {day.StudyDay}");
            Assert.True(reviews.Count == 0 || day.Blocks[0].Kind == BlockKind.Review, $"{input}: review not first on {day.StudyDay}");
            Assert.True(
                reviews.All(block => !isNew[block.TopicId] || learned.Contains(block.TopicId)),
                $"{input}: review of a topic not introduced yet on {day.StudyDay}");

            var learn = day.Blocks.Where(block => block.Kind == BlockKind.Learn).ToList();
            Assert.True(
                i < studyDays - finalPhase || learn.All(block => !isNew[block.TopicId] || learned.Contains(block.TopicId)),
                $"{input}: new topic in the final phase on {day.StudyDay}");
            Assert.True(
                reviews.Count == 0 || learn.All(block => block.Minutes % StudyPlanner.Step == 0 && block.Minutes >= StudyPlanner.Step),
                $"{input}: learn block not in steps of five on {day.StudyDay}");
            learn.ForEach(block => learned.Add(block.TopicId));
        }

        var perTopic = MinutesPerTopic(plan);
        Assert.True(perTopic.Keys.All(id => weights[id] > 0), $"{input}: a topic without weight got time");
        var learnPerTopic = MinutesPerTopic(plan, BlockKind.Learn);
        var ranked = topics.OrderBy(topic => weights[topic.TopicId]).Select(topic => topic.TopicId).ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            for (var j = i + 1; j < ranked.Count; j++)
            {
                var (lighter, heavier) = (ranked[i], ranked[j]);
                Assert.True(
                    weights[lighter] == weights[heavier]
                        || isNew[lighter] != isNew[heavier]
                        || learnPerTopic.GetValueOrDefault(heavier) >= learnPerTopic.GetValueOrDefault(lighter),
                    $"{input}: more weight got fewer minutes");
            }
        }

        var again = StudyPlanner.Plan(topics.Reverse().ToArray(), budget, today, exam);
        Assert.True(Flat(plan).SequenceEqual(Flat(again)), $"{input}: second run differs");
    }

    private static Guid RandomId(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static Dictionary<Guid, int> MinutesPerTopic(StudyPlan plan, BlockKind? kind = null) =>
        plan.Days.SelectMany(day => day.Blocks)
            .Where(block => kind is null || block.Kind == kind)
            .GroupBy(block => block.TopicId)
            .ToDictionary(group => group.Key, group => group.Sum(block => block.Minutes));

    private static List<(DateOnly Day, Guid TopicId, int Minutes, BlockKind Kind)> Flat(StudyPlan plan) =>
        plan.Days.SelectMany(day => day.Blocks.Select(block => (day.StudyDay, block.TopicId, block.Minutes, block.Kind))).ToList();
}