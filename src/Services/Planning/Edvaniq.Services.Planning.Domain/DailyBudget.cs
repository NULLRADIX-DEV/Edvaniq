using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.Domain;

// The minutes a user wants to learn on each weekday, for all learning goals together (docs/spec/glossary.md#tagesbudget).
// Each user can store one (DailyBudgetSetting), until then the configured default applies on every day.
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

    // The minutes a user entered, null where a day is missing. Errors are keyed by the day's name (Monday …), so each
    // gets its message at its field.
    public static DailyBudget? Parse(IReadOnlyDictionary<DayOfWeek, int?> minutesPerDay, out Dictionary<string, string[]> errors)
    {
        errors = [];
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (minutesPerDay.GetValueOrDefault(day) is not (>= MinMinutes and <= MaxMinutes))
            {
                errors[day.ToString()] = [$"Gib für jeden Tag zwischen {MinMinutes} und {MaxMinutes} Minuten an."];
            }
        }

        return errors.Count > 0 ? null : Of(minutesPerDay.ToDictionary(pair => pair.Key, pair => pair.Value!.Value));
    }

    public int MinutesOn(DayOfWeek day) => minutes[(int)day];

    // The weekday of the study day counts, not the calendar day.
    public int MinutesOn(DateOnly studyDay) => minutes[(int)studyDay.DayOfWeek];

    // So 03:30 on a Monday in Berlin still gets Sunday's minutes.
    public int MinutesAt(DateTime utc) => MinutesOn(StudyDay.Of(utc));
}