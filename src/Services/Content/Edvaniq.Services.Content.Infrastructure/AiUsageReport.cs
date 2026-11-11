using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// The AI usage of all users for the cost report (M6), per UTC day, per user and per document. A day adds the attempts
// of existing users to the sums of deleted ones, user and document only know existing users. Days are UTC days, as the
// provider bills them, not study days. Grouped in memory: the MySQL provider doesn't translate every date function,
// and a range of days is small.
public sealed class AiUsageReport(ContentDbContext db)
{
    public async Task<IReadOnlyDictionary<DateOnly, AiUsageTotal>> PerDayAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var attempts = await AttemptsAsync(from, to, cancellationToken);
        var (start, end) = RangeOf(from, to);
        var archived = await db.AiUsageDays.Where(sum => sum.Day >= start && sum.Day < end).ToListAsync(cancellationToken);
        return attempts.GroupBy(AiUsageDay.DayOf).Select(day => (Key: DateOnly.FromDateTime(day.Key), Total: AiUsageTotal.Of(day)))
            .Concat(archived.Select(sum => (Key: DateOnly.FromDateTime(sum.Day), Total: AiUsageTotal.Of(sum))))
            .GroupBy(day => day.Key)
            .ToDictionary(day => day.Key, day => day.Select(part => part.Total).Aggregate(AiUsageTotal.Plus));
    }

    public async Task<IReadOnlyDictionary<string, AiUsageTotal>> PerUserAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        (await AttemptsAsync(from, to, cancellationToken))
            .GroupBy(attempt => attempt.OwnerId)
            .ToDictionary(user => user.Key, AiUsageTotal.Of);

    public async Task<IReadOnlyDictionary<Guid, AiUsageTotal>> PerDocumentAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        (await AttemptsAsync(from, to, cancellationToken))
            .GroupBy(attempt => attempt.DocumentId)
            .ToDictionary(document => document.Key, AiUsageTotal.Of);

    private Task<List<AiAttempt>> AttemptsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (start, end) = RangeOf(from, to);
        return db.AiAttempts.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(attempt => attempt.CalledAt >= start && attempt.CalledAt < end)
            .ToListAsync(cancellationToken);
    }

    // From midnight UTC of the first day to midnight after the last.
    private static (DateTime Start, DateTime End) RangeOf(DateOnly from, DateOnly to) =>
        (from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
}

// Costs in the provider's billing currency, UnpricedAttempts had none.
public sealed record AiUsageTotal(int Calls, int Attempts, long InputTokens, long OutputTokens, decimal Cost, int UnpricedAttempts, long DurationMs)
{
    public static AiUsageTotal Of(IEnumerable<AiAttempt> attempts)
    {
        var list = attempts.ToList();
        return new AiUsageTotal(
            list.Count(attempt => attempt.Attempt == 1), list.Count, list.Sum(attempt => attempt.InputTokens),
            list.Sum(attempt => attempt.OutputTokens), list.Sum(attempt => attempt.Cost ?? 0), list.Count(attempt => attempt.Cost is null),
            list.Sum(attempt => attempt.DurationMs));
    }

    public static AiUsageTotal Of(AiUsageDay sum) =>
        new(sum.Calls, sum.Attempts, sum.InputTokens, sum.OutputTokens, sum.Cost, sum.UnpricedAttempts, sum.DurationMs);

    public static AiUsageTotal Plus(AiUsageTotal left, AiUsageTotal right) =>
        new(left.Calls + right.Calls, left.Attempts + right.Attempts, left.InputTokens + right.InputTokens,
            left.OutputTokens + right.OutputTokens, left.Cost + right.Cost, left.UnpricedAttempts + right.UnpricedAttempts,
            left.DurationMs + right.DurationMs);
}