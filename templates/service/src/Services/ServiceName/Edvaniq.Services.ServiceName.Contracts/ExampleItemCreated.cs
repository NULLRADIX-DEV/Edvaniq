using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.ServiceName.Contracts;

// Example of an integration event, sent when an ExampleItem is created (docs/service-internals.md#nachrichten). Replace it
// with the service's own events. It carries ids and the values a listener needs, nothing it could look up itself.
public sealed record ExampleItemCreated(Guid ExampleItemId, string OwnerId) : IUserEvent
{
    // Not on the broker: the contract names the field OwnerId.
    string IUserEvent.UserId => OwnerId;
}