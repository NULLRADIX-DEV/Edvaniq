using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Messaging;

// The DbContext of the service that holds its outbox and inbox. Background services have no generic argument for it,
// they get it from the scope they open.
internal sealed record MessagingStore(Type ContextType)
{
    public DbContext Resolve(IServiceProvider scope) => (DbContext)scope.GetRequiredService(ContextType);
}