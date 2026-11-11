using System.Net.Http.Headers;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.BuildingBlocks.AI;

public static class AiExtensions
{
    // Registers IChatClient for the provider in Ai:Provider (docs/architecture.md#ki-zugang). A missing or unknown
    // provider, or a real one without its settings, stops the start, so a typo never leaves a process that can't
    // analyse anything.
    public static TBuilder AddAi<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var provider = builder.Configuration["Ai:Provider"];
        switch (provider)
        {
            case SimulatedChatClient.Provider:
                // Ai:Simulated:Fault lets a running process fail on purpose, e.g. on the server for a demo of the error path.
                var fault = builder.Configuration.GetValue("Ai:Simulated:Fault", SimulatedFault.None);
                builder.Services.AddSingleton(services => new SimulatedChatClient(services.GetServices<ISimulatedAnswer>(), fault));
                builder.Services.AddSingleton<IChatClient>(services => services.GetRequiredService<SimulatedChatClient>());
                break;
            case OpenAiCompatibleChatClient.Provider:
                var settings = OpenAiCompatibleSettings.Read(builder.Configuration);
                // No standard resilience: its attempt timeout is far below an answer of a model, and a retry is a
                // second paid request. The caller sets the time limit. The call is still marked as evaluation.
#pragma warning disable EXTEXP0001
                builder.Services.AddHttpClient(OpenAiCompatibleChatClient.HttpClientName, client =>
                    {
                        client.BaseAddress = settings.Endpoint;
                        client.Timeout = Timeout.InfiniteTimeSpan;
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                    })
                    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
                builder.Services.AddSingleton<IChatClient>(services =>
                    new OpenAiCompatibleChatClient(services.GetRequiredService<IHttpClientFactory>(), settings));
                break;
            default:
                throw new InvalidOperationException(provider is null
                    ? $"Ai:Provider is missing. Set it to {Known}."
                    : $"Ai:Provider '{provider}' is unknown. Set it to {Known}.");
        }

        return builder;
    }

    private const string Known = $"{SimulatedChatClient.Provider} or {OpenAiCompatibleChatClient.Provider}";
}