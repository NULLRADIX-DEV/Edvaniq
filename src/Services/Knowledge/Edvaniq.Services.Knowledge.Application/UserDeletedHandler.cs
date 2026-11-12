using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;

namespace Edvaniq.Services.Knowledge.Application;

// The remover finds every IOwnedByUser entity itself (docs/service-internals.md#nutzer-löschen).
public sealed class UserDeletedHandler(IUserDataRemover userData) : IIntegrationEventHandler<UserDeleted>, IUserDeletion
{
    public Task HandleAsync(UserDeleted integrationEvent, CancellationToken cancellationToken) =>
        DeleteAsync(integrationEvent.UserId, cancellationToken);

    public Task DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        userData.RemoveAll(userId);
        return Task.CompletedTask;
    }
}