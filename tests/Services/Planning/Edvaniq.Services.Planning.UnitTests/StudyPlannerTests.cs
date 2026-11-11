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

        // Weights 100, 50 and 100 of 250.
        var minutes = MinutesPerTopic(plan);
        Assert.Equal(120, minutes[A]);
        Assert.Equal(60, minutes[B]);
        Assert.Equal(120, minutes[C]);
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

        Assert.All(plan.Days, day => Assert.Equal([A, B], day.Blocks.Select(block => block.TopicId)));
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
        var exam = today.AddDays(random.Next(-3, 121));
        var topics = Enumerable.Range(0, random.Next(13))
            .Select(_ => new TopicToPlan(RandomId(random), random.Next(21), random.Next(101)))
            .ToArray();
        var weights = topics.ToDictionary(topic => topic.TopicId, topic => topic.Size * (100 - topic.Mastery));

        var plan = StudyPlanner.Plan(topics, budget, today, exam);

        var studyDays = Math.Max(0, exam.DayNumber - today.DayNumber);
        if (studyDays == 0 || weights.Values.All(weight => weight == 0))
        {
            Assert.True(plan.Days.Count == 0, $"{input}: expected an empty plan");
            return;
        }

        Assert.True(plan.Days.Count == studyDays, $"{input}: {plan.Days.Count} days instead of {studyDays}");
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
        }

        var perTopic = MinutesPerTopic(plan);
        Assert.True(perTopic.Keys.All(id => weights[id] > 0), $"{input}: a topic without weight got time");
        var ranked = topics.OrderBy(topic => weights[topic.TopicId]).Select(topic => topic.TopicId).ToList();
        for (var i = 1; i < ranked.Count; i++)
        {
            var lighter = ranked[i - 1];
            var heavier = ranked[i];
            Assert.True(
                weights[lighter] == weights[heavier] || perTopic.GetValueOrDefault(heavier) >= perTopic.GetValueOrDefault(lighter),
                $"{input}: more weight got fewer minutes");
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

    private static Dictionary<Guid, int> MinutesPerTopic(StudyPlan plan) =>
        plan.Days.SelectMany(day => day.Blocks)
            .GroupBy(block => block.TopicId)
            .ToDictionary(group => group.Key, group => group.Sum(block => block.Minutes));

    private static List<(DateOnly Day, Guid TopicId, int Minutes)> Flat(StudyPlan plan) =>
        plan.Days.SelectMany(day => day.Blocks.Select(block => (day.StudyDay, block.TopicId, block.Minutes))).ToList();
}