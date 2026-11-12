using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers each kind of call with its queue in order and repeats the last answer after that. Remembers what it was sent.
internal sealed class FakeDailyBudget : IDailyBudget
{
    private readonly Queue<CallOutcome<DailyBudgetInfo>> gets = new();
    private readonly Queue<CallOutcome<DailyBudgetInfo>> sets = new();

    public List<int> Set { get; } = [];

    public int Gets { get; private set; }

    public static DailyBudgetInfo Week(params int[] minutes) =>
        new(minutes.Length == 1 ? [.. Enumerable.Repeat(minutes[0], 7)] : minutes, IsDefault: false);

    public FakeDailyBudget Get(CallOutcome<DailyBudgetInfo> outcome)
    {
        gets.Enqueue(outcome);
        return this;
    }

    public FakeDailyBudget Answer(CallOutcome<DailyBudgetInfo> outcome)
    {
        sets.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<DailyBudgetInfo>> GetAsync(CancellationToken cancellationToken = default)
    {
        Gets++;
        return Task.FromResult(Next(gets, CallOutcome<DailyBudgetInfo>.Ok(Week(60))));
    }

    // The week as stored, unless the test queued an answer.
    public Task<CallOutcome<DailyBudgetInfo>> SetAsync(int minutes, CancellationToken cancellationToken = default)
    {
        Set.Add(minutes);
        return Task.FromResult(Next(sets, CallOutcome<DailyBudgetInfo>.Ok(Week(minutes))));
    }

    private static T Next<T>(Queue<T> queue, T otherwise) =>
        queue.Count > 1 ? queue.Dequeue() : queue.Count == 1 ? queue.Peek() : otherwise;
}