namespace Edvaniq.Services.Planning.Domain;

// A planned piece of learning on one study day (docs/spec/glossary.md#block).
public sealed record Block(Guid TopicId, int Minutes, BlockKind Kind = BlockKind.Learn);

public enum BlockKind
{
    // Learning the topic, as a new topic or a quiz.
    Learn,

    // Refreshing a topic that was already introduced (docs/spec/glossary.md#wiederholung). Comes first on its day.
    Review,
}