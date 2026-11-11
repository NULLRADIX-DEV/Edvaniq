using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

public class DailyBudgetTests
{
    // 15.11.2026 is a Sunday, 16.11.2026 a Monday.
    private static readonly DateOnly Sunday = new(2026, 11, 15);
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void Uniform_GivesTheSameMinutesOnEveryDay()
    {
        var budget = DailyBudget.Uniform(45);

        Assert.All(Enumerable.Range(0, 7), offset => Assert.Equal(45, budget.MinutesOn(Monday.AddDays(offset))));
    }

    [Theory]
    [InlineData(DailyBudget.MinMinutes - 1)]
    [InlineData(DailyBudget.MaxMinutes + 1)]
    public void Uniform_OutsideTheBounds_Throws(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DailyBudget.Uniform(minutes));

    [Theory]
    [InlineData(DailyBudget.MinMinutes)]
    [InlineData(DailyBudget.MaxMinutes)]
    public void Uniform_AtTheBounds_IsAccepted(int minutes) =>
        Assert.Equal(minutes, DailyBudget.Uniform(minutes).MinutesOn(Monday));

    [Fact]
    public void Of_GivesEachWeekdayItsOwnMinutes()
    {
        var budget = DailyBudget.Of(Week(day => 10 + (int)day));

        Assert.Equal(10, budget.MinutesOn(Sunday));
        Assert.Equal(11, budget.MinutesOn(Monday));
        Assert.Equal(16, budget.MinutesOn(Monday.AddDays(5)));
    }

    [Fact]
    public void Of_WithoutAWeekday_Throws()
    {
        var week = Week(_ => 30);
        week.Remove(DayOfWeek.Wednesday);

        var exception = Assert.Throws<ArgumentException>(() => DailyBudget.Of(week));
        Assert.Contains("Wednesday", exception.Message);
    }

    [Theory]
    [InlineData(DailyBudget.MinMinutes - 1)]
    [InlineData(DailyBudget.MaxMinutes + 1)]
    public void Of_WithADayOutsideTheBounds_Throws(int minutes)
    {
        var week = Week(_ => 30);
        week[DayOfWeek.Friday] = minutes;

        Assert.Throws<ArgumentOutOfRangeException>(() => DailyBudget.Of(week));
    }

    [Theory]
    // 03:59 in Berlin (UTC+1 in November) still belongs to Sunday, 04:00 starts Monday.
    [InlineData("2026-11-16T02:59:00Z", DayOfWeek.Sunday)]
    [InlineData("2026-11-16T03:00:00Z", DayOfWeek.Monday)]
    // Summer time, UTC+2.
    [InlineData("2026-07-06T01:59:00Z", DayOfWeek.Sunday)]
    [InlineData("2026-07-06T02:00:00Z", DayOfWeek.Monday)]
    public void MinutesAt_UsesTheWeekdayOfTheStudyDay(string utc, DayOfWeek expected)
    {
        var budget = DailyBudget.Of(Week(day => 10 + (int)day));

        Assert.Equal(10 + (int)expected, budget.MinutesAt(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal)));
    }

    // AK1 and AK2: a user nobody has seen before gets the configured default for every study day.
    [Fact]
    public async Task DefaultDailyBudgets_GiveEveryUserTheConfiguredMinutesOnEveryDay()
    {
        IDailyBudgets budgets = new DefaultDailyBudgets(new DailyBudgetOptions { DefaultMinutes = 25 });

        foreach (var user in new[] { "alice", "bob" })
        {
            var budget = await budgets.ForUserAsync(user, TestContext.Current.CancellationToken);

            Assert.All(Enumerable.Range(0, 7), offset => Assert.Equal(25, budget.MinutesOn(Monday.AddDays(offset))));
        }
    }

    [Fact]
    public void DefaultDailyBudgets_WithMinutesOutsideTheBounds_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DefaultDailyBudgets(new DailyBudgetOptions { DefaultMinutes = 0 }));

    private static Dictionary<DayOfWeek, int> Week(Func<DayOfWeek, int> minutes) =>
        Enum.GetValues<DayOfWeek>().ToDictionary(day => day, minutes);
}