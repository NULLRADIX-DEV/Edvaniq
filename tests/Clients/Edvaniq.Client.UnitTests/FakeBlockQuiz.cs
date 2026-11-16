using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers each open with the next outcome of its queue and repeats the last. Remembers what it was asked.
internal sealed class FakeBlockQuiz : IBlockQuiz
{
    private readonly Queue<CallOutcome<BlockQuiz>> opens = new();

    public List<(Guid TopicId, Guid BlockId, int Size)> Opened { get; } = [];

    public FakeBlockQuiz Open(CallOutcome<BlockQuiz> outcome)
    {
        opens.Enqueue(outcome);
        return this;
    }

    public FakeBlockQuiz Open(BlockQuiz quiz) => Open(CallOutcome<BlockQuiz>.Ok(quiz));

    public Task<CallOutcome<BlockQuiz>> OpenAsync(Guid topicId, Guid blockId, int size, CancellationToken cancellationToken = default)
    {
        Opened.Add((topicId, blockId, size));
        var next = opens.Count > 1 ? opens.Dequeue() : opens.Count == 1 ? opens.Peek() : CallOutcome<BlockQuiz>.Failed();
        return Task.FromResult(next);
    }
}