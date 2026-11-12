namespace Edvaniq.Services.Content.Domain;

// The operator's controls for every AI call of Content: the emergency stop and the monthly budget. One row at most,
// none means running without a budget. Belongs to nobody: no user and no document.
public sealed class AiSwitch
{
    public const int TheOne = 1;

    // The share of the budget at which the operator gets a warning.
    public const decimal WarningShare = 0.8m;

    private AiSwitch(DateTime changedAt) => ChangedAt = changedAt;

    public int Id { get; private set; } = TheOne;

    public bool Paused { get; private set; }

    // UTC.
    public DateTime ChangedAt { get; private set; }

    // What the AI may cost in one UTC month, in the provider's billing currency like AiAttempt.Cost. Null is no limit.
    public decimal? MonthlyBudget { get; private set; }

    // The month (its first day, UTC) the warning at WarningShare went out for, and the one the AI was found used up in.
    // Each goes out once a month.
    public DateTime? BudgetWarnedFor { get; private set; }

    public DateTime? BudgetExhaustedFor { get; private set; }

    public static AiSwitch Running(DateTime now) => new(now);

    // The first moment of the UTC month of at. The provider bills by UTC, so a new month starts the sum at 0.
    public static DateTime MonthOf(DateTime at) => new(at.Year, at.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    // Whether this changed anything.
    public bool Set(bool paused, DateTime now)
    {
        if (Paused == paused)
        {
            return false;
        }

        Paused = paused;
        ChangedAt = now;
        return true;
    }

    // Null takes the limit away. Call it only with null or an amount above 0.
    public bool SetBudget(decimal? budget, DateTime now)
    {
        if (MonthlyBudget == budget)
        {
            return false;
        }

        MonthlyBudget = budget;
        ChangedAt = now;
        return true;
    }

    // True the first time in the month of now, the caller warns then.
    public bool MarkWarned(DateTime now) => Mark(BudgetWarnedFor, now, month => BudgetWarnedFor = month);

    public bool MarkExhausted(DateTime now) => Mark(BudgetExhaustedFor, now, month => BudgetExhaustedFor = month);

    private static bool Mark(DateTime? marked, DateTime now, Action<DateTime> mark)
    {
        var month = MonthOf(now);
        if (marked == month)
        {
            return false;
        }

        mark(month);
        return true;
    }
}

// What the AI cost this month against the budget. Spent counts attempts without a price as 0.
public sealed record AiBudgetState(decimal? Budget, decimal Spent)
{
    public bool Exhausted => Budget is { } budget && Spent >= budget;

    public bool Warned => Budget is { } budget && Spent >= budget * AiSwitch.WarningShare;

    // Whole percent of the budget, null without one.
    public int? Percent => Budget is { } budget ? (int)Math.Floor(Spent / budget * 100) : null;
}