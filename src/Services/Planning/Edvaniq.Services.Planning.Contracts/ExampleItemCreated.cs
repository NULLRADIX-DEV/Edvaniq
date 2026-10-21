using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Planning.Contracts;

// Example of an integration event, sent when an ExampleItem is created (docs/service-template.md#nachrichten). Replace it
// with the service's own events. It carries ids and the values a listener needs, nothing it could look up itself.
public sealed record ExampleItemCreated(Guid ExampleItemId, string OwnerId) : IIntegrationEvent;