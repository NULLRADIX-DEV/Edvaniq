using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Alpha.Contracts;

// A made-up service for the tests: its events live where the events of a real service live.
public sealed record Pinged(Guid Id, string Text) : IIntegrationEvent;