using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers with its queue in order and repeats the last answer after that.
internal sealed class FakeMaterialQuota : IMaterialQuota
{
    private readonly Queue<CallOutcome<MaterialQuota>> answers = new();

    public int Calls { get; private set; }

    public FakeMaterialQuota Answer(int pagesLeft, int pagesPerDay = 200) =>
        Answer(CallOutcome<MaterialQuota>.Ok(new MaterialQuota(pagesLeft, pagesPerDay, 20, 20, new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc))));

    public FakeMaterialQuota Answer(CallOutcome<MaterialQuota> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<MaterialQuota>> GetAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        var next = answers.Count > 1 ? answers.Dequeue() : answers.Count == 1 ? answers.Peek() : CallOutcome<MaterialQuota>.Failed();
        return Task.FromResult(next);
    }
}