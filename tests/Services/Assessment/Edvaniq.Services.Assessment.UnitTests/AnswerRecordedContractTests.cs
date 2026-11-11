using System.Text.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Testing;

namespace Edvaniq.Services.Assessment.UnitTests;

// Assessment as the sender of the learning record: the message it writes matches the shipped samples in
// tests/EventSamples, field by field and with the same JSON types (docs/spec/events.md, "Der Vertrag im Code").
public class AnswerRecordedContractTests
{
    private const string Event = "Assessment.AnswerRecorded";

    public static TheoryData<string> Samples => [.. EventSamples.For(Event).Select(sample => sample.Name)];

    [Fact]
    public void EveryVersion_HasOneFullAndOneMinimalSample()
    {
        var samples = EventSamples.For(Event);

        Assert.Equal(
            Enumerable.Range(1, AnswerRecorded.CurrentVersion).SelectMany(version => new[] { (version, false), (version, true) }),
            samples.Select(sample => (sample.Version, sample.Minimal)));
        Assert.All(samples, sample => Assert.Equal(sample.Version, JsonNode.Parse(sample.Json)!["version"]!.GetValue<int>()));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_IsWrittenBackUnchanged(string name)
    {
        var sample = EventSamples.For(Event).Single(candidate => candidate.Name == name);
        var shipped = JsonNode.Parse(sample.Json)!.AsObject();

        var written = Write(JsonSerializer.Deserialize<AnswerRecorded>(sample.Json, EventJson.Options)!);

        if (sample.Version == AnswerRecorded.CurrentVersion)
        {
            AssertSame(shipped, written);
            return;
        }

        // An older message lacks the optional fields added since, everything it has stays as it was.
        Assert.All(shipped, field => Assert.True(JsonNode.DeepEquals(field.Value, written[field.Key]), $"{field.Key} changed"));
        Assert.All(written.Where(field => !shipped.ContainsKey(field.Key)), field => Assert.Null(field.Value));
    }

    [Fact]
    public void AnswerBuiltInCode_IsWrittenLikeTheSample()
    {
        var answer = new AnswerRecorded(
            AnswerRecorded.CurrentVersion,
            "alice",
            Guid.Parse("6f1c2a3b-0d4e-4f5a-8b6c-7d8e9f0a1b2c"),
            Guid.Parse("0a1b2c3d-4e5f-4a6b-9c7d-8e9f0a1b2c3d"),
            Guid.Parse("9e8d7c6b-5a4f-4e3d-ac2b-1a0f9e8d7c6b"),
            Guid.Parse("c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f"),
            Correct: true,
            Difficulty: 2,
            ResponseTimeMs: 5100,
            Confidence: 3,
            Mode.Placement,
            new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc));

        var sample = EventSamples.For(Event).Single(candidate => candidate.Version == AnswerRecorded.CurrentVersion && !candidate.Minimal);

        AssertSame(JsonNode.Parse(sample.Json)!, Write(answer));
    }

    private static JsonObject Write(AnswerRecorded answer) =>
        JsonNode.Parse(JsonSerializer.Serialize(answer, answer.GetType(), EventJson.Options))!.AsObject();

    private static void AssertSame(JsonNode expected, JsonNode actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected {expected.ToJsonString()}, wrote {actual.ToJsonString()}");
}