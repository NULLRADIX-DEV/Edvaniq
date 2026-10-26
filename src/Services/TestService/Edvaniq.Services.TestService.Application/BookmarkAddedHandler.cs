using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.TestService.Contracts;

namespace Edvaniq.Services.TestService.Application;

// Hears the service's own event, so the way from the outbox to a handler is part of the test service. It must not call
// SaveChanges: the consumer saves its changes together with the inbox entry.
public sealed class BookmarkAddedHandler : IIntegrationEventHandler<BookmarkAdded>
{
    public Task HandleAsync(BookmarkAdded integrationEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}