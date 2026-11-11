using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// The daily budget a user stored, one row per user. Seven columns, one per weekday, so the row reads as a week.
public sealed class DailyBudgetSetting : IOwnedByUser
{
    private DailyBudgetSetting(string ownerId) => OwnerId = ownerId;

    public string OwnerId { get; private set; }

    public int MondayMinutes { get; private set; }

    public int TuesdayMinutes { get; private set; }

    public int WednesdayMinutes { get; private set; }

    public int ThursdayMinutes { get; private set; }

    public int FridayMinutes { get; private set; }

    public int SaturdayMinutes { get; private set; }

    public int SundayMinutes { get; private set; }

    // UTC.
    public DateTime UpdatedAt { get; private set; }

    public static DailyBudgetSetting Create(string ownerId, DailyBudget budget, DateTime updatedAt)
    {
        var setting = new DailyBudgetSetting(ownerId);
        setting.Change(budget, updatedAt);
        return setting;
    }

    public void Change(DailyBudget budget, DateTime updatedAt)
    {
        MondayMinutes = budget.MinutesOn(DayOfWeek.Monday);
        TuesdayMinutes = budget.MinutesOn(DayOfWeek.Tuesday);
        WednesdayMinutes = budget.MinutesOn(DayOfWeek.Wednesday);
        ThursdayMinutes = budget.MinutesOn(DayOfWeek.Thursday);
        FridayMinutes = budget.MinutesOn(DayOfWeek.Friday);
        SaturdayMinutes = budget.MinutesOn(DayOfWeek.Saturday);
        SundayMinutes = budget.MinutesOn(DayOfWeek.Sunday);
        UpdatedAt = updatedAt;
    }

    public DailyBudget ToBudget() =>
        DailyBudget.Of(new Dictionary<DayOfWeek, int>
        {
            [DayOfWeek.Monday] = MondayMinutes,
            [DayOfWeek.Tuesday] = TuesdayMinutes,
            [DayOfWeek.Wednesday] = WednesdayMinutes,
            [DayOfWeek.Thursday] = ThursdayMinutes,
            [DayOfWeek.Friday] = FridayMinutes,
            [DayOfWeek.Saturday] = SaturdayMinutes,
            [DayOfWeek.Sunday] = SundayMinutes,
        });
}