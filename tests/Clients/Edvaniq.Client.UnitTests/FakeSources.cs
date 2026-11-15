using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers each section with its queue in order and repeats the last answer after that, Failed for a section it doesn't
// know. Remembers what it was asked.
internal sealed class FakeSources : ISources
{
    private readonly Dictionary<Guid, Queue<CallOutcome<SectionSource>>> answers = [];

    public List<Guid> Asked { get; } = [];

    public FakeSources Answer(Guid sectionId, CallOutcome<SectionSource> outcome)
    {
        if (!answers.TryGetValue(sectionId, out var queue))
        {
            answers[sectionId] = queue = new();
        }

        queue.Enqueue(outcome);
        return this;
    }

    public FakeSources Answer(SectionSource source) => Answer(source.SectionId, CallOutcome<SectionSource>.Ok(source));

    public Task<CallOutcome<SectionSource>> SectionAsync(Guid sectionId, CancellationToken cancellationToken = default)
    {
        Asked.Add(sectionId);
        return Task.FromResult(answers.TryGetValue(sectionId, out var queue) && queue.Count > 0
            ? queue.Count > 1 ? queue.Dequeue() : queue.Peek()
            : CallOutcome<SectionSource>.Failed());
    }
}