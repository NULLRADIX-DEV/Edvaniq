using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #305: the switch only changes, and only takes a new time, when it really changes.
public sealed class AiSwitchTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Set_ChangesOnlyWhenTheStateDiffers()
    {
        var aiSwitch = AiSwitch.Running(Now);

        Assert.False(aiSwitch.Set(false, Now.AddMinutes(1)));
        Assert.Equal((false, Now), (aiSwitch.Paused, aiSwitch.ChangedAt));
        Assert.True(aiSwitch.Set(true, Now.AddMinutes(2)));
        Assert.False(aiSwitch.Set(true, Now.AddMinutes(3)));
        Assert.Equal((true, Now.AddMinutes(2)), (aiSwitch.Paused, aiSwitch.ChangedAt));
        Assert.True(aiSwitch.Set(false, Now.AddMinutes(4)));
        Assert.Equal(AiSwitch.TheOne, aiSwitch.Id);
    }

    // #303: a month is a UTC month, from its first moment.
    [Theory]
    [InlineData("2026-11-01T00:00:00", "2026-11-01")]
    [InlineData("2026-11-30T23:59:59", "2026-11-01")]
    [InlineData("2026-12-31T23:59:59", "2026-12-01")]
    [InlineData("2027-01-01T00:00:00", "2027-01-01")]
    public void MonthOf_IsTheFirstMomentOfTheUtcMonth(string at, string month) =>
        Assert.Equal(
            DateTime.SpecifyKind(DateTime.Parse(month, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
            AiSwitch.MonthOf(DateTime.SpecifyKind(DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc)));

    // The warning and the used-up budget each go out once a month, and again in the next one.
    [Fact]
    public void Marks_GoOutOncePerMonth()
    {
        var aiSwitch = AiSwitch.Running(Now);

        Assert.True(aiSwitch.MarkWarned(Now));
        Assert.False(aiSwitch.MarkWarned(Now.AddDays(10)));
        Assert.True(aiSwitch.MarkExhausted(Now.AddDays(10)));
        Assert.False(aiSwitch.MarkExhausted(Now.AddDays(11)));
        Assert.True(aiSwitch.MarkWarned(Now.AddMonths(1)));
        Assert.True(aiSwitch.MarkExhausted(Now.AddMonths(1)));
    }

    [Fact]
    public void Budget_ChangesOnlyWhenItDiffers()
    {
        var aiSwitch = AiSwitch.Running(Now);

        Assert.False(aiSwitch.SetBudget(null, Now));
        Assert.True(aiSwitch.SetBudget(25m, Now));
        Assert.False(aiSwitch.SetBudget(25m, Now));
        Assert.True(aiSwitch.SetBudget(null, Now));
    }

    [Theory]
    [InlineData(null, "5", false, false, null)]
    [InlineData("10", "7.99", false, false, 79)]
    [InlineData("10", "8", true, false, 80)]
    [InlineData("10", "9.99", true, false, 99)]
    [InlineData("10", "10", true, true, 100)]
    [InlineData("10", "12.5", true, true, 125)]
    public void State_WarnsAt80Percent_AndIsUsedUpAtTheBudget(string? budget, string spent, bool warned, bool exhausted, int? percent)
    {
        var state = new AiBudgetState(
            budget is null ? null : decimal.Parse(budget, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(spent, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal((warned, exhausted, percent), (state.Warned, state.Exhausted, state.Percent));
    }
}