using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// One request to the AI provider for a user's document, with what it used and cost (docs/service-internals.md#ki-kosten).
// A call that asks again has one row per attempt under the same CallId. Never the prompt, the material or the answer.
// DocumentId is only a reference, the cost stays when the document goes. When the user goes, AiUsageDay keeps the sums.
public sealed class AiAttempt : IOwnedByUser
{
    public const int PurposeMaxLength = 32;

    public const int ModelMaxLength = 128;

    public const int PromptVersionMaxLength = 32;

    // What the call was for.
    public const string Topics = "topics";

    public const string Questions = "questions";

    private AiAttempt(
        Guid callId, int attempt, string ownerId, Guid documentId, string purpose, string model, string promptVersion,
        DateTime calledAt, long durationMs, long inputTokens, long outputTokens, decimal? cost, AiAttemptOutcome outcome)
    {
        CallId = callId;
        Attempt = attempt;
        OwnerId = ownerId;
        DocumentId = documentId;
        Purpose = purpose;
        Model = model;
        PromptVersion = promptVersion;
        CalledAt = calledAt;
        DurationMs = durationMs;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Cost = cost;
        Outcome = outcome;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid CallId { get; private set; }

    // 1 for the first attempt of the call.
    public int Attempt { get; private set; }

    public string OwnerId { get; private set; }

    public Guid DocumentId { get; private set; }

    public string Purpose { get; private set; }

    public string Model { get; private set; }

    public string PromptVersion { get; private set; }

    // When the attempt started, UTC.
    public DateTime CalledAt { get; private set; }

    public long DurationMs { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    // In the provider's billing currency (US dollars). Null when unknown: a failed attempt, or a model without a price.
    public decimal? Cost { get; private set; }

    public AiAttemptOutcome Outcome { get; private set; }

    public static AiAttempt Of(
        Guid callId, int attempt, Document document, string purpose, string model, string promptVersion,
        DateTime calledAt, TimeSpan duration, long inputTokens, long outputTokens, decimal? cost, AiAttemptOutcome outcome) =>
        new(callId, attempt, document.OwnerId, document.Id, Cut(purpose, PurposeMaxLength), Cut(model, ModelMaxLength),
            Cut(promptVersion, PromptVersionMaxLength), calledAt, (long)duration.TotalMilliseconds, inputTokens, outputTokens, cost, outcome);

    // The model name comes from the provider, a long one must not lose the row.
    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
}

public enum AiAttemptOutcome
{
    Answered,

    Unusable,

    Failed,
}