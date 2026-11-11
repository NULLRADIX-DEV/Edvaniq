using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.LearningEngine.Domain;

// A user finished the unit "Neues Thema" of a topic (docs/spec/glossary.md#eingeführt), learnt from SessionCompleted.
// The first one counts, a later session doesn't introduce the topic again.
public sealed class TopicIntroduction : IOwnedByUser
{
    private TopicIntroduction(string ownerId, Guid topicId, Guid sessionId, DateTime introducedAt)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        SessionId = sessionId;
        IntroducedAt = introducedAt;
    }

    public string OwnerId { get; private set; }

    public Guid TopicId { get; private set; }

    public Guid SessionId { get; private set; }

    public DateTime IntroducedAt { get; private set; }

    public static TopicIntroduction Create(string ownerId, Guid topicId, Guid sessionId, DateTime introducedAt) =>
        new(ownerId, topicId, sessionId, DateTime.SpecifyKind(introducedAt, DateTimeKind.Utc));

    // Answers count once the topic is introduced, the check questions of the unit itself too (docs/spec/modes.md).
    public bool Counts(LearningRecord record) => record.SessionId == SessionId || record.AnsweredAt >= IntroducedAt;
}