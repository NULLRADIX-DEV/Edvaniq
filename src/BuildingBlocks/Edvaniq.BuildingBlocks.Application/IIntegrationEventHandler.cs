using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.BuildingBlocks.Application;

// Reacts to an integration event of another service. The consumer stores its changes together with its inbox entry in one
// SaveChanges, after the handler has returned, so a handler must not call SaveChanges itself. An event can arrive more
// than once and in any order, the inbox only makes sure the effect happens once.
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}