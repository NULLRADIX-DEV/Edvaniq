using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Application;

namespace Edvaniq.Services.Knowledge.Api;

// A change the user makes to a topic, tried again when an analysis or another change of the same learning goal got its
// save in first (TopicSet). Each try starts over in a fresh scope, so it checks names against the topics as they are now.
// Three tries, 75 ms of waiting at most, then the user hears to try again.
internal static class TopicChange
{
    private const int Tries = 3;

    public static async Task<TopicResult> RunAsync(
        IServiceScopeFactory scopes, Func<ITopics, CancellationToken, Task<TopicResult>> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var scope = scopes.CreateScope();
            try
            {
                return await change(scope.ServiceProvider.GetRequiredService<ITopics>(), cancellationToken);
            }
            catch (MessageNotReadyException) when (attempt < Tries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (MessageNotReadyException)
            {
                return TopicResult.Fail(TopicError.Busy);
            }
        }
    }
}