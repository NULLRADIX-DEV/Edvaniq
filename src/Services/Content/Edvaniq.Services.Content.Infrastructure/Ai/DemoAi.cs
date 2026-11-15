using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// The AI of the demo documents (#411): it answers from the answers that ship with the demo material (DemoReplay) and
// asks no provider, so the demo costs nothing and works while the AI is paused or away. A prompt it doesn't know fails
// like a provider that refuses, the demo tests catch that before it ships.
public sealed class DemoAi : IChatClient
{
    public const string Model = "edvaniq-demo";

    public static DemoAi Client { get; } = new();

    private DemoAi()
    {
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var system = list.FirstOrDefault(message => message.Role == ChatRole.System)?.Text ?? "";
        var user = list.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? "";
        var answer = DemoReplay.Answer(system, user)
            ?? throw new InvalidOperationException("The demo answers have none for this prompt.");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = Model });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("edvaniq-demo", null, Model) : null;

    public void Dispose()
    {
    }
}