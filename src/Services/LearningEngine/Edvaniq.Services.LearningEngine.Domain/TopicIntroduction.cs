using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.LearningEngine.Domain;

// A user finished the unit "Neues Thema" of a topic (docs/spec/glossary.md#eingeführt), learnt from SessionCompleted.
// The first one counts, a later session doesn't introduce the topic again.
public sealed class TopicIntroduction : IOwnedByUser
{
    private TopicIntroduction(string ownerId, Guid topicId, Guid sessionId, DateTime introducedAt, DateTime? unitStartedAt)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        SessionId = sessionId;
        IntroducedAt = introducedAt;
        UnitStartedAt = unitStartedAt;
    }

    public string OwnerId { get; private set; }

    public Guid TopicId { get; private set; }

    public Guid SessionId { get; private set; }

    public DateTime IntroducedAt { get; private set; }

    // When the unit started (#1040): the first session on its block, broken off ones included. Null for an introduction
    // from an event sent before Planning named it.
    public DateTime? UnitStartedAt { get; private set; }

    // From here on every answer on the topic counts.
    public DateTime CountsFrom => UnitStartedAt is { } started && started < IntroducedAt ? started : IntroducedAt;

    public static TopicIntroduction Create(string ownerId, Guid topicId, Guid sessionId, DateTime introducedAt, DateTime? unitStartedAt = null) =>
        new(
            ownerId,
            topicId,
            sessionId,
            DateTime.SpecifyKind(introducedAt, DateTimeKind.Utc),
            unitStartedAt is { } started ? DateTime.SpecifyKind(started, DateTimeKind.Utc) : null);

    // Answers count once the unit started, its check questions and quiz too (docs/spec/modes.md). Without the unit's
    // start only from the end, and the check questions of the introducing session.
    public bool Counts(LearningRecord record) => record.SessionId == SessionId || record.AnsweredAt >= CountsFrom;
}