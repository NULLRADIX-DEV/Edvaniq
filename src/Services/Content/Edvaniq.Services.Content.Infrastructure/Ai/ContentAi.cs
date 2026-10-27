using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// The AI of Content: the provider from Ai:Provider and the simulated answers to Content's prompts.
public static class ContentAi
{
    // The first line of the system message of each task. The simulated provider tells the tasks apart by it, so every
    // real prompt for the task starts with it too.
    public const string TopicsTask = TopicAnalysis.Task;

    public const string QuestionsTask = "edvaniq:questions";

    public static TBuilder AddContentAi<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<ISimulatedAnswer, SimulatedTopics>();
        builder.Services.AddSingleton<ISimulatedAnswer, SimulatedQuestions>();
        return builder.AddAi();
    }

    internal static bool IsTask(SimulatedPrompt prompt, string task) =>
        prompt.System.Split('\n', 2)[0].Trim() == task;
}
