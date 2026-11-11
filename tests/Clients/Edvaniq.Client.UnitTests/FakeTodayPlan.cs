using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers with its queue in order and repeats the last answer after that.
internal sealed class FakeTodayPlan : ITodayPlan
{
    private readonly Queue<CallOutcome<TodayPlan>> answers = new();

    public int Calls { get; private set; }

    public FakeTodayPlan Answer(TodayPlan plan) => Answer(CallOutcome<TodayPlan>.Ok(plan));

    public FakeTodayPlan Answer(CallOutcome<TodayPlan> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<TodayPlan>> GetAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        var next = answers.Count > 1 ? answers.Dequeue() : answers.Count == 1 ? answers.Peek() : CallOutcome<TodayPlan>.Failed();
        return Task.FromResult(next);
    }
}