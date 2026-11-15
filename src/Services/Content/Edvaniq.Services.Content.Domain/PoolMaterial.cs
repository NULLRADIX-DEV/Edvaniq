using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A section the questions of a topic's pool were asked from (#915), with the keys of the questions that came from it when
// it was added to a pool later. A section with a row gets asked about no second time. The row goes with its section, so
// deleting a document frees its material and its keys: pasted again, it gets questions again.
public sealed class PoolMaterial : IOwnedByUser
{
    private PoolMaterial(Guid topicId, Guid sectionId, string ownerId, string? questionKeys)
    {
        TopicId = topicId;
        SectionId = sectionId;
        OwnerId = ownerId;
        QuestionKeys = questionKeys;
    }

    public Guid TopicId { get; private set; }

    public Guid SectionId { get; private set; }

    public string OwnerId { get; private set; }

    // The keys of the questions this section brought when it was added later, separated by spaces. Null for the material
    // a pool was made from, whose keys the pool keeps itself.
    public string? QuestionKeys { get; private set; }

    public IEnumerable<string> Keys => QuestionKeys?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    // The material a pool was made from.
    public static PoolMaterial Of(QuestionPool pool, Guid sectionId) => new(pool.TopicId, sectionId, pool.OwnerId, null);

    // New material of a pool and the keys of the questions that came from it.
    public static PoolMaterial Added(QuestionPool pool, Guid sectionId, IEnumerable<string> keys) =>
        new(pool.TopicId, sectionId, pool.OwnerId, string.Join(' ', keys) is { Length: > 0 } joined ? joined : null);
}