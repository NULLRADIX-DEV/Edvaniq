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

    [Fact]
    public void Parse_WithMinutesWithinTheBounds_GivesEachDayItsMinutes()
    {
        var budget = DailyBudget.Parse(Week(day => (int?)(DailyBudget.MinMinutes + (int)day)), out var errors);

        Assert.Empty(errors);
        Assert.NotNull(budget);
        Assert.All(Enum.GetValues<DayOfWeek>(), day => Assert.Equal(DailyBudget.MinMinutes + (int)day, budget.MinutesOn(day)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-5)]
    [InlineData(DailyBudget.MinMinutes - 1)]
    [InlineData(DailyBudget.MaxMinutes + 1)]
    public void Parse_WithAnInvalidDay_NamesThatDayOnly(int? minutes)
    {
        var week = Week(_ => (int?)30);
        week[DayOfWeek.Wednesday] = minutes;

        var budget = DailyBudget.Parse(week, out var errors);

        Assert.Null(budget);
        var error = Assert.Single(errors);
        Assert.Equal("Wednesday", error.Key);
        Assert.Equal(["Gib für jeden Tag zwischen 1 und 720 Minuten an."], error.Value);
    }

    [Fact]
    public void Parse_WithoutDays_NamesEveryDay()
    {
        Assert.Null(DailyBudget.Parse(new Dictionary<DayOfWeek, int?>(), out var errors));
        Assert.Equal(Enum.GetNames<DayOfWeek>().Order(), errors.Keys.Order());
    }

    [Fact]
    public void Setting_KeepsTheMinutesOfEachDay()
    {
        var budget = DailyBudget.Of(Week(day => 10 + (int)day));

        var setting = DailyBudgetSetting.Create("alice", budget, new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc));

        Assert.All(Enum.GetValues<DayOfWeek>(), day => Assert.Equal(10 + (int)day, setting.ToBudget().MinutesOn(day)));
    }

    private static Dictionary<DayOfWeek, T> Week<T>(Func<DayOfWeek, T> minutes) =>
        Enum.GetValues<DayOfWeek>().ToDictionary(day => day, minutes);
}