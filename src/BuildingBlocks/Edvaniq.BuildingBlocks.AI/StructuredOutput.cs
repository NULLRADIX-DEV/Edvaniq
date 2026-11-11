using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// Asks for an answer in a fixed form and checks it, asking again when the answer is no use, up to MaxAttempts. Only an
// unusable answer counts for that: a provider that fails or takes too long ends at once, every new request would be
// another paid one (outages are #300). The usage of every attempt adds up in the result, failed or not.
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
        var options = request.ResponseFormat is null ? null : new ChatOptions { ResponseFormat = request.ResponseFormat };
        string? model = null;
        var attempts = 0;
        try
        {
            while (attempts < request.MaxAttempts)
            {
                attempts++;
                using var timeout = new CancellationTokenSource(request.AttemptTimeout, time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                var response = await ai.GetResponseAsync(messages, options, linked.Token);
                if (response.Usage is { } used)
                {
                    usage.Add(used);
                }

                model = response.ModelId ?? model;
                if (read(response.Text) is { } value)
                {
                    return new StructuredAnswer<T>(value, attempts, usage, model, null);
                }
            }

            return new StructuredAnswer<T>(null, attempts, usage, model, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new StructuredAnswer<T>(null, attempts, usage, model, exception);
        }
    }
}

// How to ask: how often at most, how long each answer may take, and the form the provider should keep to, if any.
public sealed record StructuredRequest(TimeSpan AttemptTimeout, ChatResponseFormat? ResponseFormat = null, int MaxAttempts = StructuredOutput.DefaultAttempts);

// Value is null when no attempt gave a usable answer. Failure is set when the provider failed or took too long, then
// Attempts includes that attempt.
public sealed record StructuredAnswer<T>(T? Value, int Attempts, UsageDetails Usage, string? ModelId, Exception? Failure)
    where T : class;