using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Sender.Contracts;

// A made-up service for the tests: its events live where the events of a real service live.
public sealed record Greeted(Guid GreetingId, string Text) : IIntegrationEvent;

// An event about one user's data.
public sealed record NoteWritten(string UserId, Guid NoteId) : IUserEvent;