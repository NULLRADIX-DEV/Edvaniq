using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;

namespace Edvaniq.Services.Content.Application;

// The remover finds every IOwnedByUser entity itself (docs/service-internals.md#nutzer-löschen). The AI costs stay
// only as daily sums without the user, in the same save. The files go first, before their rows: if the save fails, the
// redelivery finds the rows again and only the rows are left to remove.
public sealed class UserDeletedHandler(IAiUsageArchive aiUsage, IUserDataRemover userData, IUserFiles files) : IIntegrationEventHandler<UserDeleted>
{
    public async Task HandleAsync(UserDeleted integrationEvent, CancellationToken cancellationToken)
    {
        await files.DeleteAllOfAsync(integrationEvent.UserId, cancellationToken);
        aiUsage.Archive(integrationEvent.UserId);
        userData.RemoveAll(integrationEvent.UserId);
    }
}