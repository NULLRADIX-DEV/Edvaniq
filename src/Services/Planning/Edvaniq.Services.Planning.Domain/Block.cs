namespace Edvaniq.Services.Planning.Domain;

// A planned piece of learning on one study day (docs/spec/glossary.md#block).
public sealed record Block(Guid TopicId, int Minutes);