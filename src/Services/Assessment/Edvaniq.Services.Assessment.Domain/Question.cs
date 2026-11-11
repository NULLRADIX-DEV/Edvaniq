using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// A question taken over from Content's worker (docs/spec/glossary.md#frage), owned by Assessment from then on. The id is
// the itemId Content gave it, so a second delivery finds it. TopicId is Knowledge's topic. Questions taken over before
// it came have none and are never asked, DocumentId and TopicPosition stay as their source.
public sealed class Question : IOwnedByUser
{
    // Well above Content's limits (500, 200 per option, 1000), so a longer prompt later still fits the columns.
    public const int TextMaxLength = 2000;
    public const int OptionMaxLength = 1000;
    public const int MinOptions = 2;
    public const int MaxOptions = 10;
    public const int TrueFalseOptions = 2;
    public const int ExplanationMaxLength = 4000;
    public const int ModelIdMaxLength = 200;
    public const int PromptVersionMaxLength = 50;
    public const int MinDifficulty = 1;
    public const int MaxDifficulty = 3;

    private Question()
    {
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; } = "";

    public Guid DocumentId { get; private set; }

    public int TopicPosition { get; private set; }

    public Guid? TopicId { get; private set; }

    public Guid SectionId { get; private set; }

    public QuestionType Type { get; private set; }

    public string Text { get; private set; } = "";

    public IReadOnlyList<string> Options { get; private set; } = [];

    // The index of the one right option. For multiple choice the lowest of Answers, so an older reader still finds one.
    public int Answer { get; private set; }

    // Every right option, sorted, only for multiple choice.
    public IReadOnlyList<int>? Answers { get; private set; }

    public string Explanation { get; private set; } = "";

    public int Difficulty { get; private set; }

    public string ModelId { get; private set; } = "";

    public string PromptVersion { get; private set; } = "";

    public DateTime CreatedAt { get; private set; }

    public IReadOnlyList<int> RightOptions => Answers ?? [Answer];

    // What a question needs to be asked at all. Anything else would break the quiz or the columns. Answers only counts
    // for multiple choice, there it holds every right option, Answer among them.
    public static bool IsValid(
        string? text,
        IReadOnlyList<string>? options,
        int answer,
        string? explanation,
        int difficulty,
        string? modelId,
        string? promptVersion,
        QuestionType type = QuestionType.SingleChoice,
        IReadOnlyList<int>? answers = null) =>
        Fits(text, TextMaxLength)
        && options is { Count: >= MinOptions and <= MaxOptions }
        && options.All(option => Fits(option, OptionMaxLength))
        && answer >= 0 && answer < options.Count
        && type switch
        {
            QuestionType.SingleChoice => true,
            QuestionType.TrueFalse => options.Count == TrueFalseOptions,
            QuestionType.MultipleChoice => answers is { Count: > 0 }
                && answers.Distinct().Count() == answers.Count
                && answers.All(right => right >= 0 && right < options.Count)
                && answers.Contains(answer),
            _ => false,
        }
        && Fits(explanation, ExplanationMaxLength)
        && difficulty is >= MinDifficulty and <= MaxDifficulty
        && Fits(modelId, ModelIdMaxLength)
        && Fits(promptVersion, PromptVersionMaxLength);

    public static Question Create(
        Guid id,
        string ownerId,
        Guid topicId,
        Guid documentId,
        int topicPosition,
        Guid sectionId,
        string text,
        IReadOnlyList<string> options,
        int answer,
        string explanation,
        int difficulty,
        string modelId,
        string promptVersion,
        DateTime createdAt,
        QuestionType type = QuestionType.SingleChoice,
        IReadOnlyList<int>? answers = null)
    {
        if (!IsValid(text, options, answer, explanation, difficulty, modelId, promptVersion, type, answers))
        {
            throw new ArgumentException("The question is not valid, check it with IsValid first.");
        }

        var rights = type == QuestionType.MultipleChoice ? answers!.Order().ToList() : null;
        return new()
        {
            Id = id,
            OwnerId = ownerId,
            TopicId = topicId,
            DocumentId = documentId,
            TopicPosition = topicPosition,
            SectionId = sectionId,
            Type = type,
            Text = text,
            Options = [.. options],
            Answer = rights?[0] ?? answer,
            Answers = rights,
            Explanation = explanation,
            Difficulty = difficulty,
            ModelId = modelId,
            PromptVersion = promptVersion,
            CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
        };
    }

    private static bool Fits(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;
}