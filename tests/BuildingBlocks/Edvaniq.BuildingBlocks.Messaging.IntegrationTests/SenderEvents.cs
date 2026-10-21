using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Sender.Contracts;

// A made-up service for the tests: its events live where the events of a real service live.
public sealed record Greeted(Guid GreetingId, string Text) : IIntegrationEvent;