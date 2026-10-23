using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;

namespace Edvaniq.Services.ServiceName.Application;

// Removes the data of a deleted user from this service (docs/service-template.md#nutzer-löschen). It stays when the
// example is replaced: the remover finds every entity with IOwnedByUser by itself. Like every handler it stores nothing,
// the consumer saves the removal together with its inbox entry, and a second delivery finds nothing left to remove.
public sealed class UserDeletedHandler(IUserDataRemover userData) : IIntegrationEventHandler<UserDeleted>
{
    public Task HandleAsync(UserDeleted integrationEvent, CancellationToken cancellationToken)
    {
        userData.RemoveAll(integrationEvent.UserId);
        return Task.CompletedTask;
    }
}