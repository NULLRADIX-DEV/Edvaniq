using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.LearningEngine.Domain;

// What one answer leaves behind (docs/spec/glossary.md#lern-nachweis). Never changed after it's stored. The AnswerId
// is the key, so an answer counts once however often its event arrives. ResponseTimeMs and Confidence are kept for
// later, nothing weighs them. Kept for RetentionMonths after AnsweredAt (Q27), deleting them after that is #365.
public sealed class LearningRecord : IOwnedByUser
{
    public const int RetentionMonths = 12;

    private LearningRecord()
    {
    }

    public Guid AnswerId { get; private set; }

    public string OwnerId { get; private set; } = "";

    public Guid TopicId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid? SessionId { get; private set; }

    public bool Correct { get; private set; }

    public int? Difficulty { get; private set; }

    public int ResponseTimeMs { get; private set; }

    public int? Confidence { get; private set; }

    public Mode Source { get; private set; }

    public DateTime AnsweredAt { get; private set; }

    // The answer of the newest record, so a client can tell its answer has arrived. Two at the same time go by the
    // larger id, the same records always give the same one. Null without records.
    public static Guid? Latest(IEnumerable<LearningRecord> records) =>
        records.OrderByDescending(record => record.AnsweredAt).ThenByDescending(record => record.AnswerId)
            .Select(record => (Guid?)record.AnswerId)
            .FirstOrDefault();

    public static LearningRecord Create(
        Guid answerId,
        string ownerId,
        Guid topicId,
        Guid itemId,
        Guid? sessionId,
        bool correct,
        int? difficulty,
        int responseTimeMs,
        int? confidence,
        Mode source,
        DateTime answeredAt) =>
        new()
        {
            AnswerId = answerId,
            OwnerId = ownerId,
            TopicId = topicId,
            ItemId = itemId,
            SessionId = sessionId,
            Correct = correct,
            Difficulty = difficulty,
            ResponseTimeMs = responseTimeMs,
            Confidence = confidence,
            Source = source,
            AnsweredAt = DateTime.SpecifyKind(answeredAt, DateTimeKind.Utc),
        };
}