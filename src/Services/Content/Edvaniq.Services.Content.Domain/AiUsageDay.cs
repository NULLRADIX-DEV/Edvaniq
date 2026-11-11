namespace Edvaniq.Services.Content.Domain;

// What the AI cost on one UTC day for one purpose, model and prompt version, from attempts of users that are deleted.
// Belongs to nobody: no user, no document, no time of day, so the cost report keeps the sums of deleted users without
// anything that leads back to them. The attempts of users that still exist stay in AiAttempt, a report adds both.
public sealed class AiUsageDay
{
    private AiUsageDay(DateTime day, string purpose, string model, string promptVersion)
    {
        Day = day;
        Purpose = purpose;
        Model = model;
        PromptVersion = promptVersion;
    }

    // Midnight UTC of the day.
    public DateTime Day { get; private set; }

    public string Purpose { get; private set; }

    public string Model { get; private set; }

    public string PromptVersion { get; private set; }

    // Counted on the day of their first attempt.
    public int Calls { get; private set; }

    public int Attempts { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    // The known costs, UnpricedAttempts says how many had none.
    public decimal Cost { get; private set; }

    public int UnpricedAttempts { get; private set; }

    public long DurationMs { get; private set; }

    public static DateTime DayOf(AiAttempt attempt) => DateTime.SpecifyKind(attempt.CalledAt.Date, DateTimeKind.Utc);

    public static AiUsageDay For(AiAttempt attempt) => new(DayOf(attempt), attempt.Purpose, attempt.Model, attempt.PromptVersion);

    public bool Holds(AiAttempt attempt) =>
        Day == DayOf(attempt) && Purpose == attempt.Purpose && Model == attempt.Model && PromptVersion == attempt.PromptVersion;

    public void Add(AiAttempt attempt)
    {
        if (!Holds(attempt))
        {
            throw new InvalidOperationException("The attempt belongs to another day, purpose, model or prompt version.");
        }

        Calls += attempt.Attempt == 1 ? 1 : 0;
        Attempts++;
        InputTokens += attempt.InputTokens;
        OutputTokens += attempt.OutputTokens;
        Cost += attempt.Cost ?? 0;
        UnpricedAttempts += attempt.Cost is null ? 1 : 0;
        DurationMs += attempt.DurationMs;
    }
}