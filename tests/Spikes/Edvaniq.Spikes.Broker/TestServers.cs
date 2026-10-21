using Edvaniq.Spikes.Broker;
using Edvaniq.Testing;

using Testcontainers.Nats;

[assembly: AssemblyFixture(typeof(MySqlTestServer))]
[assembly: AssemblyFixture(typeof(NatsTestServer))]

namespace Edvaniq.Spikes.Broker;

// The module starts NATS with JetStream switched on.
public sealed class NatsTestServer : IAsyncLifetime
{
    private readonly NatsContainer container = new NatsBuilder("nats:2.15").Build();

    public string Url => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public ValueTask DisposeAsync() => container.DisposeAsync();
}