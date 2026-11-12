using System.Net;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// A provider without network and without key, for tests, local development and the walking skeleton on the server. It
// answers with the first registered ISimulatedAnswer that fits the prompt, the same answer for the same prompt.
public sealed class SimulatedChatClient(IEnumerable<ISimulatedAnswer> answers, SimulatedFault fault = SimulatedFault.None)
    : IChatClient
{
    public const string Provider = "Simulated";

    private readonly IReadOnlyList<ISimulatedAnswer> answers = [.. answers];

    // Tests switch it while the process runs.
    public SimulatedFault Fault { get; set; } = fault;

    // The context limit of a model, in characters of all messages together. A longer request fails like a provider that
    // answers 400 for a prompt that doesn't fit. Null means no limit. Tests set it.
    public int? PromptLimit { get; set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = new SimulatedPrompt(messages);
        if (PromptLimit is { } limit && prompt.Messages.Sum(message => message.Text.Length) > limit)
        {
            throw new HttpRequestException("The prompt is longer than the simulated context.", null, HttpStatusCode.BadRequest);
        }

        switch (Fault)
        {
            case SimulatedFault.Outage:
                throw new HttpRequestException("The simulated AI provider is unavailable.", null, HttpStatusCode.ServiceUnavailable);
            case SimulatedFault.Timeout:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
        }

        var answer = answers.FirstOrDefault(candidate => candidate.Answers(prompt))?.Answer(prompt)
            ?? throw new InvalidOperationException("No simulated answer fits this prompt. Register an ISimulatedAnswer for it.");
        if (Fault == SimulatedFault.InvalidOutput)
        {
            answer = answer[..(answer.Length / 2)];
        }

        // Costs nothing, but has a cost like a real answer.
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = Provider, Usage = EstimatedUsage(prompt, answer) };
        AiCost.Set(response, 0m);
        return response;
    }

    // Estimated, not counted: about four characters a token, so the usage of a document shows up without a real provider.
    internal static UsageDetails EstimatedUsage(SimulatedPrompt prompt, string answer)
    {
        long input = prompt.Messages.Sum(message => message.Text.Length) / 4;
        long output = answer.Length / 4;
        return new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };
    }

    // The whole answer as one update, nobody streams the simulated provider.
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text) { ModelId = Provider };
    }

    // The metadata names the model as a real client names the configured one.
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata(Provider, defaultModelId: Provider)
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }
}