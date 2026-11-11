using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;

namespace Edvaniq.Services.Content.Application;

// The remover finds every IOwnedByUser entity itself (docs/service-internals.md#nutzer-löschen). The AI costs stay
// only as daily sums without the user, in the same save.
public sealed class UserDeletedHandler(IAiUsageArchive aiUsage, IUserDataRemover userData) : IIntegrationEventHandler<UserDeleted>
{
    public Task HandleAsync(UserDeleted integrationEvent, CancellationToken cancellationToken)
    {
        aiUsage.Archive(integrationEvent.UserId);
        userData.RemoveAll(integrationEvent.UserId);
        return Task.CompletedTask;
    }
}