using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;

namespace Edvaniq.Services.TestService.Application;

// Keep it when the example goes, the remover finds every IOwnedByUser entity itself (docs/service-internals.md#nutzer-löschen).
public sealed class UserDeletedHandler(IUserDataRemover userData) : IIntegrationEventHandler<UserDeleted>
{
    public Task HandleAsync(UserDeleted integrationEvent, CancellationToken cancellationToken)
    {
        userData.RemoveAll(integrationEvent.UserId);
        return Task.CompletedTask;
    }
}