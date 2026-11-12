using System.Diagnostics;

using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #372: the typical and edge cases of the planner, each by name. Every plan here is also checked for soundness: no
// crash, no block without minutes, no day over its budget (AssertSound). Each runs from the smallest to the largest
// budget, so rounding to steps and the minimum block can't produce negative minutes at either end.
public sealed class PlannerScenarioTests
{
    // Monday 16.11.2026, 11:00 in Berlin.
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(2);

    public static TheoryData<int> Budgets => [DailyBudget.MinMinutes, 7, 30, DailyBudget.MaxMinutes];

    [Theory]
    [MemberData(nameof(Budgets))]
    public void ExamTomorrow_PlansOnlyToday(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        var topics = MixedTopics(5);

        var plan = StudyPlanner.Plan(topics, budget, Today, examDay: Today.AddDays(1));

        AssertSound(plan, budget, topics, examDay: Today.AddDays(1));
        Assert.Equal([Today], plan.Days.Select(day => day.StudyDay));
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void ExamToday_OrAlreadyPast_GivesAnEmptyPlan(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        var topics = MixedTopics(5);

        Assert.Empty(StudyPlanner.Plan(topics, budget, Today, examDay: Today).Days);
        Assert.Empty(StudyPlanner.Plan(topics, budget, Today, examDay: Today.AddDays(-1)).Days);
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void NoTopics_GiveAnEmptyPlan_WithTheNoTopicsHint(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);

        Assert.Empty(StudyPlanner.Plan([], budget, Today, examDay: Today.AddDays(10)).Days);
        Assert.Empty(StudyPlanner.Plan([], budget, Today, examDay: null).Days);
        var today = TodayPlanner.Plan([], new Dictionary<Guid, TopicStanding>(), budget, Now);
        Assert.Empty(today.Blocks);
        Assert.Equal(TodayPlanEmpty.NoTopics, today.Empty);
    }

    // A topic without questions (#374) can't be practised: no block, named in NotReady. The others are planned.
    [Theory]
    [MemberData(nameof(Budgets))]
    public void TopicWithoutQuestions_GetsNoBlock_TheOthersAreStillPlanned(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        var (withQuestions, withoutQuestions) = (Id(1), Id(2));
        var standings = new Dictionary<Guid, TopicStanding>
        {
            [withQuestions] = new(40, TopicState.Building),
            [withoutQuestions] = new(10, TopicState.Weakness),
        };

        var plan = TodayPlanner.Plan([withQuestions, withoutQuestions], standings, budget, Now, readyTopics: new HashSet<Guid> { withQuestions });

        AssertSound(plan, budget, [withQuestions]);
        Assert.Equal([withoutQuestions], plan.NotReady);
        Assert.NotEmpty(plan.Blocks);
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void NoTopicWithQuestions_GivesAnEmptyPlan_WithTheQuestionsPendingHint(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        var standings = new Dictionary<Guid, TopicStanding> { [Id(1)] = new(40, TopicState.Building) };

        var plan = TodayPlanner.Plan([Id(1), Id(2)], standings, budget, Now, readyTopics: new HashSet<Guid>());

        Assert.Empty(plan.Blocks);
        Assert.Equal(TodayPlanEmpty.QuestionsPending, plan.Empty);
        Assert.Equal([Id(1), Id(2)], plan.NotReady);
    }

    // Secure topics still have a gap to 100, so they keep getting time and none is shortened when the time is enough.
    [Theory]
    [MemberData(nameof(Budgets))]
    public void AllTopicsSecure_StillGetTime_WithAnExamAndWithout(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        List<TopicToPlan> topics = [.. Enumerable.Range(1, 4).Select(i => new TopicToPlan(Id(i), 1, 80 + 5 * i))];

        foreach (var examDay in new DateOnly?[] { Today.AddDays(30), null })
        {
            var plan = StudyPlanner.Plan(topics, budget, Today, examDay);

            AssertSound(plan, budget, topics, examDay);
            Assert.NotEmpty(plan.Days);
        }

        var standings = topics.ToDictionary(topic => topic.TopicId, topic => new TopicStanding(topic.Mastery, TopicState.Secure));
        var today = TodayPlanner.Plan([.. standings.Keys], standings, budget, Now);
        AssertSound(today, budget, standings.Keys);
        Assert.NotEmpty(today.Blocks);
    }

    // Secure all the way at 100: nothing left to learn.
    [Theory]
    [MemberData(nameof(Budgets))]
    public void AllTopicsFullyMastered_GiveAnEmptyPlan_WithTheAllMasteredHint(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        var standings = Enumerable.Range(1, 4).ToDictionary(Id, _ => new TopicStanding(100, TopicState.Secure));

        Assert.Empty(StudyPlanner.Plan([.. standings.Keys.Select(id => new TopicToPlan(id, 1, 100))], budget, Today, Today.AddDays(30)).Days);
        var today = TodayPlanner.Plan([.. standings.Keys], standings, budget, Now);
        Assert.Empty(today.Blocks);
        Assert.Equal(TodayPlanEmpty.AllMastered, today.Empty);
    }

    // 500 topics of every kind for a year, with a different budget on each weekday from 1 to 720 minutes.
    [Fact]
    public void VeryLargeLearningGoal_AYearAhead_IsSoundAndFast()
    {
        var budget = WeekFromOneTo720();
        var topics = MixedTopics(500);

        var plan = Timed(() => StudyPlanner.Plan(topics, budget, Today, Today.AddDays(365)), "500 topics, 365 days");

        AssertSound(plan, budget, topics, Today.AddDays(365));
        Assert.Equal(365, plan.Days.Count);
        Assert.Equal(plan.Days, StudyPlanner.Plan(topics, budget, Today, Today.AddDays(365)).Days, DayPlanComparer);
    }

    // The same 500 topics in a week: far too short, so the path through shortening. No weakness is ever shortened.
    [Fact]
    public void VeryLargeLearningGoal_AWeekAhead_ShortensButKeepsEveryWeakness()
    {
        var budget = WeekFromOneTo720();
        var topics = MixedTopics(500);

        var plan = Timed(() => StudyPlanner.Plan(topics, budget, Today, Today.AddDays(7)), "500 topics, 7 days");

        AssertSound(plan, budget, topics, Today.AddDays(7));
        Assert.NotEmpty(plan.Shortened);
        Assert.Contains(topics, topic => topic.IsWeakness);
        Assert.Contains(topics, topic => topic.IsFoundation);
    }

    // Today's plan of the same goal without an exam, through TodayPlanner as /plan/today asks for it.
    [Fact]
    public void VeryLargeLearningGoal_Today_IsSoundAndFast()
    {
        var budget = WeekFromOneTo720();
        var topics = MixedTopics(500);
        var standings = topics.ToDictionary(topic => topic.TopicId, Standing);
        var ready = topics.Where((_, i) => i % 10 != 0).Select(topic => topic.TopicId).ToHashSet();
        var foundations = topics.Where(topic => topic.IsFoundation).Select(topic => topic.TopicId).ToHashSet();

        var plan = Timed(
            () => TodayPlanner.Plan([.. standings.Keys], standings, budget, Now, ready, foundations), "500 topics, today");

        AssertSound(plan, budget, ready);
        Assert.Equal(50, plan.NotReady.Count);
        Assert.DoesNotContain(plan.Shortened, id => standings[id].State == TopicState.Weakness);
    }

    // #378, A46: after a session that lifted the weakness A, the rest of the plan gives A strictly less time over the
    // week, the time goes to the others, and today's block that was done stays done.
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void HigherMasteryAfterASession_GivesTheTopicLessTimeInTheRestOfThePlan(int minutes)
    {
        var budget = DailyBudget.Uniform(minutes);
        var (a, b, c) = (Id(1), Id(2), Id(3));
        List<TopicToPlan> Topics(int masteryOfA, bool weakness) =>
        [
            new(a, TodayPlanner.DefaultSize, masteryOfA, IsNew: false, IsWeakness: weakness),
            new(b, TodayPlanner.DefaultSize, 50, IsNew: false),
            new(c, TodayPlanner.DefaultSize, 50, IsNew: false),
        ];

        var before = StudyPlanner.Plan(Topics(20, weakness: true), budget, Today, examDay: null);
        var after = StudyPlanner.Plan(Topics(70, weakness: false), budget, Today, examDay: null);

        AssertSound(after, budget, Topics(70, weakness: false), examDay: null);
        Assert.True(
            WeekMinutes(after, a) < WeekMinutes(before, a),
            $"A has {WeekMinutes(after, a)} minutes after the session, {WeekMinutes(before, a)} before.");
        Assert.True(WeekMinutes(after, b) + WeekMinutes(after, c) > WeekMinutes(before, b) + WeekMinutes(before, c));

        // The block of the session, done today, stays done whatever the new plan does with the rest of the day.
        var done = DayBlocks.WithIds("alice", Guid.Empty, Today, before.Days[0].Blocks)[0];
        var stored = DoneBlock.Create(done.Id, "alice", Guid.Empty, Today, done.Block, Now);
        var today = DayBlocks.WithDone("alice", Guid.Empty, Today, after.Days[0].Blocks, [stored], budget.MinutesOn(Today));
        Assert.Contains(today, block => block.Id == done.Id && block.Done && block.Block == done.Block);
    }

    private static int WeekMinutes(StudyPlan plan, Guid topic) =>
        plan.Days.SelectMany(day => day.Blocks).Where(block => block.TopicId == topic).Sum(block => block.Minutes);

    // No crash, no block without minutes, no day over its budget, review only once and first, days ascending within the
    // plan, blocks only for the topics given, and no weakness among the shortened topics.
    private static void AssertSound(StudyPlan plan, DailyBudget budget, IReadOnlyCollection<TopicToPlan> topics, DateOnly? examDay)
    {
        var ids = topics.Select(topic => topic.TopicId).ToHashSet();
        Assert.Equal(plan.Days.Select(day => day.StudyDay).Order(), plan.Days.Select(day => day.StudyDay));
        Assert.Equal(plan.Days.Count, plan.Days.Select(day => day.StudyDay).Distinct().Count());
        foreach (var day in plan.Days)
        {
            Assert.True(day.StudyDay >= Today, $"{day.StudyDay} lies before today.");
            Assert.True(examDay is null || day.StudyDay < examDay, $"{day.StudyDay} lies on or after the exam.");
            AssertSoundDay(day.StudyDay, day.Blocks, budget, ids);
        }

        Assert.All(plan.Shortened, id => Assert.Contains(id, ids));
        var weaknesses = topics.Where(topic => topic.IsWeakness).Select(topic => topic.TopicId).ToHashSet();
        Assert.DoesNotContain(plan.Shortened, weaknesses.Contains);
    }

    private static void AssertSound(TodayPlan plan, DailyBudget budget, IEnumerable<Guid> plannable)
    {
        Assert.Equal(Today, plan.StudyDay);
        AssertSoundDay(plan.StudyDay, plan.Blocks, budget, plannable.ToHashSet());
        Assert.Equal(plan.Blocks.Count == 0, plan.Empty is not null);
    }

    private static void AssertSoundDay(DateOnly day, IReadOnlyList<Block> blocks, DailyBudget budget, HashSet<Guid> topics)
    {
        Assert.All(blocks, block =>
        {
            Assert.True(block.Minutes > 0, $"A block of {block.Minutes} minutes on {day}.");
            Assert.Contains(block.TopicId, topics);
        });
        Assert.True(
            blocks.Sum(block => block.Minutes) <= budget.MinutesOn(day),
            $"{blocks.Sum(block => block.Minutes)} minutes on {day}, budget {budget.MinutesOn(day)}.");
        var reviews = blocks.Select((block, index) => (block, index)).Where(item => item.block.Kind == BlockKind.Review).ToList();
        Assert.True(reviews.Count <= 1, $"{reviews.Count} review blocks on {day}.");
        Assert.All(reviews, review => Assert.Equal(0, review.index));
    }

    private static T Timed<T>(Func<T> plan, string what)
    {
        var watch = Stopwatch.StartNew();
        var result = plan();
        watch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{what}: {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(watch.Elapsed < TimeLimit, $"{what} took {watch.Elapsed.TotalMilliseconds:F0} ms, limit {TimeLimit.TotalMilliseconds} ms.");
        return result;
    }

    private static DailyBudget WeekFromOneTo720() =>
        DailyBudget.Of(new Dictionary<DayOfWeek, int>
        {
            [DayOfWeek.Monday] = DailyBudget.MinMinutes,
            [DayOfWeek.Tuesday] = 7,
            [DayOfWeek.Wednesday] = 15,
            [DayOfWeek.Thursday] = 30,
            [DayOfWeek.Friday] = 45,
            [DayOfWeek.Saturday] = DailyBudget.MaxMinutes,
            [DayOfWeek.Sunday] = 90,
        });

    // Every kind of topic in a fixed mix: sizes 0 to 10, mastery 0 to 100, new ones, weaknesses and foundations.
    private static List<TopicToPlan> MixedTopics(int count)
    {
        var random = new Random(372);
        return
        [
            .. Enumerable.Range(1, count).Select(i =>
            {
                var mastery = random.Next(0, 101);
                var isNew = random.Next(5) == 0;
                return new TopicToPlan(
                    Id(i),
                    Size: random.Next(0, 11),
                    Mastery: mastery,
                    IsNew: isNew,
                    IsWeakness: !isNew && (mastery < 50 || random.Next(10) == 0),
                    IsFoundation: random.Next(10) == 0);
            }),
        ];
    }

    private static TopicStanding Standing(TopicToPlan topic) =>
        new(topic.Mastery, topic.IsNew ? TopicState.New
            : topic.IsWeakness ? TopicState.Weakness
            : topic.Mastery >= 80 ? TopicState.Secure
            : TopicState.Building);

    private static Guid Id(int number) => new($"00000000-0000-0000-0000-{number:D12}");

    private static readonly IEqualityComparer<DayPlan> DayPlanComparer = EqualityComparer<DayPlan>.Create(
        (left, right) => left!.StudyDay == right!.StudyDay && left.Blocks.SequenceEqual(right.Blocks),
        day => day.StudyDay.GetHashCode());
}