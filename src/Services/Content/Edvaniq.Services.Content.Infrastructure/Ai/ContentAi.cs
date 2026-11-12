using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// The AI of Content: the provider from Ai:Provider and the simulated answers to Content's prompts.
public static class ContentAi
{
    // The first line of the system message of each task. The simulated provider tells the tasks apart by it, so every
    // real prompt for the task starts with it too.
    public const string TopicsTask = TopicAnalysis.Task;

    public const string QuestionsTask = QuestionGeneration.Task;

    // What the simulated provider writes into a question once text gets out of the material block (SimulatedQuestions).
    public const string SimulatedHijack = "PWNED";

    public static readonly ChatResponseFormat TopicsFormat = FormatOf(TopicAnalysis.Schema, "topics");

    public static readonly ChatResponseFormat QuestionsFormat = FormatOf(QuestionGeneration.Schema, "questions");

    public static TBuilder AddContentAi<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddContentTestDouble();
        return builder.AddAi();
    }

    // The simulated answers, and the simulated AI for documents of a test account whatever Ai:Provider says.
    public static IServiceCollection AddContentTestDouble(this IServiceCollection services)
    {
        services.AddSingleton<ISimulatedAnswer, SimulatedTopics>();
        services.AddSingleton<ISimulatedAnswer, SimulatedQuestions>();
        services.AddSingleton<TestDoubleAi>();
        return services;
    }

    // The AI for this document: the test double if it uses one, else the configured provider.
    internal static IChatClient For(Document document, IChatClient ai, TestDoubleAi testDouble) =>
        document.UsesTestDouble ? testDouble.Client : ai;

    // Puts the operator's emergency stop in front of the configured AI (PausableChatClient), the one place every call
    // to the provider passes. The test double of a test account calls no provider and stays. Call it after
    // AddContentAi, in a process with the database.
    public static TBuilder AddAiSwitch<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var provider = builder.Services.Last(service => service.ServiceType == typeof(IChatClient));
        var inner = provider.ImplementationFactory ?? throw new InvalidOperationException("The AI is registered without a factory.");
        builder.Services.Remove(provider);
        builder.Services.AddSingleton<IChatClient>(services =>
            new PausableChatClient((IChatClient)inner(services), services.GetRequiredService<IServiceScopeFactory>()));
        return builder;
    }

    // The model that answered. Some providers leave it out of the answer, then it is the configured one, never empty.
    internal static string ModelOf(IChatClient ai, string? answered) =>
        new[] { answered, ai.GetService<ChatClientMetadata>()?.DefaultModelId, ai.GetService<ChatClientMetadata>()?.ProviderName }
            .FirstOrDefault(model => !string.IsNullOrWhiteSpace(model)) ?? "unknown";

    private static ChatResponseFormat FormatOf(string schema, string name)
    {
        using var json = JsonDocument.Parse(schema);
        return ChatResponseFormat.ForJsonSchema(json.RootElement.Clone(), name);
    }

    internal static bool IsTask(SimulatedPrompt prompt, string task) =>
        prompt.System.Split('\n', 2)[0].Trim() == task;
}