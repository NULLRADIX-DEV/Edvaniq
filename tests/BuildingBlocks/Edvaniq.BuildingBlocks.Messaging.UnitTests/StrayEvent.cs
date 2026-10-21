using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// An event that is in no .Contracts of a service.
public sealed record Stray(Guid Id) : IIntegrationEvent;