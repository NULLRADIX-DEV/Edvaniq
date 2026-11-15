using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers requests and reads per length with their queues in order and repeats the last answer after that. Remembers
// what it was asked.
internal sealed class FakeTopicSummaries : ITopicSummaries
{
    private readonly Dictionary<SummaryLength, Queue<CallOutcome<TopicSummary>>> requests = [];
    private readonly Dictionary<SummaryLength, Queue<CallOutcome<TopicSummary>>> reads = [];

    public List<SummaryLength> Requested { get; } = [];

    public List<SummaryLength> Read { get; } = [];

    // While set, every request waits until the test opens it.
    public TaskCompletionSource? RequestGate { get; set; }

    public FakeTopicSummaries OnRequest(SummaryLength length, CallOutcome<TopicSummary> outcome)
    {
        Queue(requests, length).Enqueue(outcome);
        return this;
    }

    public FakeTopicSummaries OnRequest(TopicSummary summary) => OnRequest(summary.Length, CallOutcome<TopicSummary>.Ok(summary));

    public FakeTopicSummaries OnGet(SummaryLength length, CallOutcome<TopicSummary> outcome)
    {
        Queue(reads, length).Enqueue(outcome);
        return this;
    }

    public FakeTopicSummaries OnGet(TopicSummary summary) => OnGet(summary.Length, CallOutcome<TopicSummary>.Ok(summary));

    public async Task<CallOutcome<TopicSummary>> RequestAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken = default)
    {
        Requested.Add(length);
        var next = Next(requests, length);
        if (RequestGate is not null)
        {
            await RequestGate.Task;
        }

        return next;
    }

    public async Task<CallOutcome<TopicSummary>> GetAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Read.Add(length);
        await Task.Yield();
        return Next(reads, length);
    }

    private static Queue<CallOutcome<TopicSummary>> Queue(Dictionary<SummaryLength, Queue<CallOutcome<TopicSummary>>> all, SummaryLength length) =>
        all.TryGetValue(length, out var queue) ? queue : all[length] = new();

    private static CallOutcome<TopicSummary> Next(Dictionary<SummaryLength, Queue<CallOutcome<TopicSummary>>> all, SummaryLength length) =>
        all.TryGetValue(length, out var queue) && queue.Count > 0
            ? queue.Count > 1 ? queue.Dequeue() : queue.Peek()
            : CallOutcome<TopicSummary>.Failed();
}