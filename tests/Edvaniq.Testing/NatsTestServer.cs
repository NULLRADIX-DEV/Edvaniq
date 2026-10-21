using Testcontainers.Nats;

using Xunit;

namespace Edvaniq.Testing;

// One NATS container per test run, in the version the AppHost uses. The module starts it with JetStream switched on. Tests
// that share it use streams of their own or clear theirs first, as a stream outlives the test that made it. Needs Docker,
// like the AppHost. Register it once per test project: [assembly: AssemblyFixture(typeof(NatsTestServer))].
public sealed class NatsTestServer : IAsyncLifetime
{
    // Same image as the AppHost (Aspire.Hosting.Nats), NatsImageTests checks it.
    private const string Image = "nats:2.15";

    private readonly NatsContainer container = new NatsBuilder(Image).Build();

    public string Url => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public ValueTask DisposeAsync() => container.DisposeAsync();
}