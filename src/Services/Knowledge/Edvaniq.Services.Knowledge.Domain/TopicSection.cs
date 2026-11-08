using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Knowledge.Domain;

// A section of Content's material that belongs to a topic. Knowledge keeps only the id, the text stays with Content
// (docs/spec/object-model.md). Content asks for the sections of a topic later, this is where it gets them.
public sealed class TopicSection : IOwnedByUser
{
    internal TopicSection(string ownerId, Guid topicId, Guid sectionId)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        SectionId = sectionId;
    }

    public string OwnerId { get; private set; }

    public Guid TopicId { get; private set; }

    public Guid SectionId { get; private set; }
}