using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers with its queue in order and adds every topic after that. Changes of the directory answer from their own queue,
// after that each goes through with the name it was given. Remembers what it was sent.
internal sealed class FakeTopicEntries : ITopicEntries
{
    private readonly Queue<CallOutcome<TopicAdded>> answers = new();
    private readonly Queue<CallOutcome<TopicEdit>> edits = new();

    public List<(Guid LearningGoalId, string Name)> Added { get; } = [];

    // Each change of the directory as "Kind id name", e.g. "Rename 5f0c… Zelle".
    public List<string> Changes { get; } = [];

    // Called before a change answers, to change what the directory lists next.
    public Action<string, Guid>? OnChange { get; set; }

    public FakeTopicEntries Answer(CallOutcome<TopicAdded> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public FakeTopicEntries Edit(CallOutcome<TopicEdit> outcome)
    {
        edits.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<TopicAdded>> AddAsync(Guid learningGoalId, string name, CancellationToken cancellationToken = default)
    {
        Added.Add((learningGoalId, name));
        return Task.FromResult(answers.Count > 0 ? answers.Dequeue() : CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: false)));
    }

    public Task<CallOutcome<TopicEdit>> CreateTopicAsync(Guid learningGoalId, string name, CancellationToken cancellationToken = default) =>
        Change("CreateTopic", learningGoalId, name);

    public Task<CallOutcome<TopicEdit>> CreateSubtopicAsync(Guid parentId, string name, CancellationToken cancellationToken = default) =>
        Change("CreateSubtopic", parentId, name);

    public Task<CallOutcome<TopicEdit>> RenameAsync(Guid topicId, string name, CancellationToken cancellationToken = default) =>
        Change("Rename", topicId, name);

    public Task<CallOutcome<TopicEdit>> HideAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        Change("Hide", topicId, "");

    public Task<CallOutcome<TopicEdit>> ShowAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        Change("Show", topicId, "");

    private Task<CallOutcome<TopicEdit>> Change(string kind, Guid id, string name)
    {
        Changes.Add($"{kind} {id} {name}".TrimEnd());
        if (edits.Count > 0)
        {
            return Task.FromResult(edits.Dequeue());
        }

        OnChange?.Invoke(kind, id);
        var topicId = kind.StartsWith("Create", StringComparison.Ordinal) ? Guid.NewGuid() : id;
        return Task.FromResult(CallOutcome<TopicEdit>.Ok(new TopicEdit(new EditedTopic(topicId, name, Hidden: kind == "Hide"))));
    }
}