using Edvaniq.Testing;

// One MySQL and one NATS container for the whole project. Every service gets its own database in it.
[assembly: AssemblyFixture(typeof(MySqlTestServer))]
[assembly: AssemblyFixture(typeof(NatsTestServer))]