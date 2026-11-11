using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers each kind of call with its queue in order and repeats the last answer after that. A paste becomes the newest
// document. Remembers what it was asked.
internal sealed class FakeLearningMaterial : ILearningMaterial
{
    private readonly Queue<CallOutcome<IReadOnlyList<DocumentInfo>>> documents = new();
    private readonly Queue<CallOutcome<IReadOnlyList<TopicInfo>>> topics = new();
    private readonly Queue<CallOutcome<DocumentInfo>> pastes = new();

    public int DocumentCalls { get; private set; }

    public int TopicCalls { get; private set; }

    public List<(string? Title, string Text)> Pasted { get; } = [];

    // While set, every paste waits until the test opens it.
    public TaskCompletionSource? PasteGate { get; set; }

    public FakeLearningMaterial Documents(params DocumentInfo[] list) => Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.Ok(list));

    public FakeLearningMaterial Documents(CallOutcome<IReadOnlyList<DocumentInfo>> outcome)
    {
        documents.Enqueue(outcome);
        return this;
    }

    public FakeLearningMaterial Topics(params TopicInfo[] list) => Topics(CallOutcome<IReadOnlyList<TopicInfo>>.Ok(list));

    public FakeLearningMaterial Topics(CallOutcome<IReadOnlyList<TopicInfo>> outcome)
    {
        topics.Enqueue(outcome);
        return this;
    }

    public FakeLearningMaterial Paste(CallOutcome<DocumentInfo> outcome)
    {
        pastes.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        DocumentCalls++;
        return Task.FromResult(Next(documents, CallOutcome<IReadOnlyList<DocumentInfo>>.Ok([])));
    }

    public Task<CallOutcome<IReadOnlyList<TopicInfo>>> ListTopicsAsync(CancellationToken cancellationToken = default)
    {
        TopicCalls++;
        return Task.FromResult(Next(topics, CallOutcome<IReadOnlyList<TopicInfo>>.Ok([])));
    }

    public async Task<CallOutcome<DocumentInfo>> PasteAsync(string? title, string text, CancellationToken cancellationToken = default)
    {
        Pasted.Add((title, text));
        if (PasteGate is not null)
        {
            await PasteGate.Task;
        }

        return Next(pastes, CallOutcome<DocumentInfo>.Failed());
    }

    private static T Next<T>(Queue<T> queue, T otherwise) =>
        queue.Count > 1 ? queue.Dequeue() : queue.Count == 1 ? queue.Peek() : otherwise;
}