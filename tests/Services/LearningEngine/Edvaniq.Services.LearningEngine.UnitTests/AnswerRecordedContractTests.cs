using System.Text.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// LearningEngine as the receiver of the learning record: every shipped sample from tests/EventSamples, read the way the
// consumer reads it, becomes the right learning record. So does a message of a newer sender with a field unknown here.
public class AnswerRecordedContractTests
{
    public static TheoryData<string, bool> Samples =>
        [.. EventSamples.For("Assessment.AnswerRecorded").SelectMany(sample => new[] { (sample.Name, false), (sample.Name, true) })];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Sample_BecomesTheRecordItDescribes(string name, bool fromANewerSender)
    {
        var message = JsonNode.Parse(EventSamples.For("Assessment.AnswerRecorded").Single(sample => sample.Name == name).Json)!.AsObject();
        if (fromANewerSender)
        {
            message["version"] = AnswerRecorded.CurrentVersion + 1;
            message["addedLater"] = JsonValue.Create("a field this receiver doesn't know");
        }

        var records = new FakeRecords();
        var answer = JsonSerializer.Deserialize<AnswerRecorded>(message.ToJsonString(), EventJson.Options)!;
        await new AnswerRecordedHandler(records, NullLogger<AnswerRecordedHandler>.Instance).HandleAsync(answer, TestContext.Current.CancellationToken);

        var record = Assert.Single(records.Added);
        Assert.Equal(
            (Id("answerId"), Text("userId"), Id("topicId"), Id("itemId"), Optional<Guid>("sessionId"), message["correct"]!.GetValue<bool>()),
            (record.AnswerId, record.OwnerId, record.TopicId, record.ItemId, record.SessionId, record.Correct));
        Assert.Equal(
            (Optional<int>("difficulty"), message["responseTimeMs"]!.GetValue<int>(), Optional<int>("confidence"), (Domain.Mode)message["source"]!.GetValue<int>()),
            (record.Difficulty, record.ResponseTimeMs, record.Confidence, record.Source));
        Assert.Equal(DateTime.Parse(Text("answeredAt"), System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(), record.AnsweredAt);
        Assert.Equal(DateTimeKind.Utc, record.AnsweredAt.Kind);

        string Text(string field) => message[field]!.GetValue<string>();
        Guid Id(string field) => Guid.Parse(Text(field));
        T? Optional<T>(string field) where T : struct => message[field] is { } value ? JsonSerializer.Deserialize<T>(value) : null;
    }

    // The number of a mode is fixed on the wire, the full sample of v1 says Placement.
    [Fact]
    public async Task SourceOfTheFirstSample_IsPlacement()
    {
        var records = new FakeRecords();
        var sample = EventSamples.For("Assessment.AnswerRecorded").First(sample => sample is { Version: 1, Minimal: false });

        await new AnswerRecordedHandler(records, NullLogger<AnswerRecordedHandler>.Instance).HandleAsync(
            JsonSerializer.Deserialize<AnswerRecorded>(sample.Json, EventJson.Options)!, TestContext.Current.CancellationToken);

        Assert.Equal(Domain.Mode.Placement, Assert.Single(records.Added).Source);
    }

    private sealed class FakeRecords : ILearningRecords
    {
        public List<LearningRecord> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid answerId, CancellationToken cancellationToken) => Task.FromResult(false);

        public bool TopicKnown { get; init; } = true;

        public Task<bool> IsTopicKnownAsync(string ownerId, Guid topicId, CancellationToken cancellationToken) => Task.FromResult(TopicKnown);

        public void Add(LearningRecord record) => Added.Add(record);
    }
}