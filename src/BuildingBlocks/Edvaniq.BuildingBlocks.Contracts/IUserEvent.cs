namespace Edvaniq.BuildingBlocks.Contracts;

// An event about one user's data. Once that user is deleted, consumers confirm it without running their handler, so a
// late event can't bring anything back (docs/service-internals.md#nutzer-löschen). UserId is the token's sub.
public interface IUserEvent : IIntegrationEvent
{
    string UserId { get; }
}