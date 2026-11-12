using Edvaniq.Client.Core;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.ArchitectureTests;

// #512: the settings page checks with Planning's limits and words and names the days as Planning does. The client can't
// reference Planning, so a change on one side turns this red.
public class DailyBudgetValuesTests
{
    [Fact]
    public void Limits_AreThoseOfPlanning()
    {
        Assert.Equal(DailyBudget.MinMinutes, DailyBudgetValues.MinMinutes);
        Assert.Equal(DailyBudget.MaxMinutes, DailyBudgetValues.MaxMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public void Message_IsPlannings(int minutes)
    {
        var week = Enum.GetValues<DayOfWeek>().ToDictionary(day => day, _ => (int?)minutes);

        Assert.Null(DailyBudget.Parse(week, out var errors));

        Assert.All(errors.Values, texts => Assert.Equal([DailyBudgetValues.OutOfRange], texts));
    }

    [Fact]
    public void Days_AreThoseOfPlanning() =>
        Assert.Equal(Enum.GetNames<DayOfWeek>().Select(day => day.ToLowerInvariant()), DailyBudgetValues.Days);
}