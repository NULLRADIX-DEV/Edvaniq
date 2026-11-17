using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// The text of a question Content made for a topic and sent with ItemsGenerated (#1106), so a later round on the topic can
// tell the AI what it asks already. Content's own output like the cards, Assessment keeps the question itself. Key is
// the question's key in the pool or in its PoolMaterial. The row goes with its section and with its pool, like the keys.
// Pools from before #1106 have none, there only the keys catch repeats.
public sealed class PoolQuestion : IOwnedByUser
{
    private PoolQuestion(Guid topicId, string key, Guid sectionId, string ownerId, string text)
    {
        TopicId = topicId;
        Key = key;
        SectionId = sectionId;
        OwnerId = ownerId;
        Text = text;
    }

    public Guid TopicId { get; private set; }

    public string Key { get; private set; }

    public Guid SectionId { get; private set; }

    public string OwnerId { get; private set; }

    public string Text { get; private set; }

    public static PoolQuestion Of(QuestionPool pool, string key, Guid sectionId, string text)
    {
        if (key.Length != QuestionPool.KeyLength)
        {
            throw new ArgumentException($"A key has {QuestionPool.KeyLength} characters.", nameof(key));
        }

        return new(pool.TopicId, key, sectionId, pool.OwnerId, text);
    }
}