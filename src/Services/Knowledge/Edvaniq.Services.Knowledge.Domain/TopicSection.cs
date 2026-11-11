using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Knowledge.Domain;

// A section of Content's material that belongs to a topic. Knowledge keeps only the ids, the text stays with Content
// (docs/spec/object-model.md). A topic gets sections from every document whose analysis brings it, DocumentId tells
// them apart. Content asks for the topics of a document later, this is where it gets them.
public sealed class TopicSection : IOwnedByUser
{
    internal TopicSection(string ownerId, Guid topicId, Guid documentId, Guid sectionId)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        DocumentId = documentId;
        SectionId = sectionId;
    }

    public string OwnerId { get; private set; }

    public Guid TopicId { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid SectionId { get; private set; }
}