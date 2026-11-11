using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// The handler turns each new answer into one learning record and leaves a known answerId alone.
public class AnswerRecordedHandlerTests
{
    private static readonly DateTime AnsweredAt = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NewAnswer_AddsARecordWithEveryField()
    {
        var records = new FakeRecords();
        var answer = new AnswerRecorded(
            AnswerRecorded.CurrentVersion, "alice", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            true, 2, 5100, 3, Assessment.Contracts.Mode.MockExam, AnsweredAt);

        await new AnswerRecordedHandler(records, NullLogger<AnswerRecordedHandler>.Instance).HandleAsync(answer, TestContext.Current.CancellationToken);

        var record = Assert.Single(records.Added);
        Assert.Equal(answer.AnswerId, records.AskedFor);
        Assert.Equal(
            (answer.AnswerId, "alice", answer.TopicId, answer.ItemId, answer.SessionId, true, (int?)2),
            (record.AnswerId, record.OwnerId, record.TopicId, record.ItemId, record.SessionId, record.Correct, record.Difficulty));
        Assert.Equal((5100, (int?)3, Domain.Mode.MockExam, AnsweredAt), (record.ResponseTimeMs, record.Confidence, record.Source, record.AnsweredAt));
    }

    [Fact]
    public async Task KnownAnswer_AddsNothing()
    {
        var records = new FakeRecords { Exists = true };
        var answer = new AnswerRecorded(
            AnswerRecorded.CurrentVersion, "alice", Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            false, null, 900, null, Assessment.Contracts.Mode.Practice, AnsweredAt);

        await new AnswerRecordedHandler(records, NullLogger<AnswerRecordedHandler>.Instance).HandleAsync(answer, TestContext.Current.CancellationToken);

        Assert.Empty(records.Added);
    }

    // #205: an answer before its topic is kept all the same, with a log line that names ids only.
    [Fact]
    public async Task AnswerToATopicNotKnownYet_IsKept_AndLogged()
    {
        var records = new FakeRecords { TopicKnown = false };
        var logger = new CollectingLogger();
        var answer = new AnswerRecorded(
            AnswerRecorded.CurrentVersion, "alice", Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            true, 2, 900, null, Assessment.Contracts.Mode.Practice, AnsweredAt);

        await new AnswerRecordedHandler(records, logger).HandleAsync(answer, TestContext.Current.CancellationToken);

        Assert.Single(records.Added);
        var line = Assert.Single(logger.Lines);
        Assert.Equal($"Answer {answer.AnswerId} is kept for topic {answer.TopicId}, which isn't known yet; it counts once the topic is", line);
        Assert.DoesNotContain("alice", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnswerToAKnownTopic_LogsNothing()
    {
        var logger = new CollectingLogger();
        var answer = new AnswerRecorded(
            AnswerRecorded.CurrentVersion, "alice", Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            true, 2, 900, null, Assessment.Contracts.Mode.Practice, AnsweredAt);

        await new AnswerRecordedHandler(new FakeRecords(), logger).HandleAsync(answer, TestContext.Current.CancellationToken);

        Assert.Empty(logger.Lines);
    }

    // The numbers go over the broker, the two enums must agree on every one of them.
    [Fact]
    public void Modes_HaveTheSameNamesAndNumbers()
    {
        static IEnumerable<(string, int)> Values<T>() where T : struct, Enum =>
            Enum.GetValues<T>().Select(value => (value.ToString(), Convert.ToInt32(value)));

        Assert.Equal(Values<Assessment.Contracts.Mode>(), Values<Domain.Mode>());
    }

    private sealed class FakeRecords : ILearningRecords
    {
        public bool Exists { get; init; }

        public Guid? AskedFor { get; private set; }

        public List<LearningRecord> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid answerId, CancellationToken cancellationToken)
        {
            AskedFor = answerId;
            return Task.FromResult(Exists);
        }

        public bool TopicKnown { get; init; } = true;

        public Task<bool> IsTopicKnownAsync(string ownerId, Guid topicId, CancellationToken cancellationToken) => Task.FromResult(TopicKnown);

        public void Add(LearningRecord record) => Added.Add(record);
    }

    private sealed class CollectingLogger : ILogger<AnswerRecordedHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}