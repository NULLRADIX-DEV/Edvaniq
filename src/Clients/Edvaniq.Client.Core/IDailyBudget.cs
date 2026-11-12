namespace Edvaniq.Client.Core;

// The user's daily budget in Planning: the minutes per weekday for all learning goals together
// (docs/spec/glossary.md#tagesbudget). The page sets one value for every day.
public interface IDailyBudget
{
    Task<CallOutcome<DailyBudgetInfo>> GetAsync(CancellationToken cancellationToken = default);

    // The same minutes for all seven days. Rejected carries Planning's message.
    Task<CallOutcome<DailyBudgetInfo>> SetAsync(int minutes, CancellationToken cancellationToken = default);
}

// Minutes indexed by DayOfWeek. IsDefault: the user never stored one, the minutes are the configured default.
public sealed record DailyBudgetInfo(IReadOnlyList<int> Minutes, bool IsDefault)
{
    public bool IsUniform => Minutes.Distinct().Count() == 1;

    // The value that holds on most days, the smaller one on a tie. For a week the user set day by day elsewhere.
    public int Usual => Minutes
        .GroupBy(minutes => minutes)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key)
        .First().Key;
}

// The limits and the message of DailyBudget in Planning. The client can't reference Planning, so DailyBudgetValuesTests
// in the architecture tests holds both sides together.
public static class DailyBudgetValues
{
    public const int MinMinutes = 1;

    public const int MaxMinutes = 720;

    public const string OutOfRange = "Gib für jeden Tag zwischen 1 und 720 Minuten an.";

    // The weekdays as Planning names them in its JSON, in the order of DayOfWeek.
    public static readonly IReadOnlyList<string> Days = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
}