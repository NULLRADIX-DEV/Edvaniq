using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.BuildingBlocks.AI;

public static class AiExtensions
{
    // Registers IChatClient for the provider in Ai:Provider (docs/architecture.md#ki-zugang). A missing or unknown
    // provider stops the start, so a typo never leaves a process that can't analyse anything.
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
            default:
                throw new InvalidOperationException(provider is null
                    ? "Ai:Provider is missing. Set it to Simulated, the only provider so far."
                    : $"Ai:Provider '{provider}' is unknown. The only provider so far is Simulated.");
        }

        return builder;
    }
}
