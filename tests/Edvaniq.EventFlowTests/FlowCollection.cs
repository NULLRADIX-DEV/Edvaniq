namespace Edvaniq.EventFlowTests;

// One set of services for every test class. Two sets on the same NATS would share the durable consumers by name, and a
// message would end up in the other set's database.
[CollectionDefinition(Name)]
public sealed class FlowCollection : ICollectionFixture<FlowServices>
{
    public const string Name = "Flow";
}