using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// A question taken over from Content's worker (docs/spec/glossary.md#frage), owned by Assessment from then on. The id is
// the itemId Content gave it, so a second delivery finds it. Until #214 the topic is the suggestion at TopicPosition in
// the analysis of DocumentId, #214 adds the topicId next to it and fills it in from Knowledge.
public sealed class Question : IOwnedByUser
{
    // Well above Content's limits (500, 200 per option, 1000), so a longer prompt later still fits the columns.
    public const int TextMaxLength = 2000;
    public const int OptionMaxLength = 1000;
    public const int MinOptions = 2;
    public const int MaxOptions = 10;
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

    public Guid SectionId { get; private set; }

    public string Text { get; private set; } = "";

    public IReadOnlyList<string> Options { get; private set; } = [];

    // The index of the one right option.
    public int Answer { get; private set; }

    public string Explanation { get; private set; } = "";

    public int Difficulty { get; private set; }

    public string ModelId { get; private set; } = "";

    public string PromptVersion { get; private set; } = "";

    public DateTime CreatedAt { get; private set; }

    // What a question needs to be asked at all. Anything else would break the quiz or the columns.
    public static bool IsValid(
        string? text, IReadOnlyList<string>? options, int answer, string? explanation, int difficulty, string? modelId, string? promptVersion) =>
        Fits(text, TextMaxLength)
        && options is { Count: >= MinOptions and <= MaxOptions }
        && options.All(option => Fits(option, OptionMaxLength))
        && answer >= 0 && answer < options.Count
        && Fits(explanation, ExplanationMaxLength)
        && difficulty is >= MinDifficulty and <= MaxDifficulty
        && Fits(modelId, ModelIdMaxLength)
        && Fits(promptVersion, PromptVersionMaxLength);

    public static Question Create(
        Guid id,
        string ownerId,
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
        DateTime createdAt)
    {
        if (!IsValid(text, options, answer, explanation, difficulty, modelId, promptVersion))
        {
            throw new ArgumentException("The question is not valid, check it with IsValid first.");
        }

        return new()
        {
            Id = id,
            OwnerId = ownerId,
            DocumentId = documentId,
            TopicPosition = topicPosition,
            SectionId = sectionId,
            Text = text,
            Options = [.. options],
            Answer = answer,
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