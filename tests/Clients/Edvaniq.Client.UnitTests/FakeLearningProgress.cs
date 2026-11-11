using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers with its queue in order and repeats the last answer after that. Without any answer, LearningEngine knows no
// topics.
internal sealed class FakeLearningProgress : ILearningProgress
{
    private readonly Queue<CallOutcome<IReadOnlyList<TopicProgress>>> answers = new();

    public int Calls { get; private set; }

    public FakeLearningProgress Answer(params TopicProgress[] topics) => Answer(CallOutcome<IReadOnlyList<TopicProgress>>.Ok(topics));

    public FakeLearningProgress Answer(CallOutcome<IReadOnlyList<TopicProgress>> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<IReadOnlyList<TopicProgress>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        var next = answers.Count > 1
            ? answers.Dequeue()
            : answers.Count == 1 ? answers.Peek() : CallOutcome<IReadOnlyList<TopicProgress>>.Ok([]);
        return Task.FromResult(next);
    }
}