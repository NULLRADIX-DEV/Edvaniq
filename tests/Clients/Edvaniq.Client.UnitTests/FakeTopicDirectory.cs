using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers from what the test set up, and counts what it was asked. A topic or document without an answer of its own
// gets an empty list.
internal sealed class FakeTopicDirectory : ITopicDirectory
{
    public CallOutcome<IReadOnlyList<TopicInfo>> Topics { get; set; } = CallOutcome<IReadOnlyList<TopicInfo>>.Ok([]);

    public CallOutcome<IReadOnlyList<DocumentInfo>> Documents { get; set; } = CallOutcome<IReadOnlyList<DocumentInfo>>.Ok([]);

    public Dictionary<Guid, Queue<CallOutcome<IReadOnlyList<KeyTermInfo>>>> Terms { get; } = [];

    public Dictionary<Guid, IReadOnlyList<TopicSectionInfo>> TopicSections { get; } = [];

    public Dictionary<Guid, CallOutcome<IReadOnlyList<DocumentSectionInfo>>> DocumentSections { get; } = [];

    public List<Guid> TermCalls { get; } = [];

    public List<Guid> TopicSectionCalls { get; } = [];

    public List<Guid> DocumentSectionCalls { get; } = [];

    public int TopicCalls { get; private set; }

    public FakeTopicDirectory WithTerms(Guid topicId, params CallOutcome<IReadOnlyList<KeyTermInfo>>[] answers)
    {
        Terms[topicId] = new Queue<CallOutcome<IReadOnlyList<KeyTermInfo>>>(answers);
        return this;
    }

    public Task<CallOutcome<IReadOnlyList<TopicInfo>>> ListTopicsAsync(CancellationToken cancellationToken = default)
    {
        TopicCalls++;
        return Task.FromResult(Topics);
    }

    public Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents);

    public Task<CallOutcome<IReadOnlyList<KeyTermInfo>>> TermsAsync(Guid topicId, CancellationToken cancellationToken = default)
    {
        TermCalls.Add(topicId);
        var answer = Terms.TryGetValue(topicId, out var queue) && queue.Count > 0
            ? (queue.Count > 1 ? queue.Dequeue() : queue.Peek())
            : CallOutcome<IReadOnlyList<KeyTermInfo>>.Ok([]);
        return Task.FromResult(answer);
    }

    public Task<CallOutcome<IReadOnlyList<TopicSectionInfo>>> TopicSectionsAsync(Guid topicId, CancellationToken cancellationToken = default)
    {
        TopicSectionCalls.Add(topicId);
        return Task.FromResult(CallOutcome<IReadOnlyList<TopicSectionInfo>>.Ok(TopicSections.GetValueOrDefault(topicId, [])));
    }

    public Task<CallOutcome<IReadOnlyList<DocumentSectionInfo>>> DocumentSectionsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        DocumentSectionCalls.Add(documentId);
        return Task.FromResult(DocumentSections.GetValueOrDefault(documentId, CallOutcome<IReadOnlyList<DocumentSectionInfo>>.Ok([])));
    }
}