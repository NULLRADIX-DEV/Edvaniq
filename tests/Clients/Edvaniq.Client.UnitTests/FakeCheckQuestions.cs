using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers each start with the next outcome of its queue and repeats the last. Remembers what it was asked.
internal sealed class FakeCheckQuestions : ICheckQuestions
{
    private readonly Queue<CallOutcome<CheckQuestions>> starts = new();

    public List<(Guid TopicId, Guid? SessionId)> Started { get; } = [];

    // While set, every start waits until the test opens it.
    public TaskCompletionSource? Gate { get; set; }

    public FakeCheckQuestions Start(CallOutcome<CheckQuestions> outcome)
    {
        starts.Enqueue(outcome);
        return this;
    }

    public FakeCheckQuestions Start(CheckQuestions checks) => Start(CallOutcome<CheckQuestions>.Ok(checks));

    public async Task<CallOutcome<CheckQuestions>> StartAsync(Guid topicId, Guid? sessionId, CancellationToken cancellationToken = default)
    {
        Started.Add((topicId, sessionId));
        var next = starts.Count > 1 ? starts.Dequeue() : starts.Count == 1 ? starts.Peek() : CallOutcome<CheckQuestions>.Failed();
        if (Gate is not null)
        {
            await Gate.Task;
        }

        return next;
    }
}