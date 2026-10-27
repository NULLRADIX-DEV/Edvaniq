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

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = new SimulatedPrompt(messages);
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

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = Provider };
    }

    // The whole answer as one update, nobody streams the simulated provider.
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text) { ModelId = Provider };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
