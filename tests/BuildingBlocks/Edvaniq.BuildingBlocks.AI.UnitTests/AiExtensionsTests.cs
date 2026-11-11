using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// Ai:Provider picks the provider. Simulated needs no key, an unknown one stops the start.
public class AiExtensionsTests
{
    [Fact]
    public void Simulated_NeedsNoKey()
    {
        using var host = Build(("Ai:Provider", "Simulated"));

        var client = Assert.IsType<SimulatedChatClient>(host.Services.GetRequiredService<IChatClient>());
        Assert.Equal(SimulatedFault.None, client.Fault);
        Assert.Same(client, host.Services.GetRequiredService<SimulatedChatClient>());
    }

    [Fact]
    public void Fault_ComesFromTheConfiguration()
    {
        using var host = Build(("Ai:Provider", "Simulated"), ("Ai:Simulated:Fault", "Timeout"));

        Assert.Equal(SimulatedFault.Timeout, host.Services.GetRequiredService<SimulatedChatClient>().Fault);
    }

    [Fact]
    public void MissingProvider_StopsTheStart() =>
        Assert.Equal(
            "Ai:Provider is missing. Set it to Simulated or OpenAiCompatible.",
            Assert.Throws<InvalidOperationException>(() => Build()).Message);

    [Fact]
    public void UnknownProvider_StopsTheStart() =>
        Assert.Equal(
            "Ai:Provider 'Remote' is unknown. Set it to Simulated or OpenAiCompatible.",
            Assert.Throws<InvalidOperationException>(() => Build(("Ai:Provider", "Remote"))).Message);

    private static IHost Build(params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings.Select(setting => KeyValuePair.Create(setting.Key, (string?)setting.Value)));
        builder.AddAi();
        return builder.Build();
    }
}
