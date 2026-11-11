using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// Content has made the questions of a topic and sent them with ItemsGenerated. TopicId is Knowledge's id, only a
// reference. It's the key: a topic gets its pool once, a topic that comes again unchanged gets none (docs/spec/events.md).
// The questions themselves belong to Assessment from then on, Content keeps none of them.
public sealed class QuestionPool : IOwnedByUser
{
    private QuestionPool(Guid topicId, Guid documentId, string ownerId, DateTime createdAt)
    {
        TopicId = topicId;
        DocumentId = documentId;
        OwnerId = ownerId;
        CreatedAt = createdAt;
    }

    public Guid TopicId { get; private set; }

    // The document whose analysis brought the topic.
    public Guid DocumentId { get; private set; }

    public string OwnerId { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public static QuestionPool For(Document document, Guid topicId, DateTime createdAt) =>
        new(topicId, document.Id, document.OwnerId, createdAt);
}