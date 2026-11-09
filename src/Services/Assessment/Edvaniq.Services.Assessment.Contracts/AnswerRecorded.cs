using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Assessment.Contracts;

// A user answered a question: the learning record (docs/spec/events.md#lern-nachweis). CardReviewed of Flashcards
// carries the same fields. LearningEngine keeps one record per AnswerId, however often the event arrives. UserId is the
// OwnerId of the answer, AnsweredAt is UTC and set by the server. ResponseTimeMs and Confidence are stored, never
// weighed. Version is CurrentVersion of the sender; it only grows when an optional field is added.
public sealed record AnswerRecorded(
    int Version,
    string UserId,
    Guid TopicId,
    Guid ItemId,
    Guid? SessionId,
    Guid AnswerId,
    bool Correct,
    int? Difficulty,
    int ResponseTimeMs,
    int? Confidence,
    Mode Source,
    DateTime AnsweredAt) : IIntegrationEvent
{
    public const int CurrentVersion = 1;
}