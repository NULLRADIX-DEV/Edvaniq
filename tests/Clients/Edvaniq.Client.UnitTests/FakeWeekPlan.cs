using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers with its queue in order and repeats the last answer after that.
internal sealed class FakeWeekPlan : IWeekPlan
{
    private readonly Queue<CallOutcome<WeekPlan>> answers = new();

    public int Calls { get; private set; }

    public FakeWeekPlan Answer(WeekPlan plan) => Answer(CallOutcome<WeekPlan>.Ok(plan));

    public FakeWeekPlan Answer(CallOutcome<WeekPlan> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<WeekPlan>> GetAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        var next = answers.Count > 1 ? answers.Dequeue() : answers.Count == 1 ? answers.Peek() : CallOutcome<WeekPlan>.Failed();
        return Task.FromResult(next);
    }
}