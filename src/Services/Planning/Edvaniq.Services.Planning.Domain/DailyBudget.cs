using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.Domain;

// The minutes a user wants to learn on each weekday, for all learning goals together (docs/spec/glossary.md#tagesbudget).
// The model knows a value per weekday, until M5 every user gets the same value for all days.
public sealed class DailyBudget
{
    public const int MinMinutes = 1;

    // Twelve hours. Anything above is no study plan.
    public const int MaxMinutes = 720;

    private readonly int[] minutes;

    private DailyBudget(int[] minutes) => this.minutes = minutes;

    public static DailyBudget Uniform(int minutes) => Of(Enum.GetValues<DayOfWeek>().ToDictionary(day => day, _ => minutes));

    public static DailyBudget Of(IReadOnlyDictionary<DayOfWeek, int> minutesPerDay)
    {
        var minutes = new int[7];
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (!minutesPerDay.TryGetValue(day, out var value))
            {
                throw new ArgumentException($"The daily budget has no minutes for {day}.", nameof(minutesPerDay));
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinMinutes, nameof(minutesPerDay));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxMinutes, nameof(minutesPerDay));
            minutes[(int)day] = value;
        }

        return new DailyBudget(minutes);
    }

    // The weekday of the study day counts, not the calendar day.
    public int MinutesOn(DateOnly studyDay) => minutes[(int)studyDay.DayOfWeek];

    // So 03:30 on a Monday in Berlin still gets Sunday's minutes.
    public int MinutesAt(DateTime utc) => MinutesOn(StudyDay.Of(utc));
}