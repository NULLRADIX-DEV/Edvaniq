using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers asks and answers with their queues in order and repeats the last answer after that. Remembers what it was sent.
internal sealed class FakeQuiz : IQuiz
{
    private readonly Queue<CallOutcome<AskedQuestion>> asks = new();
    private readonly Queue<CallOutcome<GradedAnswer>> answers = new();

    public List<Guid> Asked { get; } = [];

    public List<(Guid AttemptId, int Option, int ResponseTimeMs)> Answered { get; } = [];

    // While set, every answer waits until the test opens it.
    public TaskCompletionSource? AnswerGate { get; set; }

    public FakeQuiz Ask(CallOutcome<AskedQuestion> outcome)
    {
        asks.Enqueue(outcome);
        return this;
    }

    public FakeQuiz Ask(AskedQuestion question) => Ask(CallOutcome<AskedQuestion>.Ok(question));

    public FakeQuiz Answer(CallOutcome<GradedAnswer> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public FakeQuiz Answer(GradedAnswer graded) => Answer(CallOutcome<GradedAnswer>.Ok(graded));

    public Task<CallOutcome<AskedQuestion>> AskAsync(Guid topicId, CancellationToken cancellationToken = default)
    {
        Asked.Add(topicId);
        return Task.FromResult(Next(asks, CallOutcome<AskedQuestion>.Failed()));
    }

    public async Task<CallOutcome<GradedAnswer>> AnswerAsync(Guid attemptId, int option, int responseTimeMs, CancellationToken cancellationToken = default)
    {
        Answered.Add((attemptId, option, responseTimeMs));
        if (AnswerGate is not null)
        {
            await AnswerGate.Task;
        }

        return Next(answers, CallOutcome<GradedAnswer>.Failed());
    }

    public List<(Guid AttemptId, int ResponseTimeMs)> Revealed { get; } = [];

    // Shares the queue of answers, as the server grades both the same way.
    public async Task<CallOutcome<GradedAnswer>> RevealAsync(Guid attemptId, int responseTimeMs, CancellationToken cancellationToken = default)
    {
        Revealed.Add((attemptId, responseTimeMs));
        if (AnswerGate is not null)
        {
            await AnswerGate.Task;
        }

        return Next(answers, CallOutcome<GradedAnswer>.Failed());
    }

    private static T Next<T>(Queue<T> queue, T otherwise) =>
        queue.Count > 1 ? queue.Dequeue() : queue.Count == 1 ? queue.Peek() : otherwise;
}

// A clock the test moves by hand.
internal sealed class ManualTime : TimeProvider
{
    private long now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => now;

    public void Advance(TimeSpan by) => now += by.Ticks;
}