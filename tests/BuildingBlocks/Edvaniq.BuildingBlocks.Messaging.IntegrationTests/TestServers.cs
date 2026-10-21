using Edvaniq.Testing;

// One MySQL and one NATS container for all tests of this project. Every test creates a database of its own in the first.
// The second is shared, so the tests that use it run one after the other (collection "nats") and clear their stream first.
[assembly: AssemblyFixture(typeof(MySqlTestServer))]
[assembly: AssemblyFixture(typeof(NatsTestServer))]