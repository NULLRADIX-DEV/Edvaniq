using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #379: the week is the rolling plan from today on, today's plan is its first day, and the list stops before the exam.
public class WeekPlanTests
{
    // Monday 16.11.2026, 11:00 in Berlin.
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");

    public static TheoryData<string> Scenarios => ["introduced", "new", "shortened", "notReady", "noMastery", "noTopics", "allPending", "allMastered"];

    // One way to plan: day 0 of the week is today's plan in every case, blocks, empty reason and the lists.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void DayZero_IsTodaysPlan(string scenario)
    {
        var (topics, standings, budget, ready) = Input(scenario);

        var week = TodayPlanner.Week(topics, standings, budget, Now, readyTopics: ready);
        var today = TodayPlanner.Plan(topics, standings, budget, Now, readyTopics: ready);

        Assert.Equal(today.StudyDay, week.Days[0].StudyDay);
        Assert.Equal(today.Blocks, week.Days[0].Blocks);
        Assert.Equal(today.Empty, week.Today.Empty);
        Assert.Equal(today.Shortened, week.Shortened);
        Assert.Equal(today.NotReady, week.NotReady);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Week_HasSevenDaysFromToday(string scenario)
    {
        var (topics, standings, budget, ready) = Input(scenario);

        var week = TodayPlanner.Week(topics, standings, budget, Now, readyTopics: ready);

        Assert.Equal(Enumerable.Range(0, StudyPlanner.RollingDays).Select(Monday.AddDays), week.Days.Select(day => day.StudyDay));
    }

    [Fact]
    public void WithoutTopics_TheWeekSaysWhy()
    {
        var week = TodayPlanner.Week([], new Dictionary<Guid, TopicStanding>(), DailyBudget.Uniform(30), Now);

        Assert.Equal(TodayPlanEmpty.NoTopics, week.Empty);
        Assert.All(week.Days, day => Assert.Empty(day.Blocks));
    }

    // Manager's cases: exam in 3 days → 3 days, none → 7, today or past → only today, far away → 7.
    [Theory]
    [InlineData(null, 7)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(7, 7)]
    [InlineData(30, 7)]
    public void DaysShown_StopBeforeTheExamDay(int? examInDays, int expected) =>
        Assert.Equal(expected, TodayPlanner.DaysShown(Monday, examInDays is { } days ? Monday.AddDays(days) : null));

    private static (List<Guid> Topics, Dictionary<Guid, TopicStanding> Standings, DailyBudget Budget, HashSet<Guid>? Ready) Input(string scenario) =>
        scenario switch
        {
            "introduced" => ([A, B, C], Introduced(new() { [A] = 80, [B] = 20, [C] = 0 }), DailyBudget.Uniform(30), null),
            "new" => ([A, B], [], DailyBudget.Uniform(45), null),
            "shortened" => ([A, B, C], Introduced(new() { [A] = 20, [B] = 30, [C] = 90 }), DailyBudget.Uniform(10), null),
            "notReady" => ([A, B, C], Introduced(new() { [A] = 20, [B] = 30, [C] = 60 }), DailyBudget.Uniform(30), [A, C]),
            "noMastery" => ([A, B], [], DailyBudget.Uniform(30), null),
            "noTopics" => ([], [], DailyBudget.Uniform(30), null),
            "allPending" => ([A, B], Introduced(new() { [A] = 20 }), DailyBudget.Uniform(30), []),
            "allMastered" => ([A, B], Introduced(new() { [A] = 100, [B] = 100 }), DailyBudget.Uniform(30), null),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static Dictionary<Guid, TopicStanding> Introduced(Dictionary<Guid, int> masteries) =>
        masteries.ToDictionary(entry => entry.Key, entry => new TopicStanding(entry.Value, TopicState.Building));
}