using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Each wait reports the reports of its round on the way and ends with the last. Rounds go in order, the last repeats.
internal sealed class FakeSessionResults : ISessionResults
{
    private readonly Queue<(IReadOnlyList<SessionReport> OnTheWay, CallOutcome<SessionReport> End)> rounds = new();

    public int Waits { get; private set; }

    // While set, a round waits after reporting until the test opens it.
    public TaskCompletionSource? Gate { get; set; }

    public FakeSessionResults Round(CallOutcome<SessionReport> end, params SessionReport[] onTheWay)
    {
        rounds.Enqueue((onTheWay, end));
        return this;
    }

    public Task<CallOutcome<SessionReport>> GetAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Next().End);

    public async Task<CallOutcome<SessionReport>> WaitForCompleteAsync(
        Guid sessionId, IProgress<SessionReport>? progress = null, CancellationToken cancellationToken = default)
    {
        Waits++;
        var (onTheWay, end) = Next();
        foreach (var report in onTheWay)
        {
            progress?.Report(report);
        }

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return end;
    }

    private (IReadOnlyList<SessionReport> OnTheWay, CallOutcome<SessionReport> End) Next() =>
        rounds.Count > 1 ? rounds.Dequeue() : rounds.Peek();
}