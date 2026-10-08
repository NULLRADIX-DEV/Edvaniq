using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers the checks in the given order and repeats the last answer after that.
internal sealed class FakeBackendStatus(params bool[] answers) : IBackendStatus
{
    private readonly Queue<bool> answers = new(answers);

    public int Checks { get; private set; }

    public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        Checks++;
        return Task.FromResult(answers.Count > 1 ? answers.Dequeue() : answers.Peek());
    }
}