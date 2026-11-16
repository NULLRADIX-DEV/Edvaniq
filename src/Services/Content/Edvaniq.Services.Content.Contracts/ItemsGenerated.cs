using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts;

// The questions and cards of one topic, made and checked by the worker (docs/spec/events.md). Assessment takes the questions
// over and owns them from then on, Flashcards the cards (M4). TopicId is Knowledge's id of the topic, which the worker
// asks Knowledge for before it makes the questions. TopicPosition stays from before: the place of the topic in the DocumentAnalyzed of the same document.
// Messages sent before TopicId came have none.
public sealed record ItemsGenerated(
    string UserId, Guid DocumentId, int TopicPosition, IReadOnlyList<GeneratedItem> Items, Guid? TopicId = null)
    : IUserEvent;

// ItemId comes from Content, so a second delivery of the event is the same item. DocumentId and SectionId are the source.
// Question is set for a question, Card for a card (#340).
public sealed record GeneratedItem(
    Guid ItemId,
    ItemKind Kind,
    Guid DocumentId,
    Guid SectionId,
    string ModelId,
    string PromptVersion,
    GeneratedQuestion? Question,
    GeneratedCard? Card = null);

// Answer is the index of the one right option. Type null is single choice, true/false has two options. For multiple
// choice Answers holds every right option and Answer one of them, so a receiver from before still reads a valid question.
// Type and Answers came later, the worker sends all three types since questions-3 and Answer is then the lowest right one.
public sealed record GeneratedQuestion(
    string Text,
    IReadOnlyList<string> Options,
    int Answer,
    string Explanation,
    int Difficulty,
    QuestionType? Type = null,
    IReadOnlyList<int>? Answers = null);

// Front asks, Back answers: the learner turns the card over and rates how well it went.
public sealed record GeneratedCard(string Front, string Back);

// The number goes over the broker, so it never changes.
public enum ItemKind
{
    Question = 0,
    Card = 1,
}

// The number goes over the broker, so it never changes.
public enum QuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    TrueFalse = 2,
}