using System.Diagnostics;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.AI;

// One short question to the configured provider, so an operator sees after a deploy that it answers. Prints only the
// model, the time and the tokens, never the answer or the key.
public static class AiCheck
{
    public const string Command = "ai-check";

    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output, TimeSpan? timeout = null)
    {
        var ai = services.GetRequiredService<IChatClient>();
        var provider = ai.GetService<ChatClientMetadata>()?.ProviderName ?? "unknown";
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(1));
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await ai.GetResponseAsync(
                [new ChatMessage(ChatRole.System, "Answer with one word."), new ChatMessage(ChatRole.User, "Say ok.")],
                cancellationToken: cancellation.Token);
            if (string.IsNullOrWhiteSpace(response.Text))
            {
                await output.WriteLineAsync($"AI check failed: provider {provider} sent an empty answer after {watch.ElapsedMilliseconds} ms.");
                return 1;
            }

            var model = response.ModelId ?? ai.GetService<ChatClientMetadata>()?.DefaultModelId ?? "unknown";
            var tokens = response.Usage?.TotalTokenCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            await output.WriteLineAsync($"AI answered: provider {provider}, model {model}, {watch.ElapsedMilliseconds} ms, {tokens} tokens.");
            return 0;
        }
        catch (Exception exception)
        {
            var reason = exception is OperationCanceledException ? "no answer in time." : $"{exception.GetType().Name}: {exception.Message}";
            await output.WriteLineAsync($"AI check failed: provider {provider}, after {watch.ElapsedMilliseconds} ms: {reason}");
            return 1;
        }
    }
}