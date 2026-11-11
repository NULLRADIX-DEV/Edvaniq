using Edvaniq.Client.UI.Shell;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

internal static class TestAnswerSync
{
    // For tests about something else: it asks LearningEngine once and gives up at once, the page shows its hint.
    public static IServiceCollection AddQuietAnswerSync(this IServiceCollection services) =>
        services.AddSingleton(new AnswerSync(new FakeLearningProgress(), timing: new AnswerSyncTiming(TimeSpan.FromMilliseconds(1), TimeSpan.Zero)));
}