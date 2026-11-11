using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// Asks for an answer in a fixed form and checks it, asking again when the answer is no use, up to MaxAttempts. Only an
// unusable answer counts for that: a provider that fails or takes too long ends at once and the result says whether it
// may pass, so the caller can try later without waiting here. The usage of every attempt adds up in the result,
// failed or not, and each attempt is in Tries with its own tokens, cost and duration.
public static class StructuredOutput
{
    // One more try catches a model's bad moment. Two bad answers in a row point to the prompt or the model, and a
    // third paid try rarely helps.
    public const int DefaultAttempts = 2;

    public static async Task<StructuredAnswer<T>> AskAsync<T>(
        this IChatClient ai,
        IReadOnlyList<ChatMessage> messages,
        Func<string, T?> read,
        StructuredRequest request,
        TimeProvider time,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxAttempts, 1);
        var usage = new UsageDetails();
        var tries = new List<AiTry>();
        var options = request.ResponseFormat is null ? null : new ChatOptions { ResponseFormat = request.ResponseFormat };
        string? model = null;
        while (tries.Count < request.MaxAttempts)
        {
            var startedAt = time.GetUtcNow().UtcDateTime;
            var started = time.GetTimestamp();
            ChatResponse response;
            try
            {
                using var timeout = new CancellationTokenSource(request.AttemptTimeout, time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                response = await ai.GetResponseAsync(messages, options, linked.Token);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                tries.Add(new AiTry(tries.Count + 1, startedAt, time.GetElapsedTime(started), null, 0, 0, null, AiTryOutcome.Failed));
                return new StructuredAnswer<T>(null, tries.Count, usage, model, exception, tries);
            }

            if (response.Usage is { } used)
            {
                usage.Add(used);
            }

            model = response.ModelId ?? model;
            var value = read(response.Text);
            tries.Add(new AiTry(
                tries.Count + 1, startedAt, time.GetElapsedTime(started), response.ModelId,
                response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0, AiCost.Of(response),
                value is null ? AiTryOutcome.Unusable : AiTryOutcome.Answered));
            if (value is not null)
            {
                return new StructuredAnswer<T>(value, tries.Count, usage, model, null, tries);
            }
        }

        return new StructuredAnswer<T>(null, tries.Count, usage, model, null, tries);
    }
}

// How to ask: how often at most, how long each answer may take, and the form the provider should keep to, if any.
public sealed record StructuredRequest(TimeSpan AttemptTimeout, ChatResponseFormat? ResponseFormat = null, int MaxAttempts = StructuredOutput.DefaultAttempts);

// Value is null when no attempt gave a usable answer. Failure is set when the provider failed or took too long, then
// Attempts includes that attempt.
public sealed record StructuredAnswer<T>(
    T? Value, int Attempts, UsageDetails Usage, string? ModelId, Exception? Failure, IReadOnlyList<AiTry> Tries)
    where T : class
{
    // The provider failed in a way that may pass, asking later may help (AiFailure.IsTransient).
    public bool Transient => Failure is not null && AiFailure.IsTransient(Failure);
}

// One request to the provider. ModelId is the one that answered, null when it didn't say or nothing came back. Cost is
// null when it is unknown: a failed attempt, or a model without a price.
public sealed record AiTry(
    int Number, DateTime StartedAt, TimeSpan Duration, string? ModelId, long InputTokens, long OutputTokens, decimal? Cost, AiTryOutcome Outcome);

public enum AiTryOutcome
{
    // The answer was usable.
    Answered,

    // An answer came, but it was no use.
    Unusable,

    // The provider failed or took too long.
    Failed,
}