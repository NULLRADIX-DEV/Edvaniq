using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers the pings in the given order and repeats the last answer after that. Null stands for a ping that did not get
// through. While a gate is set, every ping waits until the test opens it.
internal sealed class FakePing(params PingResult?[] answers) : IPing
{
    private readonly Queue<PingResult?> answers = new(answers);

    public int Pings { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public async Task<PingResult?> SendAsync(CancellationToken cancellationToken = default)
    {
        Pings++;
        if (Gate is not null)
        {
            await Gate.Task;
        }

        return answers.Count > 1 ? answers.Dequeue() : answers.Peek();
    }
}