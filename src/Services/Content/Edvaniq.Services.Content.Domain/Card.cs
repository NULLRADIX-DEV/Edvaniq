using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A card Content made with a topic's pool and sent with ItemsGenerated (#340). Flashcards takes the cards over from M4 on,
// until then nobody reads the event, so Content keeps them to send again once Flashcards is there (#498). Id is the
// itemId of the event, so a second sending is the same card. It goes with its document and with its user.
public sealed class Card : IOwnedByUser
{
    private Card(
        Guid id, string ownerId, Guid documentId, Guid topicId, Guid sectionId, string modelId, string promptVersion, string front, string back, DateTime createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        DocumentId = documentId;
        TopicId = topicId;
        SectionId = sectionId;
        ModelId = modelId;
        PromptVersion = promptVersion;
        Front = front;
        Back = back;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; }

    // The source: the document and its section the card comes from.
    public Guid DocumentId { get; private set; }

    public Guid SectionId { get; private set; }

    // Knowledge's id of the topic, only a reference.
    public Guid TopicId { get; private set; }

    public string ModelId { get; private set; }

    public string PromptVersion { get; private set; }

    public string Front { get; private set; }

    public string Back { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public static Card Of(
        QuestionPool pool, Guid id, Guid sectionId, string modelId, string promptVersion, string front, string back, DateTime createdAt) =>
        new(id, pool.OwnerId, pool.DocumentId, pool.TopicId, sectionId, modelId, promptVersion, front, back, createdAt);
}