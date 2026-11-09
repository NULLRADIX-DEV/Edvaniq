using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts;

// The questions of one topic, made and checked by the worker (docs/spec/events.md). Assessment takes them over and owns
// them from then on. Content knows no topicId yet, so until #214 the topic is the suggestion at TopicPosition in the
// DocumentAnalyzed of the same document, which Knowledge keeps as the topic's position.
public sealed record ItemsGenerated(string UserId, Guid DocumentId, int TopicPosition, IReadOnlyList<GeneratedItem> Items)
    : IIntegrationEvent;

// ItemId comes from Content, so a second delivery of the event is the same item. DocumentId and SectionId are the source.
// Question is set for a question, cards come with their own field (M4).
public sealed record GeneratedItem(
    Guid ItemId,
    ItemKind Kind,
    Guid DocumentId,
    Guid SectionId,
    string ModelId,
    string PromptVersion,
    GeneratedQuestion? Question);

// Answer is the index of the one right option.
public sealed record GeneratedQuestion(string Text, IReadOnlyList<string> Options, int Answer, string Explanation, int Difficulty);

public enum ItemKind
{
    Question = 0,
}