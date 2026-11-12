using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers with its queue in order and adds every topic after that. Remembers what it was sent.
internal sealed class FakeTopicEntries : ITopicEntries
{
    private readonly Queue<CallOutcome<TopicAdded>> answers = new();

    public List<(Guid LearningGoalId, string Name)> Added { get; } = [];

    public FakeTopicEntries Answer(CallOutcome<TopicAdded> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<TopicAdded>> AddAsync(Guid learningGoalId, string name, CancellationToken cancellationToken = default)
    {
        Added.Add((learningGoalId, name));
        return Task.FromResult(answers.Count > 0 ? answers.Dequeue() : CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: false)));
    }
}