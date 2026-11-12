using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts;

// The questions of one topic, made and checked by the worker (docs/spec/events.md). Assessment takes them over and owns
// them from then on. TopicId is Knowledge's id of the topic, which the worker asks Knowledge for before it makes the
// questions. TopicPosition stays from before: the place of the topic in the DocumentAnalyzed of the same document.
// Messages sent before TopicId came have none.
public sealed record ItemsGenerated(
    string UserId, Guid DocumentId, int TopicPosition, IReadOnlyList<GeneratedItem> Items, Guid? TopicId = null)
    : IUserEvent;

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

// Answer is the index of the one right option. Type null is single choice, true/false has two options. For multiple
// choice Answers holds every right option and Answer one of them, so a receiver from before still reads a valid question.
// Type and Answers came later, today the worker sends single choice only.
public sealed record GeneratedQuestion(
    string Text,
    IReadOnlyList<string> Options,
    int Answer,
    string Explanation,
    int Difficulty,
    QuestionType? Type = null,
    IReadOnlyList<int>? Answers = null);

public enum ItemKind
{
    Question = 0,
}

// The number goes over the broker, so it never changes.
public enum QuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    TrueFalse = 2,
}